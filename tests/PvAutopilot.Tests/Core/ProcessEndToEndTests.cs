using Microsoft.Extensions.Logging.Abstractions;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Tests.Core;

/// <summary>Ganzer Vorgang von „Vollmacht zurück“ bis Completed – nur Fakes, ausgelöst über Ereignisse wie aus HubSpot.</summary>
public class ProcessEndToEndTests
{
    private static readonly DateOnly Zusage = new(2026, 11, 20);
    private static readonly DateOnly Inbetriebnahme = new(2027, 1, 15);
    private static readonly DateOnly Zaehlerwechsel = new(2027, 2, 1);

    private static void Advance(ProcessHarness h, TimeSpan by) => h.Time.Now += by;

    /// <summary>Das E.8 geht erst am Tag der Inbetriebnahme raus; die Tests springen dorthin (10 Uhr UTC).</summary>
    private static void OnDay(ProcessHarness h, DateOnly day) =>
        h.Time.Now = new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

    [Fact]
    public async Task Live_runs_through_all_stages()
    {
        var h = new ProcessHarness(SubmissionMode.Live);

        await h.TriggerAsync(ProcessTriggerKind.Start);
        Assert.Equal(ProcessState.NetzanmeldungSubmitted, h.Process.State);

        await h.TriggerAsync(ProcessTriggerKind.EinspeisezusageReceived, date: Zusage, reference: "NB-1");
        Assert.Equal(ProcessState.EinspeisezusageReceived, h.Process.State);

        OnDay(h, Inbetriebnahme);
        await h.TriggerAsync(ProcessTriggerKind.InbetriebnahmeReported, date: Inbetriebnahme);
        Assert.Equal(ProcessState.ZaehlerwechselRequested, h.Process.State);

        await h.TriggerAsync(ProcessTriggerKind.ZaehlerwechselCompleted, date: Zaehlerwechsel);

        var process = h.Process;
        Assert.Equal(ProcessState.Completed, process.State);
        Assert.Equal([SubmissionKind.Netzanmeldung, SubmissionKind.Inbetriebnahme, SubmissionKind.Zaehlerwechsel],
            h.Adapter.FakeChannel.Sent.Select(p => p.Kind));
        Assert.Equal(1, h.Registrar.Calls);
        Assert.Empty(h.Credentials.Logins);
        Assert.Equal("SEE900000000001", process.MastrNummerAnlage);
        Assert.Equal(ProcessState.Completed, h.CrmStatus.Published[^1]);
        Assert.Empty(h.Tasks.Calls);
        Assert.Equal(4, h.Ledger.Records.Values.Count(r => r.Status == SubmissionStatus.Sent));
    }

    [Fact]
    public async Task Live_skips_zaehlerantrag_when_it_goes_out_with_the_E8()
    {
        var h = new ProcessHarness(SubmissionMode.Live);
        h.Adapter.Einreichungen = new HashSet<SubmissionKind> { SubmissionKind.Netzanmeldung, SubmissionKind.Inbetriebnahme };

        await h.TriggerAsync(ProcessTriggerKind.Start);
        await h.TriggerAsync(ProcessTriggerKind.EinspeisezusageReceived, date: Zusage);
        OnDay(h, Inbetriebnahme);
        await h.TriggerAsync(ProcessTriggerKind.InbetriebnahmeReported, date: Inbetriebnahme);

        Assert.Equal(ProcessState.ZaehlerwechselRequested, h.Process.State);
        Assert.Equal([SubmissionKind.Netzanmeldung, SubmissionKind.Inbetriebnahme], h.Adapter.FakeChannel.Sent.Select(p => p.Kind));
        Assert.DoesNotContain(h.Process.Transitions, t => t.To == ProcessState.ZaehlerwechselPrepared);
    }

