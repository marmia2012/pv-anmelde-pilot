using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Infrastructure.Jobs;
using PvAutopilot.Tests.Core;
using PvAutopilot.Tests.Host;

namespace PvAutopilot.Tests.Infrastructure;

public class ProcessJobTests
{
    private readonly InMemoryProcessStore _store = new();
    private readonly RecordingTasks _tasks = new();
    private readonly FakeRunLock _lock = new();
    private int _failureScopes;

    /// <summary>Job mit einem Schritt, der immer wirft; der FailureHandler kommt aus einem eigenen Scope.</summary>
    private ProcessJob Job()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTime(TestData.Now));
        // Zählt, wie oft ein Scope den Store auflöst – im echten Betrieb hängt daran ein frischer DbContext.
        services.AddScoped<IProcessStore>(_ => { _failureScopes++; return _store; });
        services.AddSingleton<IOperatorAlert>(new CountingAlert());
        services.AddSingleton<IHumanTaskSink>(_tasks);
        services.AddSingleton<ICrmStatusSink>(new RecordingCrmStatus());
        services.AddSingleton(new AuditLog([]));
        services.AddScoped<ProcessFailureHandler>();

        var engine = new ProcessEngine(_store, [new FailingStep()], new InMemoryLedger(), _tasks, new RecordingCrmStatus(),
            new AuditLog([]), new FixedTime(TestData.Now), NullLogger<ProcessEngine>.Instance);
        return new ProcessJob(engine, services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _lock,
            new NoJobs(), NullLogger<ProcessJob>.Instance);
    }

    [Fact]
    public async Task Last_attempt_blocks_in_a_fresh_scope_and_releases_the_lock()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));

        await Job().RunCoreAsync(process.Id, ProcessJob.MaxRetries, default);

        Assert.Equal(ProcessState.Blocked, process.State);
        Assert.Equal("HttpRequestException: HTTP 503", process.BlockReason);
        Assert.Equal(1, _failureScopes);
        Assert.Equal((1, 1), (_lock.Acquired, _lock.Released));
    }

    [Fact]
    public async Task Earlier_attempts_rethrow_for_hangfire_retry()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));

        await Assert.ThrowsAsync<HttpRequestException>(() => Job().RunCoreAsync(process.Id, ProcessJob.MaxRetries - 1, default));

        Assert.Equal(ProcessState.Created, process.State);
        Assert.Equal(0, _failureScopes);
        Assert.Equal(1, _lock.Released);
    }

    [Fact]
    public async Task Failing_unlock_is_only_logged()
    {
        // Nie geöffnete Verbindung: das Unlock schlägt fehl, darf aber nicht werfen (Blockieren ginge sonst verloren).
        var held = new PostgresProcessRunLock.Held(new NpgsqlConnection("Host=nicht-erreichbar.invalid"), 42,
            NullLogger.Instance);

        await held.DisposeAsync();
    }

    private sealed class FailingStep : IProcessStep
    {
        public bool CanHandle(ProcessState state) => state == ProcessState.Created;

        public Task<StepResult> ExecuteAsync(AnmeldeProcess process, CancellationToken ct) =>
            throw new HttpRequestException("HTTP 503");
    }

    private sealed class NoJobs : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => throw new InvalidOperationException("Kein Job erwartet.");

        public bool ChangeState(string jobId, IState state, string expectedState) => false;
    }
}
