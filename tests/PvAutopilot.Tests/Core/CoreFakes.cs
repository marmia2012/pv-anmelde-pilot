using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Mastr;
using PvAutopilot.Core.Netzbetreiber;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Processes.Steps;
using PvAutopilot.Core.Sources;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Tests.Core;

// Gemeinsame Fakes für die Kern-Tests. Fiktive Daten, keine echten Systeme.

internal sealed class InMemoryProcessStore : IProcessStore
{
    private readonly Dictionary<Guid, AnmeldeProcess> _processes = [];

    public AnmeldeProcess Add(AnmeldeProcess process)
    {
        _processes[process.Id] = process;
        return process;
    }

    public Task<AnmeldeProcess?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(_processes.GetValueOrDefault(id));

    public Task<AnmeldeProcess> GetOrCreateAsync(string crmProjectId, CancellationToken ct) =>
        Task.FromResult(_processes.Values.FirstOrDefault(p => p.CrmProjectId == crmProjectId)
            ?? Add(new AnmeldeProcess(crmProjectId, TestData.Now)));

    public Task<AnmeldeProcess?> FindByCrmProjectAsync(string crmProjectId, CancellationToken ct) =>
        Task.FromResult(_processes.Values.FirstOrDefault(p => p.CrmProjectId == crmProjectId));

    public Task<IReadOnlyList<AnmeldeProcess>> ListOpenAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AnmeldeProcess>>(_processes.Values.Where(p => !p.State.IsTerminal()).ToList());