    [Fact]
    public async Task Review_needs_approval_per_stage()
    {
        var h = new ProcessHarness(SubmissionMode.Review);

        await h.TriggerAsync(ProcessTriggerKind.Start);
        Assert.Equal(ProcessState.NetzanmeldungAwaitingReview, h.Process.State);
        Assert.Contains(h.Tasks.Keys, k => k.StartsWith($"review:{h.Process.Id:N}:Netzanmeldung:r0:"));

        // Freigabe einer anderen Stufe gibt nichts frei.
        Assert.Equal(TriggerResult.Ignored,
            await h.TriggerAsync(ProcessTriggerKind.ReviewApproved, submission: SubmissionKind.Inbetriebnahme));
        Assert.Empty(h.Adapter.FakeChannel.Sent);

        await h.TriggerAsync(ProcessTriggerKind.ReviewApproved, submission: SubmissionKind.Netzanmeldung);
        Assert.Equal(ProcessState.NetzanmeldungSubmitted, h.Process.State);

        await h.TriggerAsync(ProcessTriggerKind.EinspeisezusageReceived, date: Zusage);
        OnDay(h, Inbetriebnahme);
        await h.TriggerAsync(ProcessTriggerKind.InbetriebnahmeReported, date: Inbetriebnahme);
        Assert.Equal(ProcessState.InbetriebnahmeAwaitingReview, h.Process.State);

        // Eine stehengebliebene Freigabe der Netzanmeldung gilt nicht für das E.8.
        Assert.Equal(TriggerResult.Ignored,
            await h.TriggerAsync(ProcessTriggerKind.ReviewApproved, submission: SubmissionKind.Netzanmeldung));

        await h.TriggerAsync(ProcessTriggerKind.ReviewApproved, submission: SubmissionKind.Inbetriebnahme);
        Assert.Equal(ProcessState.ZaehlerwechselAwaitingReview, h.Process.State);

        await h.TriggerAsync(ProcessTriggerKind.ReviewApproved, submission: SubmissionKind.Zaehlerwechsel);
        Assert.Equal(ProcessState.ZaehlerwechselRequested, h.Process.State);

        await h.TriggerAsync(ProcessTriggerKind.ZaehlerwechselCompleted, date: Zaehlerwechsel);
        Assert.Equal(ProcessState.MastrAwaitingReview, h.Process.State);
        Assert.Equal(0, h.Registrar.Calls);

        await h.TriggerAsync(ProcessTriggerKind.ReviewApproved, submission: SubmissionKind.Mastr);

        Assert.Equal(ProcessState.Completed, h.Process.State);
        Assert.Equal(3, h.Adapter.FakeChannel.Sent.Count);
        Assert.Equal(1, h.Registrar.Calls);
        Assert.Equal(4, h.Tasks.Keys.Count(k => k.StartsWith("review:")));
        Assert.Equal(3, h.Archive.Stored);
    }

    [Fact]
    public async Task DryRun_stops_after_preparing_and_sends_nothing()
    {
        var h = new ProcessHarness(SubmissionMode.DryRun);

        await h.TriggerAsync(ProcessTriggerKind.Start);
        await h.TriggerAsync(ProcessTriggerKind.Start);

        Assert.Equal(ProcessState.NetzanmeldungPrepared, h.Process.State);
        Assert.Empty(h.Adapter.FakeChannel.Sent);
        Assert.Equal(1, h.Archive.Stored);
        Assert.Equal(SubmissionStatus.DryRun, Assert.Single(h.Ledger.Records.Values).Status);
    }

    [Fact]
    public async Task DryRun_with_ContinueAfterDryRun_simulates_the_whole_process()
    {
        var h = new ProcessHarness(SubmissionMode.DryRun);
        h.Submission.ContinueAfterDryRun = true;
        h.Credentials.Logins.Clear(); // Demo ohne Kundenlogin

        await h.TriggerAsync(ProcessTriggerKind.Start);
        Assert.Equal(ProcessState.NetzanmeldungSubmitted, h.Process.State);
        await h.TriggerAsync(ProcessTriggerKind.EinspeisezusageReceived, date: Zusage);
        OnDay(h, Inbetriebnahme);
        await h.TriggerAsync(ProcessTriggerKind.InbetriebnahmeReported, date: Inbetriebnahme);
        await h.TriggerAsync(ProcessTriggerKind.ZaehlerwechselCompleted, date: Zaehlerwechsel);

        Assert.Equal(ProcessState.Completed, h.Process.State);
        Assert.Empty(h.Adapter.FakeChannel.Sent);
        Assert.Equal(0, h.Registrar.Calls);
        Assert.All(h.Ledger.Records.Values, r => Assert.Equal(SubmissionStatus.DryRun, r.Status));
        Assert.Equal(4, h.Ledger.Records.Count);
        Assert.Empty(h.Tasks.Calls);
    }

