using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PvAutopilot.Core;
using PvAutopilot.Core.Mastr;
using PvAutopilot.Core.Netzbetreiber;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Processes.Steps;
using PvAutopilot.Core.Sources;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Tests.Core;

public class ProcessEngineTests
{
    private readonly ProcessHarness _h = new();

    private AnmeldeProcess Add(params ProcessState[] path)
    {
        var process = _h.Store.Add(new AnmeldeProcess(ProcessHarness.ProjectId, TestData.Now));
        foreach (var state in path)
            process.TransitionTo(state, TestData.Now);
        return process;
    }

    // Schrittzuständigkeit

    [Fact]
    public void Registered_steps_cover_every_open_state_exactly_once()
    {
        using var provider = CoreServices();
        using var scope = provider.CreateScope();

        var steps = scope.ServiceProvider.GetServices<IProcessStep>().ToList();
        var map = ProcessEngine.MapSteps(steps);
        var uncovered = Enum.GetValues<ProcessState>()
            .Where(s => !s.IsTerminal() && s != ProcessState.Blocked && !map.ContainsKey(s));

        Assert.Empty(uncovered);
        Assert.DoesNotContain(map.Keys, s => s.IsTerminal() || s == ProcessState.Blocked);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ProcessEngine>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ProcessWatchdog>());
    }

    [Fact]
    public void Two_steps_for_the_same_state_are_rejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ProcessEngine.MapSteps([new CompleteStep(_h.Time), new CompleteStep(_h.Time)]));

        Assert.Contains(nameof(ProcessState.MastrRegistered), error.Message);
        Assert.Contains(nameof(CompleteStep), error.Message);
    }

    [Fact]
    public async Task State_without_step_ends_the_run_quietly()
    {
        var process = Add();
        var engine = new ProcessEngine(_h.Store, [], _h.Ledger, _h.Tasks, _h.CrmStatus, new AuditLog([]), _h.Time,
            NullLogger<ProcessEngine>.Instance);

        await engine.RunAsync(process.Id, default);

        Assert.Equal(ProcessState.Created, process.State);
    }

    // Statusspiegel

    [Fact]
    public async Task Every_state_change_is_audited_and_published_to_crm()
    {
        var process = Add();

        await _h.Engine().RunAsync(process.Id, default);

        Assert.Equal(ProcessState.NetzanmeldungSubmitted, process.State);
        Assert.Equal(
            [ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved, ProcessState.NetzanmeldungSubmitted],
            _h.CrmStatus.Published);
        Assert.Equal(3, _h.Audit.Events.Count(e => e.Type == "state-changed"));
    }

    [Fact]
    public async Task Waiting_run_publishes_nothing()
    {
        var process = Add(ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved, ProcessState.NetzanmeldungSubmitted);

        await _h.Engine().RunAsync(process.Id, default);

        Assert.Empty(_h.CrmStatus.Published);
    }

    [Fact]
    public async Task Blocked_process_does_not_run()
    {
        var process = Add();
        process.Block("Timeout", TestData.Now);

        await _h.Engine().RunAsync(process.Id, default);

        Assert.Equal(ProcessState.Blocked, process.State);
        Assert.Equal(0, _h.Planning.Calls);
    }

    // Storno

    [Fact]
    public async Task Cancellation_before_any_submission_needs_no_task()
    {
        var process = Add(ProcessState.DataCardAggregated);
        process.RequestCancellation(TestData.Now);

        await _h.Engine().RunAsync(process.Id, default);

        Assert.Equal(ProcessState.Cancelled, process.State);
        Assert.Empty(_h.Tasks.Calls);
        Assert.Equal([ProcessState.Cancelled], _h.CrmStatus.Published);
    }

    [Fact]
    public async Task Cancellation_works_from_blocked()
    {
        var process = Add(ProcessState.DataCardAggregated);
        process.Block("Timeout", TestData.Now);
        process.RequestCancellation(TestData.Now);

        await _h.Engine().RunAsync(process.Id, default);

        Assert.Equal(ProcessState.Cancelled, process.State);
    }

    [Theory]
    [InlineData(SubmissionStatus.Sent, true)]
    [InlineData(SubmissionStatus.Sending, true)]
    [InlineData(SubmissionStatus.DryRun, false)]
    [InlineData(SubmissionStatus.Failed, false)]
    public async Task Cancellation_after_submission_asks_to_withdraw(SubmissionStatus status, bool expectTask)
    {
        var process = Add(ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved, ProcessState.NetzanmeldungSubmitted);
        _h.Ledger.Put(new SubmissionKey(process.Id, SubmissionKind.Netzanmeldung), status);
        process.RequestCancellation(TestData.Now);

        await _h.Engine().RunAsync(process.Id, default);

        Assert.Equal(ProcessState.Cancelled, process.State);
        var withdraw = _h.Tasks.Calls.Where(t => t.Key == $"withdraw:{process.Id:N}").ToList();
        Assert.Equal(expectTask, withdraw.Count == 1);
        if (expectTask)
        {
            Assert.Equal("Antrag beim Netzbetreiber zurückziehen", withdraw[0].Title);
            Assert.Contains("Netzanmeldung", withdraw[0].Body);
        }
    }

    [Fact]
    public async Task Cancellation_ignores_other_processes_and_mastr()
    {
        var process = Add(ProcessState.DataCardAggregated);
        _h.Ledger.Put(new SubmissionKey(Guid.NewGuid(), SubmissionKind.Netzanmeldung), SubmissionStatus.Sent);
        _h.Ledger.Put(new SubmissionKey(process.Id, SubmissionKind.Mastr), SubmissionStatus.Sent);
        process.RequestCancellation(TestData.Now);

        await _h.Engine().RunAsync(process.Id, default);

        Assert.Empty(_h.Tasks.Calls);
    }

    [Fact]
    public async Task Terminal_process_is_left_alone()
    {
        var process = Add();
        process.TransitionTo(ProcessState.Cancelled, TestData.Now);
        process.RequestCancellation(TestData.Now);

        await _h.Engine().RunAsync(process.Id, default);

        Assert.Empty(_h.CrmStatus.Published);
    }

    private ServiceProvider CoreServices()
    {
        var services = new ServiceCollection();
        services.AddAutopilotCore();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ICrmProjectSource, FakeCrmSource>();
        services.AddSingleton<IPlanningDataSource, FakePlanningSource>();
        services.AddSingleton<IProductDocumentSource, NoProductDocuments>();
        services.AddSingleton<INetzbetreiberResolver>(new FakeResolver());
        services.AddSingleton<INetzbetreiberAdapter, FakeAdapter>();
        services.AddSingleton<ISubmissionLedger, InMemoryLedger>();
        services.AddSingleton<IDocumentArchive, RecordingArchive>();
        services.AddSingleton<IMastrRegistrar, FakeMastrRegistrar>();
        services.AddSingleton<IMastrCredentialStore, FakeCredentialStore>();
        services.AddSingleton<IProcessStore, InMemoryProcessStore>();
        services.AddSingleton<IProcessScheduler, RecordingScheduler>();
        services.AddSingleton<IHumanTaskSink, RecordingTasks>();
        services.AddSingleton<ICrmStatusSink, RecordingCrmStatus>();
        services.AddSingleton(Options.Create(new SubmissionOptions()));
        services.AddSingleton(Options.Create(new ProcessWatchdogOptions()));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
