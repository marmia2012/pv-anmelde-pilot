using Microsoft.Extensions.Logging.Abstractions;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;

namespace PvAutopilot.Tests.Core;

public class ProcessLifecycleTests
{
    private readonly InMemoryStore _store = new();
    private readonly RecordingScheduler _scheduler = new();
    private readonly FixedTime _time = new(TestData.Now);

    private ProcessTriggerHandler Handler() =>
        new(_store, _scheduler, _time, NullLogger<ProcessTriggerHandler>.Instance);

    [Fact]
    public async Task Resume_unknown_process_reports_not_found()
    {
        Assert.Equal(ResumeResult.NotFound, await Handler().ResumeAsync(Guid.NewGuid(), default));
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public async Task Resume_process_that_is_not_blocked_reports_conflict()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));

        Assert.Equal(ResumeResult.NotBlocked, await Handler().ResumeAsync(process.Id, default));
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public async Task Resume_blocked_process_restores_state_and_schedules_run()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));
        process.Block("Timeout", TestData.Now);

        Assert.Equal(ResumeResult.Resumed, await Handler().ResumeAsync(process.Id, default));
        Assert.Equal(ProcessState.Created, process.State);
        Assert.Equal([process.Id], _scheduler.Enqueued);
    }

    [Fact]
    public async Task Failure_handler_alerts_only_once_for_parallel_retry_chains()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));
        var alert = new CountingAlert();
        var handler = new ProcessFailureHandler(_store, alert, new NoTasks(), new AuditLog([]), _time,
            NullLogger<ProcessFailureHandler>.Instance);

        await handler.BlockAsync(process.Id, new HttpRequestException("HTTP 503"), default);
        await handler.BlockAsync(process.Id, new HttpRequestException("HTTP 503"), default);

        Assert.Equal(ProcessState.Blocked, process.State);
        Assert.Equal(1, alert.Count);
    }

    private sealed class InMemoryStore : IProcessStore
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

        public Task SaveAsync(AnmeldeProcess process, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingScheduler : IProcessScheduler
    {
        public List<Guid> Enqueued { get; } = [];
        public void Enqueue(Guid processId) => Enqueued.Add(processId);
    }

    private sealed class CountingAlert : IOperatorAlert
    {
        public int Count { get; private set; }

        public Task SendAsync(string subject, string body, byte[]? screenshot, CancellationToken ct)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class NoTasks : IHumanTaskSink
    {
        public Task CreateTaskAsync(string crmProjectId, string taskKey, string title, string body, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
