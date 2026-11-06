using System.Text;
using Microsoft.AspNetCore.Http;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;
using PvAutopilot.Host.Endpoints;
using PvAutopilot.Infrastructure.Jobs;
using PvAutopilot.Tests.Core;

namespace PvAutopilot.Tests.Host;

public class AdminSubmissionResolutionTests
{
    private readonly ProcessHarness _h = new(SubmissionMode.Live);
    private readonly FakeRunLock _lock = new();

    private async Task<int?> Resolve(Guid id, string kind, string json)
    {
        var result = await AdminSubmissionResolution.PostAsync(id, kind, new MemoryStream(Encoding.UTF8.GetBytes(json)),
            _h.Store, _h.Ledger, _h.Handler(), new AuditLog([_h.Audit]), _lock, default);
        return ((IStatusCodeHttpResult)result).StatusCode;
    }

    /// <summary>Netzanmeldung bricht ab, der Retry blockiert den Vorgang wegen unklarem Versand.</summary>
    private async Task<AnmeldeProcess> BlockedByUncertainNetzanmeldung()
    {
        _h.Adapter.FakeChannel.Failure = new HttpRequestException("Verbindung abgebrochen");
        await Assert.ThrowsAsync<HttpRequestException>(() => _h.TriggerAsync(ProcessTriggerKind.Start));
        _h.Adapter.FakeChannel.Failure = null;
        await _h.Engine().RunAsync(_h.Process.Id, default);
        Assert.Equal(ProcessState.Blocked, _h.Process.State);
        return _h.Process;
    }

    [Fact]
    public async Task Sent_marks_the_entry_and_continues_without_resending()
    {
        var process = await BlockedByUncertainNetzanmeldung();

        var status = await Resolve(process.Id, "netzanmeldung", """{"outcome": "Sent", "reference": " NB-4711 "}""");
        await _h.DrainAsync();

        Assert.Equal(StatusCodes.Status200OK, status);
        var record = _h.Ledger.Records[$"{process.Id:N}:Netzanmeldung"];
        Assert.Equal(SubmissionStatus.Sent, record.Status);
        Assert.Equal("NB-4711", record.ExternalReference);
        Assert.Equal(ProcessState.NetzanmeldungSubmitted, process.State);
        Assert.Equal("NB-4711", process.NetzbetreiberReferenz);
        Assert.Empty(_h.Adapter.FakeChannel.Sent);
        Assert.Contains(_h.Audit.Events, e => e.Type == "submission-resolved" && !e.Message.Contains("NB-4711"));
    }

    [Fact]
    public async Task Not_sent_allows_exactly_one_new_submission()
    {
        var process = await BlockedByUncertainNetzanmeldung();

        Assert.Equal(StatusCodes.Status200OK, await Resolve(process.Id, "Netzanmeldung", """{"outcome": "NotSent"}"""));
        await _h.DrainAsync();

        Assert.Equal(ProcessState.NetzanmeldungSubmitted, process.State);
        Assert.Single(_h.Adapter.FakeChannel.Sent);
        Assert.Equal(SubmissionStatus.Sent, _h.Ledger.Records[$"{process.Id:N}:Netzanmeldung"].Status);
    }

    [Fact]
    public async Task Uses_the_key_of_the_current_revision()
    {
        await _h.TriggerAsync(ProcessTriggerKind.Start);
        await _h.TriggerAsync(ProcessTriggerKind.RueckfrageReceived);
        _h.Adapter.FakeChannel.Failure = new HttpRequestException("Verbindung abgebrochen");
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            _h.TriggerAsync(ProcessTriggerKind.RueckfrageResolved, resolution: RueckfrageResolution.Resubmit));
        _h.Adapter.FakeChannel.Failure = null;
        await _h.Engine().RunAsync(_h.Process.Id, default);
        var process = _h.Process;
        Assert.Equal((1, ProcessState.Blocked), (process.Revision, process.State));

        Assert.Equal(StatusCodes.Status200OK, await Resolve(process.Id, "Netzanmeldung", """{"outcome": "Sent"}"""));

