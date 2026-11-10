using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using PvAutopilot.Core.Orders;
using PvAutopilot.Infrastructure.Jobs;
using PvAutopilot.Tests.Core;
using PvAutopilot.Tests.Sources.HubSpot;

namespace PvAutopilot.Tests.Infrastructure;

public class OrderTransferJobTests
{
    private const string Project = "reonic-proj-1";
    private const string Sig = "sig-1";

    private readonly FakeOrderSource _source = new();
    private readonly FakeOrderSink _sink = new();
    private readonly RecordingTasks _tasks = new();
    private readonly OrderAlertRecorder _alert = new();
    private readonly RecordingLock _lock = new();
    private readonly RecordingJobs _jobs = new();

    private OrderTransferJob Job()
    {
        var service = new OrderTransferService(_source, _sink, _tasks, _alert, new InMemoryCrmWriteLedger(), NullLogger<OrderTransferService>.Instance);
        return new OrderTransferJob(service, _lock, _jobs, _alert, NullLogger<OrderTransferJob>.Instance);
    }

    private void GoodOrder() => _source.Order = new SignedOrder
    {
        PlanningProjectId = Project, SignatureRequestId = Sig, IsSigned = true, CrmProjectId = "4711", VariantId = "v1",
        Positions = [new OrderPosition("module", "M", "N", null, null, 1, 1m, null)],
    };

    [Fact]
    public async Task Successful_run_transfers_once_releases_the_lock_and_neither_alerts_nor_reschedules()
    {
        GoodOrder();

        await Job().RunCoreAsync(Project, Sig, 0, default);

        Assert.Equal([(Project, (string?)Sig)], _source.Reads);
        Assert.Single(_sink.Published);
        Assert.Empty(_alert.Sent);
        Assert.Empty(_jobs.Created);
        Assert.Equal((1, 1), (_lock.Acquired, _lock.Released));
    }

    [Fact]
    public async Task Lock_is_taken_for_the_project_with_the_order_lock_id_and_wait_time()
    {
        GoodOrder();

        await Job().RunCoreAsync(Project, Sig, 0, default);

        Assert.Equal(OrderTransferJob.LockId(Project), Assert.Single(_lock.Ids));
        Assert.Equal(OrderTransferJob.LockWait, Assert.Single(_lock.Waits));
    }

    [Fact]
    public async Task Busy_lock_reschedules_the_same_run_and_transfers_nothing()
    {
        _lock.Busy = true;
        GoodOrder();

        await Job().RunCoreAsync(Project, Sig, 0, default);

        Assert.Empty(_source.Reads);
        Assert.Empty(_sink.Published);
        Assert.Empty(_alert.Sent);
        var (job, state) = Assert.Single(_jobs.Created);
        var scheduled = Assert.IsType<ScheduledState>(state);
        Assert.InRange(scheduled.EnqueueAt, DateTime.UtcNow + OrderTransferJob.RescheduleWhenBusy - TimeSpan.FromSeconds(30),
            DateTime.UtcNow + OrderTransferJob.RescheduleWhenBusy + TimeSpan.FromSeconds(30));
        Assert.Equal(typeof(OrderTransferJob), job.Type);
        Assert.Equal(nameof(OrderTransferJob.RunAsync), job.Method.Name);
        Assert.Equal(Project, job.Args[0]);
        Assert.Equal(Sig, job.Args[1]);
        Assert.Equal(0, _lock.Released);
    }

    [Fact]
    public async Task Failure_before_the_last_retry_is_rethrown_without_alert_and_releases_the_lock()
    {
        _source.ThrowOnRead = new HttpRequestException("HTTP 503");

        await Assert.ThrowsAsync<HttpRequestException>(() => Job().RunCoreAsync(Project, Sig, OrderTransferJob.MaxRetries - 1, default));

        Assert.Empty(_alert.Sent);
        Assert.Equal((1, 1), (_lock.Acquired, _lock.Released));
    }

    [Fact]
    public async Task Failure_on_the_last_retry_alerts_without_throwing_and_without_the_exception_message()
    {
        _source.ThrowOnRead = new HttpRequestException("HTTP 503 unter /residentialProjects/geheime-id");

        await Job().RunCoreAsync(Project, Sig, OrderTransferJob.MaxRetries, default);

        var alert = Assert.Single(_alert.Sent);
        Assert.Contains(Project, alert.Subject);
        Assert.Contains(Sig, alert.Body);
        Assert.Contains(nameof(HttpRequestException), alert.Body);
        Assert.DoesNotContain("geheime-id", alert.Body);
        Assert.DoesNotContain("503", alert.Body);
        Assert.Equal((1, 1), (_lock.Acquired, _lock.Released));
    }