    [Fact]
    public async Task Rueckfrage_answered_directly_then_second_rueckfrage_gets_new_task()
    {
        var h = new ProcessHarness(SubmissionMode.Live);
        await h.TriggerAsync(ProcessTriggerKind.Start);

        Advance(h, TimeSpan.FromDays(3));
        await h.TriggerAsync(ProcessTriggerKind.RueckfrageReceived);
        Assert.Equal(ProcessState.RueckfrageOpen, h.Process.State);
        await h.TriggerAsync(ProcessTriggerKind.RueckfrageReceived); // doppelt
        Assert.Single(h.Tasks.Keys);

        await h.TriggerAsync(ProcessTriggerKind.RueckfrageResolved, resolution: RueckfrageResolution.AnsweredDirectly);
        Assert.Equal(ProcessState.NetzanmeldungSubmitted, h.Process.State);

        Advance(h, TimeSpan.FromDays(3));
        await h.TriggerAsync(ProcessTriggerKind.RueckfrageReceived);
        Assert.Equal(2, h.Tasks.Keys.Count(k => k.StartsWith("rueckfrage:")));

        await h.TriggerAsync(ProcessTriggerKind.EinspeisezusageReceived, date: Zusage);
        Assert.Equal(ProcessState.EinspeisezusageReceived, h.Process.State);
        Assert.Null(h.Process.RueckfrageOffenSeit);
        Assert.Single(h.Adapter.FakeChannel.Sent);
    }

    [Fact]
    public async Task Rueckfrage_resubmit_aggregates_again_and_submits_with_new_idempotency_key()
    {
        var h = new ProcessHarness(SubmissionMode.Live);
        await h.TriggerAsync(ProcessTriggerKind.Start);
        await h.TriggerAsync(ProcessTriggerKind.RueckfrageReceived);

        await h.TriggerAsync(ProcessTriggerKind.RueckfrageResolved, resolution: RueckfrageResolution.Resubmit);

        var process = h.Process;
        Assert.Equal(ProcessState.NetzanmeldungSubmitted, process.State);
        Assert.Equal(1, process.Revision);
        Assert.Equal(2, h.Planning.Calls);
        Assert.Equal([0, 1], h.Adapter.FakeChannel.Sent.Select(p => p.Revision));
        Assert.Contains($"{process.Id:N}:Netzanmeldung", h.Ledger.Records.Keys);
        Assert.Contains($"{process.Id:N}:Netzanmeldung:r1", h.Ledger.Records.Keys);

        // Nach der Zusage laufen E.8 & Co. normal (ohne Revisions-Suffix).
        await h.TriggerAsync(ProcessTriggerKind.EinspeisezusageReceived, date: Zusage);
        OnDay(h, Inbetriebnahme);
        await h.TriggerAsync(ProcessTriggerKind.InbetriebnahmeReported, date: Inbetriebnahme);
        Assert.Contains($"{process.Id:N}:Inbetriebnahme", h.Ledger.Records.Keys);
    }

    [Fact]
    public async Task Events_before_their_time_are_used_when_the_process_gets_there()
    {
        var h = new ProcessHarness(SubmissionMode.Live);
        await h.TriggerAsync(ProcessTriggerKind.Start);

        // Inbetriebnahme und Zählerwechsel kommen vor der Zusage.
        await h.TriggerAsync(ProcessTriggerKind.InbetriebnahmeReported, date: Inbetriebnahme);
        await h.TriggerAsync(ProcessTriggerKind.ZaehlerwechselCompleted, date: Zaehlerwechsel);
        Assert.Equal(ProcessState.NetzanmeldungSubmitted, h.Process.State);
        Assert.Single(h.Adapter.FakeChannel.Sent);

        await h.TriggerAsync(ProcessTriggerKind.EinspeisezusageReceived, date: Zusage);

        // Das Inbetriebnahmedatum liegt noch in der Zukunft: kein E.8 vor der Inbetriebnahme.
        Assert.Equal(ProcessState.EinspeisezusageReceived, h.Process.State);
        Assert.Single(h.Adapter.FakeChannel.Sent);

        // Am Tag der Inbetriebnahme holt der Watchdog den Vorgang ab, ohne neues Ereignis.
        OnDay(h, Inbetriebnahme);
        Assert.Equal(1, (await h.Watchdog().RunAsync(default)).Rescheduled);
        await h.DrainAsync();

        Assert.Equal(ProcessState.Completed, h.Process.State);
        Assert.Equal(Inbetriebnahme, h.Process.Inbetriebnahmedatum);
    }

