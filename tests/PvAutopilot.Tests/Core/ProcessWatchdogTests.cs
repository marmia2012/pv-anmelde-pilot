using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Tests.Core;

public class ProcessWatchdogTests
{
    private readonly InMemoryProcessStore _store = new();
    private readonly RecordingScheduler _scheduler = new();
    private readonly RecordingTasks _tasks = new();
    private readonly FixedTime _time = new(TestData.Now);

    private readonly SubmissionOptions _submission = new();

    private ProcessWatchdog Watchdog() => new(_store, _scheduler, _tasks, Options.Create(new ProcessWatchdogOptions()),
        Options.Create(_submission), _time,
        NullLogger<ProcessWatchdog>.Instance);

    private AnmeldeProcess Add(params ProcessState[] path)
    {
        var process = _store.Add(new AnmeldeProcess($"crm-{Guid.NewGuid():N}", _time.Now));
        foreach (var state in path)
            process.TransitionTo(state, _time.Now);
        return process;
    }

    private static readonly ProcessState[] ToSubmitted =
        [ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved, ProcessState.NetzanmeldungSubmitted];

    [Fact]
    public async Task Stalled_active_process_is_rescheduled()
    {
        var stalled = Add(ProcessState.DataCardAggregated);
        _time.Now = TestData.Now.AddHours(2);
        Add(); // gerade erst angelegt

        var report = await Watchdog().RunAsync(default);

        Assert.Equal([stalled.Id], _scheduler.Enqueued);
        Assert.Equal(new WatchdogReport(2, 1, 0), report);
        Assert.Empty(_tasks.Calls);
    }

    [Fact]
    public async Task Active_process_within_threshold_is_left_alone()
    {
        Add();
        _time.Now = TestData.Now.AddMinutes(30);

        var report = await Watchdog().RunAsync(default);

        Assert.Equal(new WatchdogReport(1, 0, 0), report);
    }

