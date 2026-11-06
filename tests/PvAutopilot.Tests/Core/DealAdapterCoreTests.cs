using System.Text.Json;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Processes.Steps;
using PvAutopilot.Core.Sources;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Tests.Core;

// Kern-Anteile des HubSpot-Adapters auf Deal-Basis: Errichter aus der Instanz, Defaults, Audit-Stufen, Ablage der Datenkarte.

public class ErrichterAndDefaultsTests
{
    private static readonly Anlagenerrichter DealErrichter = new("Deal Elektro GmbH", "Essen", "E-999", "rheinnetz");

    private CrmProjectData _crm = new()
    {
        PlanningProjectId = "reonic-1",
        Anlagenbetreiber = TestData.Card().Anlagenbetreiber,
        Anlagenadresse = TestData.Card().Anlagenadresse,
    };

    private ErrichterOptions _instance = new()
    {
        Firma = "Instanz Solar GmbH", Ort = "Duisburg", Eintragungsnummer = "E-123", EingetragenBeiNetzbetreiber = "netze-duisburg",
    };

    private Task<DataCardAggregation> Aggregate() =>
        new NabDataCardAggregator(new Crm(_crm), new Planning(), new NoDocuments(), Options.Create(_instance)).AggregateAsync("crm-1", default);

    [Fact]
    public async Task Deal_errichter_wins_over_the_instance_configuration()
    {
        _crm = _crm with { Errichter = DealErrichter };

        var result = await Aggregate();

        Assert.True(result.IsComplete);
        Assert.Equal(DealErrichter, result.Card!.Errichter);
    }

    [Fact]
    public async Task Without_deal_errichter_the_instance_errichter_is_used()
    {
        var result = await Aggregate();

        Assert.True(result.IsComplete);
        Assert.Equal(new Anlagenerrichter("Instanz Solar GmbH", "Duisburg", "E-123", "netze-duisburg"), result.Card!.Errichter);
    }

    [Fact]
    public async Task Instance_errichter_without_optional_fields_has_them_empty()
    {
        _instance = new ErrichterOptions { Firma = "Instanz Solar GmbH", Ort = "Duisburg" };

        var result = await Aggregate();

        Assert.Equal(new Anlagenerrichter("Instanz Solar GmbH", "Duisburg", null, null), result.Card!.Errichter);
    }

    [Fact]
    public async Task Neither_deal_nor_instance_errichter_is_a_missing_field()
    {
        _instance = new ErrichterOptions();

        var result = await Aggregate();

        Assert.False(result.IsComplete);
        Assert.Null(result.Card);
        Assert.Contains("Anlagenerrichter", result.MissingFields);
    }

    [Theory]
    [InlineData("", "Duisburg")]
    [InlineData("Instanz Solar GmbH", "")]
    [InlineData("  ", "  ")]
    public async Task Incomplete_instance_errichter_counts_as_not_configured(string firma, string ort)
    {
        _instance = new ErrichterOptions { Firma = firma, Ort = ort };

        Assert.Contains("Anlagenerrichter", (await Aggregate()).MissingFields);
    }

    [Fact]
    public async Task Empty_feed_in_type_and_fuse_get_the_defaults()
    {
        var result = await Aggregate();

        Assert.Equal(Einspeiseart.Teileinspeisung, result.Card!.Einspeiseart);
        Assert.Equal(63, result.Card.NhSicherungAmpere);
    }

    [Fact]
    public async Task Values_from_the_deal_replace_the_defaults()
    {
        _crm = _crm with { Einspeiseart = Einspeiseart.Volleinspeisung, NhSicherungAmpere = 100 };

        var result = await Aggregate();

        Assert.Equal(Einspeiseart.Volleinspeisung, result.Card!.Einspeiseart);
        Assert.Equal(100, result.Card.NhSicherungAmpere);
    }

    [Fact]
    public async Task Source_problems_make_the_card_incomplete_and_are_listed()
    {
        _crm = _crm with { Problems = ["HubSpot: Einspeiseart: unbekannter Auswahlwert"] };

        var result = await Aggregate();

        Assert.False(result.IsComplete);
        Assert.Contains("HubSpot: Einspeiseart: unbekannter Auswahlwert", result.MissingFields);
    }

