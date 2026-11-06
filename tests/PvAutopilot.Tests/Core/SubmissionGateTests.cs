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
        Assert.False(new SubmissionOptions().ContinueAfterDryRun);
    }

    [Fact]
    public async Task DryRun_never_sends()
    {
        var result = await Gate(SubmissionMode.DryRun).SubmitAsync(_package, _channel, reviewApproved: true, default);

        Assert.Equal(SubmissionOutcome.DryRun, result.Outcome);
        Assert.Empty(_channel.Sent);
        Assert.Equal(SubmissionStatus.DryRun, _ledger.Records[_package.IdempotencyKey].Status);
    }

    [Fact]
    public async Task Review_waits_for_approval()
    {
        var gate = Gate(SubmissionMode.Review);

        Assert.Equal(SubmissionOutcome.AwaitingReview, (await gate.SubmitAsync(_package, _channel, false, default)).Outcome);
        Assert.Empty(_channel.Sent);

        Assert.Equal(SubmissionOutcome.Sent, (await gate.SubmitAsync(_package, _channel, true, default)).Outcome);
        Assert.Single(_channel.Sent);
    }

    [Fact]
    public async Task Live_sends_exactly_once_and_returns_reference_again()
    {
        var gate = Gate(SubmissionMode.Live);

        var first = await gate.SubmitAsync(_package, _channel, false, default);
        var second = await gate.SubmitAsync(_package, _channel, false, default);

        Assert.Equal(new SubmissionResult(SubmissionOutcome.Sent, "NB-4711"), first);
        Assert.Equal(new SubmissionResult(SubmissionOutcome.AlreadySent, "NB-4711"), second);
        Assert.Single(_channel.Sent);
    }

    [Fact]
    public async Task Crash_during_send_is_reported_as_uncertain_and_not_resent()
    {
        var gate = Gate(SubmissionMode.Live);
        _channel.Failure = new HttpRequestException("Verbindung abgebrochen");

        await Assert.ThrowsAsync<HttpRequestException>(() => gate.SubmitAsync(_package, _channel, false, default));
        _channel.Failure = null;

        Assert.Equal(SubmissionOutcome.Uncertain, (await gate.SubmitAsync(_package, _channel, false, default)).Outcome);
        Assert.Empty(_channel.Sent);
    }

    [Fact]
    public async Task NotSent_failure_allows_retry()
    {
        var gate = Gate(SubmissionMode.Live);
        _channel.Failure = new SubmissionNotSentException("Validierung im Portal fehlgeschlagen");

        await Assert.ThrowsAsync<SubmissionNotSentException>(() => gate.SubmitAsync(_package, _channel, false, default));
        _channel.Failure = null;

        Assert.Equal(SubmissionOutcome.Sent, (await gate.SubmitAsync(_package, _channel, false, default)).Outcome);
    }

    [Fact]
    public async Task New_revision_has_its_own_idempotency_key()
    {
        var gate = Gate(SubmissionMode.Live);

        await gate.SubmitAsync(_package, _channel, false, default);
        var resubmitted = await gate.SubmitAsync(_package with { Revision = 1 }, _channel, false, default);

        Assert.Equal(SubmissionOutcome.Sent, resubmitted.Outcome);
        Assert.Equal(2, _channel.Sent.Count);
        Assert.EndsWith(":Netzanmeldung:r1", (_package with { Revision = 1 }).IdempotencyKey);
        Assert.EndsWith(":Netzanmeldung", _package.IdempotencyKey);
    }
}