    [Fact]
    public async Task Waiting_state_beyond_deadline_creates_one_task_per_state()
    {
        var process = Add(ToSubmitted);
        _time.Now = TestData.Now.AddDays(29);
        Assert.Equal(0, (await Watchdog().RunAsync(default)).Overdue);

        _time.Now = TestData.Now.AddDays(31);
        var report = await Watchdog().RunAsync(default);
        await Watchdog().RunAsync(default);

        Assert.Equal(1, report.Overdue);
        Assert.Equal([$"overdue:{process.Id:N}:NetzanmeldungSubmitted:20261103120000"], _tasks.Keys);
        Assert.Contains("31 Tagen", _tasks.Calls[0].Body);
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public async Task Waiting_state_without_deadline_creates_no_task()
    {
        Add(ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved, ProcessState.NetzanmeldungPrepared);
        _time.Now = TestData.Now.AddDays(365);

        Assert.Equal(0, (await Watchdog().RunAsync(default)).Overdue);
    }

    [Fact]
    public async Task Blocked_process_gets_reminder_after_one_day_and_is_not_rescheduled()
    {
        var process = Add(ProcessState.DataCardAggregated);
        process.Block("Timeout", TestData.Now);
        _time.Now = TestData.Now.AddHours(20);
        Assert.Equal(new WatchdogReport(1, 0, 0), await Watchdog().RunAsync(default));

        _time.Now = TestData.Now.AddDays(2);
        var report = await Watchdog().RunAsync(default);

        Assert.Equal(new WatchdogReport(1, 0, 1), report);
        Assert.Equal([$"overdue:{process.Id:N}:Blocked:20261103120000"], _tasks.Keys);
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public async Task Lost_cancellation_is_rescheduled_even_from_waiting_or_blocked_state()
    {
        var waiting = Add(ToSubmitted);
        waiting.RequestCancellation(TestData.Now);
        var blocked = Add();
        blocked.Block("Timeout", TestData.Now);
        blocked.RequestCancellation(TestData.Now);
        _time.Now = TestData.Now.AddHours(2);

        var report = await Watchdog().RunAsync(default);

        Assert.Equal(2, report.Rescheduled);
        Assert.Equal(new[] { waiting.Id, blocked.Id }.Order(), _scheduler.Enqueued.Order());
    }

    private static readonly ProcessState[] ToNetzanmeldungPrepared =
        [ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved, ProcessState.NetzanmeldungPrepared];

    [Theory]
    [InlineData(SubmissionMode.Review)]
    [InlineData(SubmissionMode.Live)]
    public async Task Prepared_process_is_rescheduled_after_switch_from_dry_run(SubmissionMode mode)
    {
        var process = Add(ToNetzanmeldungPrepared);
        _submission.Mode = mode;
        _time.Now = TestData.Now.AddDays(3);

        var report = await Watchdog().RunAsync(default);

        Assert.Equal([process.Id], _scheduler.Enqueued);
        Assert.Equal(1, report.Rescheduled);
    }

    [Fact]
    public async Task Prepared_process_in_dry_run_is_not_rescheduled()
    {
        Add(ToNetzanmeldungPrepared);
        _time.Now = TestData.Now.AddDays(3);

        Assert.Equal(new WatchdogReport(1, 0, 0), await Watchdog().RunAsync(default));
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public async Task Zusage_with_reached_inbetriebnahme_date_is_rescheduled()
    {
        var future = Add([.. ToSubmitted, ProcessState.EinspeisezusageReceived]);
        future.RecordInbetriebnahme(new DateOnly(2026, 11, 4), TestData.Now);
        var reached = Add([.. ToSubmitted, ProcessState.EinspeisezusageReceived]);
        reached.RecordInbetriebnahme(new DateOnly(2026, 11, 3), TestData.Now);
        Add([.. ToSubmitted, ProcessState.EinspeisezusageReceived]); // ohne Datum

        Assert.Equal(new WatchdogReport(3, 1, 0), await Watchdog().RunAsync(default));
        Assert.Equal([reached.Id], _scheduler.Enqueued);

        // 03.11. 23:30 UTC ist in Berlin schon der 04.11.
        _scheduler.Enqueued.Clear();
        _time.Now = new DateTimeOffset(2026, 11, 3, 23, 30, 0, TimeSpan.Zero);
        await Watchdog().RunAsync(default);
        Assert.Equal(new[] { future.Id, reached.Id }.Order(), _scheduler.Enqueued.Order());
    }

    [Fact]
    public async Task Overdue_task_is_new_when_the_state_is_reached_again()
    {
        var process = Add();
        process.Block("Timeout", TestData.Now);
        _time.Now = TestData.Now.AddDays(2);
        await Watchdog().RunAsync(default);

        process.Resume(_time.Now);
        process.Block("Timeout", _time.Now.AddMinutes(1));
        _time.Now = TestData.Now.AddDays(4);
        await Watchdog().RunAsync(default);

        Assert.Equal(2, _tasks.Keys.Count);
    }

    [Fact]
    public async Task Terminal_processes_are_not_checked()
    {
        var process = Add();
        process.TransitionTo(ProcessState.Cancelled, TestData.Now);
        _time.Now = TestData.Now.AddDays(400);

        Assert.Equal(new WatchdogReport(0, 0, 0), await Watchdog().RunAsync(default));
    }

    [Fact]
    public void Default_deadlines_match_concept()
    {
        var options = new ProcessWatchdogOptions();

        Assert.Equal(TimeSpan.FromDays(30), options.MaxWait[ProcessState.NetzanmeldungSubmitted]);
        Assert.Equal(TimeSpan.FromDays(7), options.MaxWait[ProcessState.ZaehlerwechselCompleted]);
        Assert.Equal(TimeSpan.FromDays(1), options.MaxWait[ProcessState.Blocked]);
        Assert.All(SubmissionStages.All, s => Assert.True(options.MaxWait.ContainsKey(s.AwaitingReview)));
        Assert.DoesNotContain(options.MaxWait.Keys, s => s.IsActive());
    }
}