        Assert.Equal(SubmissionStatus.Sent, _h.Ledger.Records[$"{process.Id:N}:Netzanmeldung:r1"].Status);
        Assert.Equal(SubmissionStatus.Sent, _h.Ledger.Records[$"{process.Id:N}:Netzanmeldung"].Status);
        Assert.Equal([process.Id], _h.Scheduler.Enqueued);
    }

    [Fact]
    public async Task Entry_that_is_not_sending_is_a_conflict()
    {
        await _h.TriggerAsync(ProcessTriggerKind.Start);
        var process = _h.Process;
        process.Block("Test", TestData.Now);

        Assert.Equal(StatusCodes.Status409Conflict, await Resolve(process.Id, "Netzanmeldung", """{"outcome": "NotSent"}"""));
        Assert.Equal(SubmissionStatus.Sent, _h.Ledger.Records[$"{process.Id:N}:Netzanmeldung"].Status);
        Assert.DoesNotContain(_h.Audit.Events, e => e.Type == "submission-resolved");
    }

    [Fact]
    public async Task Sending_entry_of_a_process_that_is_not_blocked_is_a_conflict()
    {
        // Steht der Vorgang nicht auf Blocked, kann gerade ein Lauf versenden: NotSent löste dann einen zweiten Versand aus.
        var process = _h.Store.Add(new AnmeldeProcess(ProcessHarness.ProjectId, TestData.Now));
        _h.Ledger.Put(new SubmissionKey(process.Id, SubmissionKind.Mastr), SubmissionStatus.Sending);

        Assert.Equal(StatusCodes.Status409Conflict, await Resolve(process.Id, "Mastr", """{"outcome": "NotSent"}"""));
        Assert.Equal(SubmissionStatus.Sending, _h.Ledger.Records[$"{process.Id:N}:Mastr"].Status);
        Assert.Empty(_h.Scheduler.Enqueued);
        Assert.DoesNotContain(_h.Audit.Events, e => e.Type == "submission-resolved");
    }

    [Fact]
    public async Task Busy_process_is_a_conflict_and_lock_is_released_afterwards()
    {
        var process = await BlockedByUncertainNetzanmeldung();
        _lock.Busy = true;

        Assert.Equal(StatusCodes.Status409Conflict, await Resolve(process.Id, "Netzanmeldung", """{"outcome": "NotSent"}"""));
        Assert.Equal(SubmissionStatus.Sending, _h.Ledger.Records[$"{process.Id:N}:Netzanmeldung"].Status);
        Assert.Equal(ProcessState.Blocked, process.State);

        _lock.Busy = false;
        Assert.Equal(StatusCodes.Status200OK, await Resolve(process.Id, "Netzanmeldung", """{"outcome": "NotSent"}"""));
        Assert.Equal((1, 1), (_lock.Acquired, _lock.Released));
    }

    [Fact]
    public async Task Unknown_process_or_entry_is_not_found()
    {
        var process = _h.Store.Add(new AnmeldeProcess(ProcessHarness.ProjectId, TestData.Now));
        process.Block("Test", TestData.Now);

        Assert.Equal(StatusCodes.Status404NotFound, await Resolve(Guid.NewGuid(), "Netzanmeldung", """{"outcome": "Sent"}"""));
        Assert.Equal(StatusCodes.Status404NotFound, await Resolve(process.Id, "Inbetriebnahme", """{"outcome": "Sent"}"""));
    }

    [Theory]
    [InlineData("Gibtsnicht", """{"outcome": "Sent"}""")]
    [InlineData("1", """{"outcome": "Sent"}""")]
    [InlineData("Netzanmeldung,Mastr", """{"outcome": "Sent"}""")]
    [InlineData("Netzanmeldung", "")]
    [InlineData("Netzanmeldung", "kein json")]
    [InlineData("Netzanmeldung", "{}")]
    [InlineData("Netzanmeldung", """{"outcome": "Vielleicht"}""")]
    [InlineData("Netzanmeldung", """{"outcome": 0}""")]
    [InlineData("Netzanmeldung", """{"outcome": "NotSent", "reference": "NB-1"}""")]
    public async Task Invalid_requests_are_rejected(string kind, string json)
    {
        var process = await BlockedByUncertainNetzanmeldung();

        Assert.Equal(StatusCodes.Status400BadRequest, await Resolve(process.Id, kind, json));
        Assert.Equal(SubmissionStatus.Sending, _h.Ledger.Records[$"{process.Id:N}:Netzanmeldung"].Status);
        Assert.Equal(ProcessState.Blocked, process.State);
    }

    [Fact]
    public async Task Too_long_reference_is_rejected()
    {
        var process = await BlockedByUncertainNetzanmeldung();

        Assert.Equal(StatusCodes.Status400BadRequest,
            await Resolve(process.Id, "Netzanmeldung", $$"""{"outcome": "Sent", "reference": "{{new string('x', 201)}}"}"""));
    }
}

/// <summary>Prozess-Lock ohne Datenbank; <see cref="Busy"/> simuliert einen laufenden Lauf.</summary>
internal sealed class FakeRunLock : IProcessRunLock
{
    public bool Busy { get; set; }
    public int Acquired { get; private set; }
    public int Released { get; private set; }

    public Task<IAsyncDisposable?> TryAcquireAsync(Guid processId, TimeSpan wait, CancellationToken ct)
    {
        if (Busy)
            return Task.FromResult<IAsyncDisposable?>(null);
        Acquired++;
        return Task.FromResult<IAsyncDisposable?>(new Held(this));
    }

    private sealed class Held(FakeRunLock owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.Released++;
            return ValueTask.CompletedTask;
        }
    }
}