    [Fact]
    public async Task Source_problems_are_kept_when_the_reonic_id_is_missing()
    {
        _crm = new CrmProjectData { Problems = ["HubSpot: Deal nicht gefunden"] };

        var result = await Aggregate();

        Assert.Null(result.Card);
        Assert.Contains("HubSpot: Deal nicht gefunden", result.MissingFields);
        Assert.Contains("Reonic-Projekt-ID", result.MissingFields);
    }

    private sealed class Crm(CrmProjectData data) : ICrmProjectSource
    {
        public Task<CrmProjectData> GetProjectAsync(string crmProjectId, CancellationToken ct) => Task.FromResult(data);
    }

    private sealed class Planning : IPlanningDataSource
    {
        public Task<PlanningData> GetPlanningDataAsync(string planningProjectId, CancellationToken ct)
        {
            var card = TestData.Card();
            return Task.FromResult(new PlanningData
            {
                Installationsart = Installationsart.Neuerrichtung,
                Hauptzaehlernummer = card.Hauptzaehlernummer,
                Module = card.Module,
                Wechselrichter = card.Wechselrichter,
            });
        }
    }

    private sealed class NoDocuments : IProductDocumentSource
    {
        public Task<IReadOnlyList<DocumentRef>> GetProductDocumentsAsync(string hersteller, string modell, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DocumentRef>>([]);
    }
}

public class AuditLevelTests
{
    [Fact]
    public void Blocked_is_an_error()
    {
        Assert.Equal(AuditLevel.Error, AuditLevels.ForStateChange(ProcessState.Blocked));
    }

    [Theory]
    [InlineData(ProcessState.Created)]
    [InlineData(ProcessState.DataCardAggregated)]
    [InlineData(ProcessState.NetzbetreiberResolved)]
    public void Internal_intermediate_steps_are_verbose(ProcessState state)
    {
        Assert.Equal(AuditLevel.Verbose, AuditLevels.ForStateChange(state));
    }

    [Fact]
    public void Every_other_state_is_info()
    {
        var others = Enum.GetValues<ProcessState>().Except(
            [ProcessState.Blocked, ProcessState.Created, ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved]);

        Assert.NotEmpty(others);
        Assert.All(others, state => Assert.Equal(AuditLevel.Info, AuditLevels.ForStateChange(state)));
    }

    [Fact]
    public void Levels_are_ordered_so_that_a_filter_can_compare_them()
    {
        Assert.True(AuditLevel.Verbose < AuditLevel.Info);
        Assert.True(AuditLevel.Info < AuditLevel.Error);
    }

    [Fact]
    public async Task Engine_audits_state_changes_with_the_level_of_the_new_state()
    {
        var h = new ProcessHarness();
        var process = h.Store.Add(new AnmeldeProcess(ProcessHarness.ProjectId, TestData.Now));

        await h.Engine().RunAsync(process.Id, default);

        var changes = h.Audit.Events.Where(e => e.Type == "state-changed").ToList();
        Assert.Equal([AuditLevel.Verbose, AuditLevel.Verbose, AuditLevel.Info], changes.Select(e => e.Level));
    }

    [Fact]
    public async Task Blocking_is_audited_as_error_and_human_actions_as_info()
    {
        var h = new ProcessHarness();
        var process = h.Store.Add(new AnmeldeProcess(ProcessHarness.ProjectId, TestData.Now));
        var failure = new ProcessFailureHandler(h.Store, new CountingAlert(), h.Tasks, h.CrmStatus, new AuditLog([h.Audit]), h.Time,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProcessFailureHandler>.Instance);

        await failure.BlockAsync(process.Id, new HttpRequestException("HTTP 503"), default);

        Assert.Equal(AuditLevel.Error, Assert.Single(h.Audit.Events, e => e.Type == "blocked").Level);
    }
}

public class AggregateDataCardStepArchiveTests
{
    private readonly CapturingArchive _archive = new();
    private readonly FixedTime _time = new(TestData.Now);

