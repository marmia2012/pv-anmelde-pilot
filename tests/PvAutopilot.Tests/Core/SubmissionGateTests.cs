using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Tests.Core;

public class SubmissionGateTests
{
    private readonly InMemoryLedger _ledger = new();
    private readonly FakeChannel _channel = new();
    private readonly ApplicationPackage _package = new(Guid.NewGuid(), SubmissionKind.Netzanmeldung, "netze-duisburg", [], []);

    private SubmissionGate Gate(SubmissionMode mode) =>
        new(_ledger, Options.Create(new SubmissionOptions { Mode = mode }), NullLogger<SubmissionGate>.Instance);

    [Fact]
    public void Default_mode_is_DryRun()
    {
        Assert.Equal(SubmissionMode.DryRun, new SubmissionOptions().Mode);
    }

    [Fact]
    public async Task DryRun_never_sends()
    {
        var outcome = await Gate(SubmissionMode.DryRun).SubmitAsync(_package, _channel, reviewApproved: true, default);

        Assert.Equal(SubmissionOutcome.DryRun, outcome);
        Assert.Equal(0, _channel.Sent);
        Assert.Equal(SubmissionStatus.DryRun, _ledger.Records[_package.IdempotencyKey].Status);
    }

    [Fact]
    public async Task Review_waits_for_approval()
    {
        var gate = Gate(SubmissionMode.Review);

        Assert.Equal(SubmissionOutcome.AwaitingReview, await gate.SubmitAsync(_package, _channel, false, default));
        Assert.Equal(0, _channel.Sent);

        Assert.Equal(SubmissionOutcome.Sent, await gate.SubmitAsync(_package, _channel, true, default));
        Assert.Equal(1, _channel.Sent);
    }

    [Fact]
    public async Task Live_sends_exactly_once()
    {
        var gate = Gate(SubmissionMode.Live);

        Assert.Equal(SubmissionOutcome.Sent, await gate.SubmitAsync(_package, _channel, false, default));
        Assert.Equal(SubmissionOutcome.AlreadySent, await gate.SubmitAsync(_package, _channel, false, default));
        Assert.Equal(1, _channel.Sent);
    }

    [Fact]
    public async Task Crash_during_send_is_reported_as_uncertain_and_not_resent()
    {
        var gate = Gate(SubmissionMode.Live);
        _channel.Failure = new HttpRequestException("Verbindung abgebrochen");

        await Assert.ThrowsAsync<HttpRequestException>(() => gate.SubmitAsync(_package, _channel, false, default));
        _channel.Failure = null;

        Assert.Equal(SubmissionOutcome.Uncertain, await gate.SubmitAsync(_package, _channel, false, default));
        Assert.Equal(0, _channel.Sent);
    }

    [Fact]
    public async Task NotSent_failure_allows_retry()
    {
        var gate = Gate(SubmissionMode.Live);
        _channel.Failure = new SubmissionNotSentException("Validierung im Portal fehlgeschlagen");

        await Assert.ThrowsAsync<SubmissionNotSentException>(() => gate.SubmitAsync(_package, _channel, false, default));
        _channel.Failure = null;

        Assert.Equal(SubmissionOutcome.Sent, await gate.SubmitAsync(_package, _channel, false, default));
    }

    private sealed class FakeChannel : ISubmissionChannel
    {
        public int Sent { get; private set; }
        public Exception? Failure { get; set; }
        public string Name => "fake";

        public Task<SubmissionReceipt> SendAsync(ApplicationPackage package, CancellationToken ct)
        {
            if (Failure is not null)
                throw Failure;
            Sent++;
            return Task.FromResult(new SubmissionReceipt("ref-1"));
        }
    }

    private sealed class InMemoryLedger : ISubmissionLedger
    {
        public Dictionary<string, SubmissionRecord> Records { get; } = [];

        public Task<SubmissionRecord?> FindAsync(string key, CancellationToken ct) =>
            Task.FromResult(Records.GetValueOrDefault(key));

        public Task RecordDryRunAsync(ApplicationPackage package, string channel, CancellationToken ct)
        {
            Records[package.IdempotencyKey] = New(package, channel, SubmissionStatus.DryRun);
            return Task.CompletedTask;
        }

        public Task<bool> TryReserveAsync(ApplicationPackage package, string channel, CancellationToken ct)
        {
            if (Records.TryGetValue(package.IdempotencyKey, out var r) && r.Status is SubmissionStatus.Sending or SubmissionStatus.Sent)
                return Task.FromResult(false);
            Records[package.IdempotencyKey] = New(package, channel, SubmissionStatus.Sending);
            return Task.FromResult(true);
        }

        public Task MarkSentAsync(string key, string? externalReference, CancellationToken ct)
        {
            Records[key].Status = SubmissionStatus.Sent;
            Records[key].ExternalReference = externalReference;
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(string key, CancellationToken ct)
        {
            Records[key].Status = SubmissionStatus.Failed;
            return Task.CompletedTask;
        }

        private static SubmissionRecord New(ApplicationPackage p, string channel, SubmissionStatus status) => new()
        {
            IdempotencyKey = p.IdempotencyKey, ProcessId = p.ProcessId, Kind = p.Kind, Channel = channel, Status = status,
        };
    }
}
