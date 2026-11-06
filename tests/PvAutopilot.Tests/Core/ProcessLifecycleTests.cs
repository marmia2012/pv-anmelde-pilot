using Microsoft.Extensions.Logging.Abstractions;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Tests.Core;

public class ProcessLifecycleTests
{
    private readonly InMemoryProcessStore _store = new();
    private readonly RecordingScheduler _scheduler = new();
    private readonly RecordingCrmStatus _crmStatus = new();
    private readonly FixedTime _time = new(TestData.Now);

    private ProcessTriggerHandler Handler() =>
        new(_store, _scheduler, _crmStatus, _time, NullLogger<ProcessTriggerHandler>.Instance);

    private static ProcessTrigger Trigger(ProcessTriggerKind kind) => new(kind, "crm-1");

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
    public async Task Resume_blocked_process_restores_state_publishes_status_and_schedules_run()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));
        process.Block("Timeout", TestData.Now);

        Assert.Equal(ResumeResult.Resumed, await Handler().ResumeAsync(process.Id, default));
        Assert.Equal(ProcessState.Created, process.State);
        Assert.Equal([process.Id], _scheduler.Enqueued);
        Assert.Equal([ProcessState.Created], _crmStatus.Published);
    }

    [Fact]
    public async Task Failure_handler_alerts_only_once_for_parallel_retry_chains()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));
        var alert = new CountingAlert();
        var tasks = new RecordingTasks();
        var handler = new ProcessFailureHandler(_store, alert, tasks, _crmStatus, new AuditLog([]), _time,
            NullLogger<ProcessFailureHandler>.Instance);

        await handler.BlockAsync(process.Id, new HttpRequestException("HTTP 503"), default);
        await handler.BlockAsync(process.Id, new HttpRequestException("HTTP 503"), default);

        Assert.Equal(ProcessState.Blocked, process.State);
        Assert.Equal(1, alert.Count);
        Assert.Single(tasks.Calls);
        Assert.Equal([ProcessState.Blocked], _crmStatus.Published);
    }

    [Fact]
    public async Task Failure_handler_uses_safe_reason_and_new_task_per_block()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));
        var tasks = new RecordingTasks();
        var alert = new RecordingAlert();
        var handler = new ProcessFailureHandler(_store, alert, tasks, _crmStatus, new AuditLog([]), _time,
            NullLogger<ProcessFailureHandler>.Instance);

        await handler.BlockAsync(process.Id, new InvalidOperationException("Max Mustermann, Musterweg 1"), default);
        Assert.Equal("InvalidOperationException", process.BlockReason);
        Assert.Equal("InvalidOperationException", alert.Bodies.Single());

        process.Resume(TestData.Now.AddHours(1));
        _time.Now = TestData.Now.AddHours(2);
        await handler.BlockAsync(process.Id, new HttpRequestException("HTTP 503"), default);

        Assert.Equal("HttpRequestException: HTTP 503", process.BlockReason);
        Assert.Equal([$"blocked:{process.Id:N}:20261103120000", $"blocked:{process.Id:N}:20261103140000"], tasks.Keys);
    }

    [Theory]
    [InlineData("HTTP 503", "HttpRequestException: HTTP 503")]
    [InlineData("Reonic /projects/123: HTTP 429", "HttpRequestException: Reonic /projects/123: HTTP 429")]
    [InlineData("Fehler bei max@example.org: HTTP 500", "HttpRequestException")]
    [InlineData("Connection refused (Musterstraße 5)", "HttpRequestException")]
    public void Http_errors_keep_only_our_own_status_format(string message, string expected)
    {
        Assert.Equal(expected, BlockReasons.From(new HttpRequestException(message)));
    }

    [Fact]
    public void Block_reasons_use_allowlisted_messages_only()
    {
        Assert.Equal("AutomationException: Selektor #absenden nicht gefunden",
            BlockReasons.From(new AutomationException("Selektor #absenden nicht gefunden")));
        Assert.StartsWith("SubmissionConflictException: Versand ", BlockReasons.From(new SubmissionConflictException("abc:Netzanmeldung")));
        Assert.Equal("InvalidProcessTransitionException: Übergang Created → Completed ist nicht erlaubt.",
            BlockReasons.From(new InvalidProcessTransitionException(ProcessState.Created, ProcessState.Completed)));
        Assert.Equal($"TaskCanceledException: {BlockReasons.Timeout}", BlockReasons.From(new TaskCanceledException("Erika Muster")));
        Assert.Equal($"TimeoutException: {BlockReasons.Timeout}", BlockReasons.From(new TimeoutException("Erika Muster")));
        Assert.Equal("HttpRequestException: HTTP 502",
            BlockReasons.From(new HttpRequestException("Antwort für Erika Muster", null, System.Net.HttpStatusCode.BadGateway)));
        Assert.Equal("FormatException", BlockReasons.From(new FormatException("IBAN DE00 1234 ungültig")));
        Assert.Equal("SubmissionNotSentException", BlockReasons.From(new SubmissionNotSentException("an max@example.org")));
    }

    [Fact]
    public async Task Stale_rueckfrage_event_after_close_is_ignored()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));
        process.RecordRueckfrage(TestData.Now);
        process.CloseRueckfrage(TestData.Now.AddHours(1));

        var result = await Handler().HandleAsync(Trigger(ProcessTriggerKind.RueckfrageReceived) with
            { OccurredAt = TestData.Now }, default);

        Assert.Equal(TriggerResult.Ignored, result);
        Assert.Null(process.RueckfrageOffenSeit);
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public async Task Start_creates_process_once_and_schedules_it()
    {
        Assert.Equal(TriggerResult.Applied, await Handler().HandleAsync(Trigger(ProcessTriggerKind.Start), default));
        Assert.Equal(TriggerResult.Applied, await Handler().HandleAsync(Trigger(ProcessTriggerKind.Start), default));

        var process = await _store.FindByCrmProjectAsync("crm-1", default);
        Assert.Equal([process!.Id, process.Id], _scheduler.Enqueued);
        Assert.Single(await _store.ListOpenAsync(default));
    }

    [Fact]
    public async Task Event_for_unknown_project_is_not_found()
    {
        Assert.Equal(TriggerResult.NotFound,
            await Handler().HandleAsync(Trigger(ProcessTriggerKind.EinspeisezusageReceived), default));
    }

    [Fact]
    public async Task Events_only_record_facts_and_never_change_state()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));

        await Handler().HandleAsync(Trigger(ProcessTriggerKind.EinspeisezusageReceived) with
            { Date = new DateOnly(2026, 11, 10), Reference = "NB-1" }, default);
        await Handler().HandleAsync(Trigger(ProcessTriggerKind.InbetriebnahmeReported), default);

        Assert.Equal(ProcessState.Created, process.State);
        Assert.Equal(new DateOnly(2026, 11, 10), process.EinspeisezusageDatum);
        Assert.Equal("NB-1", process.NetzbetreiberReferenz);
        Assert.Equal(DateOnly.FromDateTime(TestData.Now.UtcDateTime), process.Inbetriebnahmedatum);
        Assert.Equal(2, _scheduler.Enqueued.Count);
    }

    [Fact]
    public async Task Review_approval_for_another_stage_is_ignored()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));

        var result = await Handler().HandleAsync(Trigger(ProcessTriggerKind.ReviewApproved) with
            { Submission = SubmissionKind.Netzanmeldung }, default);

        Assert.Equal(TriggerResult.Ignored, result);
        Assert.False(process.IsReviewApproved(SubmissionKind.Netzanmeldung));
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public async Task Rueckfrage_resolution_without_open_rueckfrage_is_ignored()
    {
        _store.Add(new AnmeldeProcess("crm-1", TestData.Now));

        var result = await Handler().HandleAsync(Trigger(ProcessTriggerKind.RueckfrageResolved) with
            { Resolution = RueckfrageResolution.AnsweredDirectly }, default);

        Assert.Equal(TriggerResult.Ignored, result);
    }

    [Fact]
    public async Task Blocked_process_keeps_fact_but_only_cancellation_schedules_a_run()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));
        process.Block("Timeout", TestData.Now);

        Assert.Equal(TriggerResult.Applied, await Handler().HandleAsync(Trigger(ProcessTriggerKind.InbetriebnahmeReported), default));
        Assert.Empty(_scheduler.Enqueued);
        Assert.NotNull(process.Inbetriebnahmedatum);

        Assert.Equal(TriggerResult.Applied, await Handler().HandleAsync(Trigger(ProcessTriggerKind.Cancelled), default));
        Assert.Equal([process.Id], _scheduler.Enqueued);
    }

    [Fact]
    public async Task Events_for_terminal_process_are_ignored()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));
        process.TransitionTo(ProcessState.Cancelled, TestData.Now);

        Assert.Equal(TriggerResult.Ignored, await Handler().HandleAsync(Trigger(ProcessTriggerKind.Cancelled), default));
    }
}