    [Fact]
    public async Task Duplicate_events_send_nothing_twice()
    {
        var h = new ProcessHarness(SubmissionMode.Live);

        await h.TriggerAsync(ProcessTriggerKind.Start);
        await h.TriggerAsync(ProcessTriggerKind.Start);
        await h.TriggerAsync(ProcessTriggerKind.EinspeisezusageReceived, date: Zusage);
        await h.TriggerAsync(ProcessTriggerKind.EinspeisezusageReceived, date: Zusage);
        OnDay(h, Inbetriebnahme);
        await h.TriggerAsync(ProcessTriggerKind.InbetriebnahmeReported, date: Inbetriebnahme);
        await h.TriggerAsync(ProcessTriggerKind.InbetriebnahmeReported, date: Inbetriebnahme);
        await h.TriggerAsync(ProcessTriggerKind.ZaehlerwechselCompleted, date: Zaehlerwechsel);
        Assert.Equal(TriggerResult.Ignored, await h.TriggerAsync(ProcessTriggerKind.ZaehlerwechselCompleted, date: Zaehlerwechsel));

        Assert.Equal(ProcessState.Completed, h.Process.State);
        Assert.Equal(3, h.Adapter.FakeChannel.Sent.Count);
        Assert.Equal(1, h.Registrar.Calls);
        Assert.Equal(h.Process.Transitions.Count, h.Process.Transitions.Distinct().Count());
    }

    [Fact]
    public async Task Missing_mastr_login_waits_until_it_is_provided()
    {
        var h = new ProcessHarness(SubmissionMode.Live);
        h.Credentials.Logins.Clear();
        await h.TriggerAsync(ProcessTriggerKind.Start);
        await h.TriggerAsync(ProcessTriggerKind.EinspeisezusageReceived, date: Zusage);
        OnDay(h, Inbetriebnahme);
        await h.TriggerAsync(ProcessTriggerKind.InbetriebnahmeReported, date: Inbetriebnahme);
        await h.TriggerAsync(ProcessTriggerKind.ZaehlerwechselCompleted, date: Zaehlerwechsel);

        Assert.Equal(ProcessState.ZaehlerwechselCompleted, h.Process.State);
        Assert.Contains($"mastr-login-missing:{h.Process.Id:N}", h.Tasks.Keys);

        h.Credentials.Logins[ProcessHarness.ProjectId] = new("kunde", "geheim");
        await h.TriggerAsync(ProcessTriggerKind.MastrCredentialsProvided);

        Assert.Equal(ProcessState.Completed, h.Process.State);
        Assert.Empty(h.Credentials.Logins);
    }

    [Fact]
    public async Task Uncertain_submission_blocks_and_never_resends()
    {
        var h = new ProcessHarness(SubmissionMode.Live);
        h.Adapter.FakeChannel.Failure = new HttpRequestException("Verbindung abgebrochen");

        await Assert.ThrowsAsync<HttpRequestException>(() => h.TriggerAsync(ProcessTriggerKind.Start));
        h.Adapter.FakeChannel.Failure = null;

        // Hangfire-Retry
        await h.Engine().RunAsync(h.Process.Id, default);

        Assert.Equal(ProcessState.Blocked, h.Process.State);
        Assert.Empty(h.Adapter.FakeChannel.Sent);
        Assert.Contains(h.Tasks.Keys, k => k.StartsWith("submission-uncertain:"));
        Assert.Equal(ProcessState.Blocked, h.CrmStatus.Published[^1]);

        // Weitere Ereignisse halten nur Fakten fest; der Vorgang läuft erst nach Prüfung weiter.
        await h.TriggerAsync(ProcessTriggerKind.EinspeisezusageReceived, date: Zusage);
        Assert.Equal(ProcessState.Blocked, h.Process.State);
        Assert.Empty(h.Adapter.FakeChannel.Sent);
    }

    [Fact]
    public async Task Prepared_process_continues_after_switch_from_dry_run_to_review()
    {
        var h = new ProcessHarness(SubmissionMode.DryRun);
        await h.TriggerAsync(ProcessTriggerKind.Start);
        Assert.Equal(ProcessState.NetzanmeldungPrepared, h.Process.State);

        h.Submission.Mode = SubmissionMode.Review;
        Advance(h, TimeSpan.FromDays(1));
        await h.Watchdog().RunAsync(default);
        await h.DrainAsync();

        Assert.Equal(ProcessState.NetzanmeldungAwaitingReview, h.Process.State);
        Assert.Contains(h.Tasks.Keys, k => k.StartsWith("review:"));
        Assert.Empty(h.Adapter.FakeChannel.Sent);
    }