    [Fact]
    public async Task Failure_after_more_retries_than_the_maximum_also_alerts()
    {
        _source.ThrowOnRead = new HttpRequestException("x");

        await Job().RunCoreAsync(Project, null, OrderTransferJob.MaxRetries + 1, default);

        Assert.Single(_alert.Sent);
    }

    [Fact]
    public async Task Failing_alert_on_the_last_retry_does_not_throw()
    {
        _source.ThrowOnRead = new HttpRequestException("x");
        _alert.Throw = new InvalidOperationException("Mail kaputt");

        await Job().RunCoreAsync(Project, Sig, OrderTransferJob.MaxRetries, default);

        Assert.Empty(_alert.Sent);
        Assert.Equal(1, _lock.Released);
    }

    [Fact]
    public async Task Cancellation_on_the_last_retry_neither_alerts_nor_swallows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _source.ThrowOnRead = new OperationCanceledException(cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Job().RunCoreAsync(Project, Sig, OrderTransferJob.MaxRetries, cts.Token));

        Assert.Empty(_alert.Sent);
    }

    [Fact]
    public async Task Timeout_that_is_not_a_shutdown_counts_as_failure_on_the_last_retry()
    {
        // HttpClient-Timeout wirft OperationCanceledException, aber das Token des Jobs ist nicht abgebrochen.
        _source.ThrowOnRead = new TaskCanceledException("Timeout");

        await Job().RunCoreAsync(Project, Sig, OrderTransferJob.MaxRetries, default);

        Assert.Single(_alert.Sent);
    }

    [Fact]
    public void Lock_id_is_deterministic_and_differs_per_project()
    {
        Assert.Equal(OrderTransferJob.LockId("a"), OrderTransferJob.LockId("a"));
        Assert.NotEqual(OrderTransferJob.LockId("a"), OrderTransferJob.LockId("b"));
        Assert.NotEqual(Guid.Empty, OrderTransferJob.LockId("a"));
    }

    [Fact]
    public void Lock_id_does_not_collide_with_a_process_id_derived_from_the_same_text()
    {
        // Eigener Schlüsselraum "order:<projekt>" – die Roh-GUID eines Vorgangs ist nie dieselbe.
        Assert.NotEqual(OrderTransferJob.LockId("11111111-1111-4111-8111-111111111111"), Guid.Parse("11111111-1111-4111-8111-111111111111"));
    }

    [Fact]
    public void Retries_are_four_with_growing_delays()
    {
        Assert.Equal(4, OrderTransferJob.MaxRetries);
        var attribute = (AutomaticRetryAttribute)Attribute.GetCustomAttribute(typeof(OrderTransferJob).GetMethod(nameof(OrderTransferJob.RunAsync))!, typeof(AutomaticRetryAttribute))!;
        Assert.Equal(OrderTransferJob.MaxRetries, attribute.Attempts);
        Assert.Equal([30, 120, 600, 1800], attribute.DelaysInSeconds);
    }

    [Fact]
    public void Scheduler_enqueues_the_order_transfer_job_with_project_and_signature()
    {
        new HangfireOrderTransferScheduler(_jobs).Enqueue(Project, Sig);

        var (job, state) = Assert.Single(_jobs.Created);
        Assert.IsType<EnqueuedState>(state);
        Assert.Equal(typeof(OrderTransferJob), job.Type);
        Assert.Equal(nameof(OrderTransferJob.RunAsync), job.Method.Name);
        Assert.Equal(Project, job.Args[0]);
        Assert.Equal(Sig, job.Args[1]);
    }

    private sealed class RecordingLock : IProcessRunLock
    {
        public bool Busy { get; set; }
        public int Acquired { get; private set; }
        public int Released { get; private set; }
        public List<Guid> Ids { get; } = [];
        public List<TimeSpan> Waits { get; } = [];

        public Task<IAsyncDisposable?> TryAcquireAsync(Guid processId, TimeSpan wait, CancellationToken ct)
        {
            Ids.Add(processId);
            Waits.Add(wait);
            if (Busy)
                return Task.FromResult<IAsyncDisposable?>(null);
            Acquired++;
            return Task.FromResult<IAsyncDisposable?>(new Held(this));
        }

        private sealed class Held(RecordingLock owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Released++;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class RecordingJobs : IBackgroundJobClient
    {
        public List<(Job Job, IState State)> Created { get; } = [];

        public string Create(Job job, IState state)
        {
            Created.Add((job, state));
            return Created.Count.ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => false;
    }

}
