using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;
using PvAutopilot.Tests.Sources.HubSpot;

namespace PvAutopilot.Tests.Core;

/// <summary>
/// HubSpot ist oft selbst die Ursache einer Blockade: Alarm-Mail und Vorgangsstatus dürfen nicht an CRM-Writes hängen.
/// </summary>
public class BlockingResilienceTests
{
    private readonly InMemoryProcessStore _store = new();
    private readonly FixedTime _time = new(TestData.Now);

    private sealed class Throwing : IAuditSink, ICrmStatusSink, IHumanTaskSink, IOperatorAlert
    {
        public int Calls { get; private set; }

        public Task WriteAsync(AnmeldeProcess process, AuditEvent evt, CancellationToken ct) => Fail();
        public Task PublishAsync(AnmeldeProcess process, CancellationToken ct) => Fail();
        public Task CreateTaskAsync(string crmProjectId, string taskKey, string title, string body, CancellationToken ct) => Fail();
        public Task SendAsync(string subject, string body, byte[]? screenshot, CancellationToken ct) => Fail();

        private Task Fail()
        {
            Calls++;
            throw new HttpRequestException("HubSpot kaputt: Erika Mustermann, erika@example.org", null, System.Net.HttpStatusCode.BadGateway);
        }
    }

    private AnmeldeProcess Process() => _store.Add(new AnmeldeProcess("crm-1", TestData.Now));

    private ProcessFailureHandler Handler(IOperatorAlert alert, IAuditSink? audit = null, ICrmStatusSink? status = null,
        IHumanTaskSink? tasks = null, ILogger<ProcessFailureHandler>? logger = null) =>
        new(_store, alert, tasks ?? new RecordingTasks(), status ?? new RecordingCrmStatus(), new AuditLog(audit is null ? [] : [audit]), _time,
            logger ?? NullLogger<ProcessFailureHandler>.Instance);

    [Fact]
    public async Task Alert_is_sent_when_the_audit_write_fails()
    {
        var process = Process();
        var alert = new CountingAlert();

        await Handler(alert, audit: new Throwing()).BlockAsync(process.Id, new TimeoutException(), default);

        Assert.Equal(1, alert.Count);
        Assert.Equal(ProcessState.Blocked, process.State);
    }

    [Fact]
    public async Task Alert_is_sent_when_the_status_write_fails()
    {
        var process = Process();
        var alert = new CountingAlert();

        await Handler(alert, status: new Throwing()).BlockAsync(process.Id, new TimeoutException(), default);

        Assert.Equal(1, alert.Count);
        Assert.Equal(ProcessState.Blocked, process.State);
    }

    [Fact]
    public async Task Alert_is_sent_when_the_task_write_fails()
    {
        var process = Process();
        var alert = new CountingAlert();

        await Handler(alert, tasks: new Throwing()).BlockAsync(process.Id, new TimeoutException(), default);

        Assert.Equal(1, alert.Count);
        Assert.Equal(ProcessState.Blocked, process.State);
    }

    [Fact]
    public async Task Alert_is_sent_and_process_stays_blocked_when_all_crm_writes_fail()
    {
        var process = Process();
        var alert = new CountingAlert();
        var crm = new Throwing();

        await Handler(alert, audit: crm, status: crm, tasks: crm).BlockAsync(process.Id, new TimeoutException(), default);

        Assert.Equal(1, alert.Count);
        Assert.Equal(ProcessState.Blocked, process.State);
        Assert.Equal(3, crm.Calls);
    }

    [Fact]
    public async Task Failing_alert_does_not_stop_the_crm_writes_nor_unblock_the_process()
    {
        var process = Process();
        var audit = new RecordingAudit();
        var status = new RecordingCrmStatus();
        var tasks = new RecordingTasks();

        await Handler(new Throwing(), audit, status, tasks).BlockAsync(process.Id, new TimeoutException(), default);

        Assert.Equal(ProcessState.Blocked, process.State);
        Assert.Equal("blocked", Assert.Single(audit.Events).Type);
        Assert.Equal([ProcessState.Blocked], status.Published);
        Assert.Single(tasks.Calls);
    }

    [Fact]
    public async Task Working_sinks_still_get_audit_status_and_task_when_nothing_fails()
    {
        var process = Process();
        var audit = new RecordingAudit();
        var status = new RecordingCrmStatus();
        var tasks = new RecordingTasks();
        var alert = new RecordingAlert();

        await Handler(alert, audit, status, tasks).BlockAsync(process.Id, new HttpRequestException("HTTP 503"), default);

        Assert.Equal(AuditLevel.Error, Assert.Single(audit.Events).Level);
        Assert.Equal([ProcessState.Blocked], status.Published);
        Assert.Single(tasks.Calls);
        Assert.Single(alert.Bodies);
    }

    [Fact]
    public async Task Failures_of_the_crm_writes_are_logged_without_their_message()
    {
        var process = Process();
        var log = new CapturingLogger<ProcessFailureHandler>();
        var crm = new Throwing();

        await Handler(new CountingAlert(), crm, crm, crm, log).BlockAsync(process.Id, new TimeoutException(), default);

        Assert.Contains(log.Messages, m => m.Contains("HttpRequestException"));
        Assert.DoesNotContain("Mustermann", log.All);
        Assert.DoesNotContain("example.org", log.All);
    }
}

public class EngineStatusPublishingTests
{
    private readonly ProcessHarness _h = new();