    public Task SaveAsync(AnmeldeProcess process, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class RecordingScheduler : IProcessScheduler
{
    public List<Guid> Enqueued { get; } = [];
    public void Enqueue(Guid processId) => Enqueued.Add(processId);
}

/// <summary>Verhält sich wie <c>EfSubmissionLedger</c>.</summary>
internal sealed class InMemoryLedger : ISubmissionLedger
{
    public Dictionary<string, SubmissionRecord> Records { get; } = [];

    public Task<SubmissionRecord?> FindAsync(string key, CancellationToken ct) => Task.FromResult(Records.GetValueOrDefault(key));

    public Task<IReadOnlyList<SubmissionRecord>> ListByProcessAsync(Guid processId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SubmissionRecord>>(Records.Values.Where(r => r.ProcessId == processId).ToList());

    public Task RecordDryRunAsync(SubmissionKey key, string channel, CancellationToken ct)
    {
        if (Records.TryGetValue(key.Value, out var existing) && existing.Status == SubmissionStatus.Failed)
            return Task.CompletedTask;
        if (existing is not null && existing.Status != SubmissionStatus.DryRun)
            throw new InvalidOperationException($"{key.Value} hat Status {existing.Status}.");
        Records[key.Value] = New(key, channel, SubmissionStatus.DryRun);
        return Task.CompletedTask;
    }

    public Task<bool> TryReserveAsync(SubmissionKey key, string channel, CancellationToken ct)
    {
        if (Records.TryGetValue(key.Value, out var r) && r.Status is SubmissionStatus.Sending or SubmissionStatus.Sent)
            return Task.FromResult(false);
        Records[key.Value] = New(key, channel, SubmissionStatus.Sending);
        return Task.FromResult(true);
    }

    public Task MarkSentAsync(string key, string? externalReference, CancellationToken ct)
    {
        Records[key].Status = SubmissionStatus.Sent;
        Records[key].ExternalReference = externalReference ?? Records[key].ExternalReference;
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(string key, CancellationToken ct)
    {
        Records[key].Status = SubmissionStatus.Failed;
        return Task.CompletedTask;
    }

    public Task<bool> ResolveSendingAsync(string key, SubmissionStatus outcome, string? externalReference, CancellationToken ct)
    {
        if (outcome is not (SubmissionStatus.Sent or SubmissionStatus.Failed))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        if (!Records.TryGetValue(key, out var r) || r.Status != SubmissionStatus.Sending)
            return Task.FromResult(false);
        r.Status = outcome;
        if (outcome == SubmissionStatus.Sent)
            r.ExternalReference = externalReference ?? r.ExternalReference;
        return Task.FromResult(true);
    }

    public void Put(SubmissionKey key, SubmissionStatus status, string? reference = null)
    {
        var record = New(key, "fake", status);
        record.ExternalReference = reference;
        Records[key.Value] = record;
    }

    private static SubmissionRecord New(SubmissionKey key, string channel, SubmissionStatus status) => new()
    {
        IdempotencyKey = key.Value, ProcessId = key.ProcessId, Kind = key.Kind, Channel = channel, Status = status,
    };
}

internal sealed record HumanTask(string CrmProjectId, string Key, string Title, string Body);

internal sealed class RecordingTasks : IHumanTaskSink
{
    /// <summary>Alle Aufrufe; der echte Sink legt je Schlüssel nur einen Task an.</summary>
    public List<HumanTask> Calls { get; } = [];
    public IReadOnlyList<string> Keys => Calls.Select(t => t.Key).Distinct().ToList();

    public Task CreateTaskAsync(string crmProjectId, string taskKey, string title, string body, CancellationToken ct)
    {
        Calls.Add(new HumanTask(crmProjectId, taskKey, title, body));
        return Task.CompletedTask;
    }
}

internal sealed class CountingAlert : IOperatorAlert
{
    public int Count { get; private set; }

    public Task SendAsync(string subject, string body, byte[]? screenshot, CancellationToken ct)
    {
        Count++;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingCrmStatus : ICrmStatusSink
{
    public List<ProcessState> Published { get; } = [];

    public Task PublishAsync(AnmeldeProcess process, CancellationToken ct)
    {
        Published.Add(process.State);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingAudit : IAuditSink
{
    public List<AuditEvent> Events { get; } = [];

    public Task WriteAsync(AnmeldeProcess process, AuditEvent evt, CancellationToken ct)
    {
        Events.Add(evt);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingArchive : IDocumentArchive
{
    public int Stored { get; private set; }

    public Task StoreAsync(string crmProjectId, IReadOnlyList<GeneratedDocument> documents, CancellationToken ct)
    {
        Stored++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeChannel : ISubmissionChannel
{
    public List<ApplicationPackage> Sent { get; } = [];
    public Exception? Failure { get; set; }
    public string? Reference { get; set; } = "NB-4711";
    public string Name => "fake";

    public Task<SubmissionReceipt> SendAsync(ApplicationPackage package, CancellationToken ct)
    {
        if (Failure is not null)
            throw Failure;
        Sent.Add(package);
        return Task.FromResult(new SubmissionReceipt(Reference));
    }
}

internal sealed class FakeAdapter : INetzbetreiberAdapter
{
    public const string TestCode = "test-nb";

    public string Code => TestCode;
    public FakeChannel FakeChannel { get; } = new();
    public ISubmissionChannel Channel => FakeChannel;

    public IReadOnlySet<SubmissionKind> Einreichungen { get; set; } =
        new HashSet<SubmissionKind> { SubmissionKind.Netzanmeldung, SubmissionKind.Inbetriebnahme, SubmissionKind.Zaehlerwechsel };

    public List<string> Problems { get; } = [];

    public IReadOnlyList<string> Validate(SubmissionKind kind, NabDataCard card) => Problems;

    public Task<ApplicationPackage> PrepareAsync(SubmissionKind kind, AnmeldeProcess process, CancellationToken ct) =>
        Task.FromResult(new ApplicationPackage(process.Id, kind, Code,
            [new GeneratedDocument($"{kind}.pdf", "application/pdf", [1])], []));
}

internal sealed class FakeResolver(string? code = FakeAdapter.TestCode) : INetzbetreiberResolver
{
    public Task<string?> ResolveAsync(PostalAddress address, CancellationToken ct) => Task.FromResult(code);
}

internal sealed class FakeMastrRegistrar : IMastrRegistrar
{
    public int Calls { get; private set; }
    public Exception? Failure { get; set; }
    public MastrRegistration Result { get; set; } = new("SEE900000000001", "SSE900000000002");

    public Task<MastrRegistration> RegisterAsync(AnmeldeProcess process, MastrCredentials credentials, CancellationToken ct)
    {
        if (Failure is not null)
            throw Failure;
        Calls++;
        return Task.FromResult(Result);
    }
}

internal sealed class FakeCredentialStore : IMastrCredentialStore
{
    public Dictionary<string, MastrCredentials> Logins { get; } = [];
    public int Reads { get; private set; }
    public int Deletes { get; private set; }

    public Task<MastrCredentials?> GetAsync(string crmProjectId, CancellationToken ct)
    {
        Reads++;
        return Task.FromResult(Logins.GetValueOrDefault(crmProjectId));
    }

    public Task DeleteAsync(string crmProjectId, CancellationToken ct)
    {
        Deletes++;
        Logins.Remove(crmProjectId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeCrmSource : ICrmProjectSource
{
    public Task<CrmProjectData> GetProjectAsync(string crmProjectId, CancellationToken ct)
    {
        var card = TestData.Card();
        return Task.FromResult(new CrmProjectData
        {
            PlanningProjectId = "reonic-1",
            Anlagenbetreiber = card.Anlagenbetreiber,
            Anlagenadresse = card.Anlagenadresse,
            Errichter = card.Errichter,
        });
    }
}

internal sealed class FakePlanningSource : IPlanningDataSource
{
    public int Calls { get; private set; }
    public List<string> Problems { get; } = [];

    public Task<PlanningData> GetPlanningDataAsync(string planningProjectId, CancellationToken ct)
    {
        Calls++;
        var card = TestData.Card();
        return Task.FromResult(new PlanningData
        {
            Installationsart = Installationsart.Neuerrichtung,
            Hauptzaehlernummer = card.Hauptzaehlernummer,
            Module = card.Module,
            Wechselrichter = card.Wechselrichter,
            Problems = Problems,
        });
    }
}

internal sealed class NoProductDocuments : IProductDocumentSource
{
    public Task<IReadOnlyList<DocumentRef>> GetProductDocumentsAsync(string hersteller, string modell, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DocumentRef>>([]);
}

/// <summary>Verdrahtet Engine, Schritte und Trigger-Handler mit Fakes; <see cref="DrainAsync"/> spielt Hangfire.</summary>
internal sealed class ProcessHarness
{
    public const string ProjectId = "crm-1";

    public FixedTime Time { get; } = new(TestData.Now);
    public SubmissionOptions Submission { get; } = new() { Mode = SubmissionMode.Live };
    public InMemoryProcessStore Store { get; } = new();
    public RecordingScheduler Scheduler { get; } = new();
    public InMemoryLedger Ledger { get; } = new();
    public RecordingTasks Tasks { get; } = new();
    public RecordingCrmStatus CrmStatus { get; } = new();
    public RecordingAudit Audit { get; } = new();
    public RecordingArchive Archive { get; } = new();
    /// <summary>Ablage der Datenkarte, getrennt von <see cref="Archive"/> (Antragsunterlagen).</summary>
    public RecordingArchive DataCardArchive { get; } = new();
    public FakeAdapter Adapter { get; } = new();
    public FakeMastrRegistrar Registrar { get; } = new();
    public FakeCredentialStore Credentials { get; } = new();
    public FakePlanningSource Planning { get; } = new();

    public ProcessHarness(SubmissionMode mode = SubmissionMode.Live)
    {
        Submission.Mode = mode;
        Credentials.Logins[ProjectId] = new MastrCredentials("kunde", "geheim");
    }

    public SubmissionGate Gate() =>
        new(Ledger, Options.Create(Submission), NullLogger<SubmissionGate>.Instance);

    public NetzbetreiberSubmissionStep SubmissionStep(SubmissionStage stage) =>
        new(stage, [Adapter], Gate(), Archive, Options.Create(Submission), Time);

    public MastrRegistrationStep MastrStep() =>
        new(Registrar, Credentials, Ledger, Gate(), Options.Create(Submission), Time);

    public IReadOnlyList<IProcessStep> Steps() =>
    [
        new AggregateDataCardStep(new NabDataCardAggregator(new FakeCrmSource(), Planning, new NoProductDocuments(), Options.Create(new ErrichterOptions())), DataCardArchive, Time),
        new ResolveNetzbetreiberStep(new FakeResolver(), [Adapter], Time),
        SubmissionStep(SubmissionStages.Netzanmeldung),
        new AwaitNetzbetreiberResponseStep(Time),
        new AwaitInbetriebnahmeStep(Time),
        SubmissionStep(SubmissionStages.Inbetriebnahme),
        SubmissionStep(SubmissionStages.Zaehlerwechsel),
        new AwaitZaehlerwechselStep(Time),
        MastrStep(),
        new CompleteStep(Time),
    ];

    public ProcessEngine Engine() =>
        new(Store, Steps(), Ledger, Tasks, CrmStatus, new AuditLog([Audit]), Time, NullLogger<ProcessEngine>.Instance);

    public ProcessTriggerHandler Handler() =>
        new(Store, Scheduler, CrmStatus, Time, NullLogger<ProcessTriggerHandler>.Instance);

    public ProcessWatchdog Watchdog() =>
        new(Store, Scheduler, Tasks, Options.Create(new ProcessWatchdogOptions()), Options.Create(Submission), Time,
            NullLogger<ProcessWatchdog>.Instance);

    public AnmeldeProcess Process => Store.FindByCrmProjectAsync(ProjectId, default).Result!;

    /// <summary>Ereignis annehmen und alle eingeplanten Läufe ausführen.</summary>
    public async Task<TriggerResult> TriggerAsync(ProcessTriggerKind kind, SubmissionKind? submission = null, DateOnly? date = null,
        RueckfrageResolution? resolution = null, string? reference = null, DateTimeOffset? occurredAt = null)
    {
        var result = await Handler().HandleAsync(new ProcessTrigger(kind, ProjectId)
        {
            Submission = submission, Date = date, Resolution = resolution, Reference = reference, OccurredAt = occurredAt,
        }, default);
        await DrainAsync();
        return result;
    }

    public async Task DrainAsync()
    {
        var engine = Engine();
        while (Scheduler.Enqueued.Count > 0)
        {
            var id = Scheduler.Enqueued[0];
            Scheduler.Enqueued.RemoveAt(0);
            await engine.RunAsync(id, default);
        }
    }
}

internal sealed class RecordingAlert : IOperatorAlert
{
    public List<string> Bodies { get; } = [];

    public Task SendAsync(string subject, string body, byte[]? screenshot, CancellationToken ct)
    {
        Bodies.Add(body);
        return Task.CompletedTask;
    }
}