    [Fact]
    public async Task Review_approval_is_needed_again_after_block_and_resume()
    {
        var h = new ProcessHarness(SubmissionMode.Review);
        await h.TriggerAsync(ProcessTriggerKind.Start);
        var approvedAt = h.Time.Now.AddMinutes(5);
        Advance(h, TimeSpan.FromMinutes(10));

        // Freigegeben, Versand scheitert nachweislich; nach allen Retries blockiert.
        h.Adapter.FakeChannel.Failure = new SubmissionNotSentException("Portal nicht erreichbar");
        await Assert.ThrowsAsync<SubmissionNotSentException>(() => h.TriggerAsync(ProcessTriggerKind.ReviewApproved,
            submission: SubmissionKind.Netzanmeldung, occurredAt: approvedAt));
        await new ProcessFailureHandler(h.Store, new CountingAlert(), h.Tasks, h.CrmStatus, new AuditLog([h.Audit]), h.Time,
            NullLogger<ProcessFailureHandler>.Instance).BlockAsync(h.Process.Id, new HttpRequestException("HTTP 503"), default);
        h.Adapter.FakeChannel.Failure = null;

        Advance(h, TimeSpan.FromHours(1));
        await h.Handler().ResumeAsync(h.Process.Id, default);
        await h.DrainAsync();

        // Die Freigabe ist mit der Blockade verfallen: neuer Freigabe-Task, nichts versendet.
        Assert.Equal(ProcessState.NetzanmeldungAwaitingReview, h.Process.State);
        Assert.Equal(2, h.Tasks.Keys.Count(k => k.StartsWith("review:")));
        Assert.Empty(h.Adapter.FakeChannel.Sent);

        // Das alte Freigabe-Ereignis kommt erneut (HubSpot-Retry): gibt nichts frei.
        Assert.Equal(TriggerResult.Ignored, await h.TriggerAsync(ProcessTriggerKind.ReviewApproved,
            submission: SubmissionKind.Netzanmeldung, occurredAt: approvedAt));
        Assert.Empty(h.Adapter.FakeChannel.Sent);

        Assert.Equal(TriggerResult.Applied, await h.TriggerAsync(ProcessTriggerKind.ReviewApproved,
            submission: SubmissionKind.Netzanmeldung, occurredAt: h.Time.Now));
        Assert.Equal(ProcessState.NetzanmeldungSubmitted, h.Process.State);
        Assert.Single(h.Adapter.FakeChannel.Sent);
    }

    [Fact]
    public async Task Late_redelivered_rueckfrage_does_not_reopen_a_closed_rueckfrage()
    {
        var h = new ProcessHarness(SubmissionMode.Live);
        await h.TriggerAsync(ProcessTriggerKind.Start);
        var receivedAt = h.Time.Now;
        await h.TriggerAsync(ProcessTriggerKind.RueckfrageReceived, occurredAt: receivedAt);
        Advance(h, TimeSpan.FromHours(1));
        await h.TriggerAsync(ProcessTriggerKind.RueckfrageResolved, resolution: RueckfrageResolution.AnsweredDirectly);
        Assert.Equal(ProcessState.NetzanmeldungSubmitted, h.Process.State);

        Advance(h, TimeSpan.FromHours(1));
        Assert.Equal(TriggerResult.Ignored, await h.TriggerAsync(ProcessTriggerKind.RueckfrageReceived, occurredAt: receivedAt));

        Assert.Equal(ProcessState.NetzanmeldungSubmitted, h.Process.State);
        Assert.Single(h.Tasks.Keys, k => k.StartsWith("rueckfrage:"));
    }

    [Fact]
    public async Task Cancellation_after_submission_creates_withdraw_task()
    {
        var h = new ProcessHarness(SubmissionMode.Live);
        await h.TriggerAsync(ProcessTriggerKind.Start);

        await h.TriggerAsync(ProcessTriggerKind.Cancelled);

        Assert.Equal(ProcessState.Cancelled, h.Process.State);
        Assert.Contains($"withdraw:{h.Process.Id:N}", h.Tasks.Keys);
        Assert.Equal(TriggerResult.Ignored, await h.TriggerAsync(ProcessTriggerKind.EinspeisezusageReceived, date: Zusage));
    }
}