    private sealed class FlakyStatus : ICrmStatusSink
    {
        public bool Fail { get; set; } = true;
        public List<ProcessState> Published { get; } = [];
        public int Attempts { get; private set; }

        public Task PublishAsync(AnmeldeProcess process, CancellationToken ct)
        {
            Attempts++;
            if (Fail)
                throw new HttpRequestException("HTTP 502 für Erika Mustermann");
            Published.Add(process.State);
            return Task.CompletedTask;
        }
    }

    private ProcessEngine Engine(ICrmStatusSink status) =>
        new(_h.Store, _h.Steps(), _h.Ledger, _h.Tasks, status, new AuditLog([_h.Audit]), _h.Time, NullLogger<ProcessEngine>.Instance);

    private AnmeldeProcess Waiting() =>
        _h.Store.Add(WaitingProcess());

    private static AnmeldeProcess WaitingProcess()
    {
        var process = new AnmeldeProcess(ProcessHarness.ProjectId, TestData.Now);
        process.TransitionTo(ProcessState.DataCardAggregated, TestData.Now);
        process.TransitionTo(ProcessState.NetzbetreiberResolved, TestData.Now);
        process.TransitionTo(ProcessState.NetzanmeldungSubmitted, TestData.Now);
        return process;
    }

    [Fact]
    public async Task Run_without_state_change_publishes_the_status_exactly_once()
    {
        var process = Waiting();

        await _h.Engine().RunAsync(process.Id, default);

        Assert.Equal([ProcessState.NetzanmeldungSubmitted], _h.CrmStatus.Published);
    }

    [Fact]
    public async Task Every_further_run_publishes_once_more()
    {
        var process = Waiting();

        await _h.Engine().RunAsync(process.Id, default);
        await _h.Engine().RunAsync(process.Id, default);

        Assert.Equal(2, _h.CrmStatus.Published.Count);
    }

    [Fact]
    public async Task Run_with_state_changes_does_not_publish_the_final_state_twice()
    {
        var process = _h.Store.Add(new AnmeldeProcess(ProcessHarness.ProjectId, TestData.Now));

        await _h.Engine().RunAsync(process.Id, default);

        Assert.Equal(
            [ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved, ProcessState.NetzanmeldungSubmitted],
            _h.CrmStatus.Published);
    }

    [Fact]
    public async Task Terminal_process_publishes_nothing()
    {
        var process = _h.Store.Add(new AnmeldeProcess(ProcessHarness.ProjectId, TestData.Now));
        process.TransitionTo(ProcessState.Cancelled, TestData.Now);

        await _h.Engine().RunAsync(process.Id, default);

        Assert.Empty(_h.CrmStatus.Published);
    }

    [Fact]
    public async Task Cancellation_publishes_the_cancelled_state_once()
    {
        var process = Waiting();
        process.RequestCancellation(TestData.Now);

        await _h.Engine().RunAsync(process.Id, default);

        Assert.Equal([ProcessState.Cancelled], _h.CrmStatus.Published);
    }

    [Fact]
    public async Task Failing_status_sink_does_not_abort_the_run()
    {
        var process = _h.Store.Add(new AnmeldeProcess(ProcessHarness.ProjectId, TestData.Now));
        var status = new FlakyStatus();

        await Engine(status).RunAsync(process.Id, default);

        Assert.Equal(ProcessState.NetzanmeldungSubmitted, process.State);
        Assert.True(status.Attempts >= 3);
        Assert.Equal(3, _h.Audit.Events.Count(e => e.Type == "state-changed"));
    }

    [Fact]
    public async Task Failing_status_sink_on_a_waiting_process_does_not_throw()
    {
        var process = Waiting();

        await Engine(new FlakyStatus()).RunAsync(process.Id, default);

        Assert.Equal(ProcessState.NetzanmeldungSubmitted, process.State);
    }

    [Fact]
    public async Task Next_run_publishes_the_state_that_failed_before()
    {
        var process = Waiting();
        var status = new FlakyStatus();
        await Engine(status).RunAsync(process.Id, default);
        Assert.Empty(status.Published);

        status.Fail = false;
        await Engine(status).RunAsync(process.Id, default);

        Assert.Equal([ProcessState.NetzanmeldungSubmitted], status.Published);
    }

    [Fact]
    public async Task Status_failure_is_logged_without_the_exception_message()
    {
        var process = Waiting();
        var log = new CapturingLogger<ProcessEngine>();
        var engine = new ProcessEngine(_h.Store, _h.Steps(), _h.Ledger, _h.Tasks, new FlakyStatus(), new AuditLog([_h.Audit]), _h.Time, log);

        await engine.RunAsync(process.Id, default);

        Assert.Contains(log.Messages, m => m.Contains("HttpRequestException"));
        Assert.DoesNotContain("Mustermann", log.All);
    }

    [Fact]
    public async Task Resume_is_scheduled_even_when_the_status_write_fails()
    {
        var process = _h.Store.Add(new AnmeldeProcess(ProcessHarness.ProjectId, TestData.Now));
        process.Block("Timeout", TestData.Now);
        var handler = new ProcessTriggerHandler(_h.Store, _h.Scheduler, new FlakyStatus(), _h.Time, NullLogger<ProcessTriggerHandler>.Instance);

        var result = await handler.ResumeAsync(process.Id, default);

        Assert.Equal(ResumeResult.Resumed, result);
        Assert.Equal(ProcessState.Created, process.State);
        Assert.Equal([process.Id], _h.Scheduler.Enqueued);
    }
}