    private AggregateDataCardStep Step(IDocumentArchive? archive = null) =>
        new(new NabDataCardAggregator(new FakeCrmSource(), new FakePlanningSource(), new NoProductDocuments(), Options.Create(new ErrichterOptions())),
            archive ?? _archive, _time);

    [Fact]
    public async Task Data_card_is_archived_as_json_named_after_the_revision()
    {
        var process = new AnmeldeProcess("crm-1", TestData.Now);

        var result = await Step().ExecuteAsync(process, default);

        Assert.IsType<StepResult.ContinueResult>(result);
        Assert.Equal(ProcessState.DataCardAggregated, process.State);
        var (crmId, documents) = Assert.Single(_archive.Calls);
        Assert.Equal("crm-1", crmId);
        var document = Assert.Single(documents);
        Assert.Equal("nab-datenkarte-r0.json", document.FileName);
        Assert.Equal("application/json", document.ContentType);
        using var json = JsonDocument.Parse(document.Content);
        Assert.Equal("Teileinspeisung", json.RootElement.GetProperty("einspeiseart").GetString());
        Assert.Equal(63, json.RootElement.GetProperty("nhSicherungAmpere").GetInt32());
    }

    [Fact]
    public void Each_revision_gets_its_own_file_name()
    {
        Assert.Equal("nab-datenkarte-r0.json", AggregateDataCardStep.DataCardDocument(0, TestData.Card()).FileName);
        Assert.Equal("nab-datenkarte-r2.json", AggregateDataCardStep.DataCardDocument(2, TestData.Card()).FileName);
    }

    [Fact]
    public void Same_card_gives_identical_bytes_so_that_the_archive_can_deduplicate()
    {
        var a = AggregateDataCardStep.DataCardDocument(1, TestData.Card());
        var b = AggregateDataCardStep.DataCardDocument(1, TestData.Card());

        Assert.Equal(a.Content, b.Content);
    }

    [Fact]
    public async Task Incomplete_data_card_is_not_archived()
    {
        var incomplete = new Core_IncompleteCrm();
        var step = new AggregateDataCardStep(
            new NabDataCardAggregator(incomplete, new FakePlanningSource(), new NoProductDocuments(), Options.Create(new ErrichterOptions())),
            _archive, _time);
        var process = new AnmeldeProcess("crm-1", TestData.Now);

        var result = await step.ExecuteAsync(process, default);

        Assert.IsType<StepResult.HumanActionResult>(result);
        Assert.Empty(_archive.Calls);
        Assert.Equal(ProcessState.Created, process.State);
    }

    [Fact]
    public async Task Failing_archive_keeps_the_process_in_created_for_the_retry()
    {
        var process = new AnmeldeProcess("crm-1", TestData.Now);

        await Assert.ThrowsAsync<HttpRequestException>(() => Step(new ThrowingArchive()).ExecuteAsync(process, default));

        Assert.Equal(ProcessState.Created, process.State);
    }

    [Fact]
    public async Task Process_run_archives_the_data_card_once()
    {
        var h = new ProcessHarness();
        var process = h.Store.Add(new AnmeldeProcess(ProcessHarness.ProjectId, TestData.Now));

        await h.Engine().RunAsync(process.Id, default);
        await h.Engine().RunAsync(process.Id, default);

        Assert.Equal(1, h.DataCardArchive.Stored);
    }

    private sealed class CapturingArchive : IDocumentArchive
    {
        public List<(string CrmId, IReadOnlyList<GeneratedDocument> Documents)> Calls { get; } = [];

        public Task StoreAsync(string crmProjectId, IReadOnlyList<GeneratedDocument> documents, CancellationToken ct)
        {
            Calls.Add((crmProjectId, documents));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingArchive : IDocumentArchive
    {
        public Task StoreAsync(string crmProjectId, IReadOnlyList<GeneratedDocument> documents, CancellationToken ct) =>
            throw new HttpRequestException("HTTP 503");
    }

    private sealed class Core_IncompleteCrm : ICrmProjectSource
    {
        public Task<CrmProjectData> GetProjectAsync(string crmProjectId, CancellationToken ct) =>
            Task.FromResult(new CrmProjectData { PlanningProjectId = "reonic-1", Problems = ["HubSpot: Anlagenadresse fehlt"] });
    }
}
