using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Processes.Steps;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Tests.Core;

public class ProcessStepTests
{
    private readonly ProcessHarness _h = new();

    private AnmeldeProcess Process(params ProcessState[] path)
    {
        var process = _h.Store.Add(new AnmeldeProcess(ProcessHarness.ProjectId, TestData.Now));
        process.AttachDataCard(TestData.Card(), TestData.Now);
        process.AssignNetzbetreiber(FakeAdapter.TestCode, TestData.Now);
        foreach (var state in path)
            process.TransitionTo(state, TestData.Now);
        return process;
    }

    private AnmeldeProcess NetzanmeldungSubmitted() => Process(
        ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved, ProcessState.NetzanmeldungSubmitted);

    private AnmeldeProcess ZaehlerwechselCompleted() => Process(
        ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved, ProcessState.NetzanmeldungSubmitted,
        ProcessState.EinspeisezusageReceived, ProcessState.InbetriebnahmeReady, ProcessState.InbetriebnahmeSubmitted,
        ProcessState.ZaehlerwechselRequested, ProcessState.ZaehlerwechselCompleted);

    // Rückmeldung des Netzbetreibers

    [Fact]
    public async Task Response_step_waits_without_facts()
    {
        var process = NetzanmeldungSubmitted();

        var result = await new AwaitNetzbetreiberResponseStep(_h.Time).ExecuteAsync(process, default);

        Assert.IsType<StepResult.WaitResult>(result);
        Assert.Equal(ProcessState.NetzanmeldungSubmitted, process.State);
    }

    [Fact]
    public async Task Einspeisezusage_moves_on()
    {
        var process = NetzanmeldungSubmitted();
        process.RecordEinspeisezusage(new DateOnly(2026, 11, 20), null, TestData.Now);

        var result = await new AwaitNetzbetreiberResponseStep(_h.Time).ExecuteAsync(process, default);

        Assert.Same(StepResult.Continue, result);
        Assert.Equal(ProcessState.EinspeisezusageReceived, process.State);
    }

    [Fact]
    public async Task Rueckfrage_opens_task_with_revision_and_timestamp_in_key()
    {
        var process = NetzanmeldungSubmitted();
        process.RecordRueckfrage(TestData.Now);

        var result = await new AwaitNetzbetreiberResponseStep(_h.Time).ExecuteAsync(process, default);

        var action = Assert.IsType<StepResult.HumanActionResult>(result);
        Assert.Equal($"rueckfrage:{process.Id:N}:r0:20261103120000", action.TaskKey);
        Assert.Equal(ProcessState.RueckfrageOpen, process.State);
        Assert.DoesNotContain("Mustermann", action.Body);
    }

    [Fact]
    public async Task Open_rueckfrage_without_resolution_waits()
    {
        var process = NetzanmeldungSubmitted();
        process.RecordRueckfrage(TestData.Now);
        process.TransitionTo(ProcessState.RueckfrageOpen, TestData.Now);

        var result = await new AwaitNetzbetreiberResponseStep(_h.Time).ExecuteAsync(process, default);

        Assert.IsType<StepResult.WaitResult>(result);
    }

    [Fact]
    public async Task Rueckfrage_answered_directly_returns_to_waiting_for_zusage()
    {
        var process = NetzanmeldungSubmitted();
        process.RecordRueckfrage(TestData.Now);
        process.TransitionTo(ProcessState.RueckfrageOpen, TestData.Now);
        process.ResolveRueckfrage(RueckfrageResolution.AnsweredDirectly, TestData.Now);

        var result = await new AwaitNetzbetreiberResponseStep(_h.Time).ExecuteAsync(process, default);

        Assert.Same(StepResult.Continue, result);
        Assert.Equal(ProcessState.NetzanmeldungSubmitted, process.State);
        Assert.Null(process.RueckfrageOffenSeit);
        Assert.Equal(0, process.Revision);
    }

    [Fact]
    public async Task Rueckfrage_resubmit_starts_new_revision()
    {
        var process = NetzanmeldungSubmitted();
        process.RecordRueckfrage(TestData.Now);
        process.TransitionTo(ProcessState.RueckfrageOpen, TestData.Now);
        process.ResolveRueckfrage(RueckfrageResolution.Resubmit, TestData.Now);

        var result = await new AwaitNetzbetreiberResponseStep(_h.Time).ExecuteAsync(process, default);

        Assert.Same(StepResult.Continue, result);
        Assert.Equal(ProcessState.Created, process.State);
        Assert.Equal(1, process.Revision);
        Assert.Null(process.DataCard);
    }

    [Fact]
    public async Task Rueckfrage_resolved_before_run_needs_no_task()
    {
        var process = NetzanmeldungSubmitted();
        process.RecordRueckfrage(TestData.Now);
        process.ResolveRueckfrage(RueckfrageResolution.AnsweredDirectly, TestData.Now);

        var result = await new AwaitNetzbetreiberResponseStep(_h.Time).ExecuteAsync(process, default);

        Assert.Same(StepResult.Continue, result);
        Assert.Equal(ProcessState.RueckfrageOpen, process.State);
    }

    [Fact]
    public async Task Zusage_wins_over_open_rueckfrage()
    {
        var process = NetzanmeldungSubmitted();
        process.RecordRueckfrage(TestData.Now);
        process.TransitionTo(ProcessState.RueckfrageOpen, TestData.Now);
        process.ResolveRueckfrage(RueckfrageResolution.Resubmit, TestData.Now);
        process.RecordEinspeisezusage(new DateOnly(2026, 11, 20), null, TestData.Now);

        await new AwaitNetzbetreiberResponseStep(_h.Time).ExecuteAsync(process, default);

        Assert.Equal(ProcessState.EinspeisezusageReceived, process.State);
        Assert.Null(process.RueckfrageOffenSeit);
        Assert.Equal(0, process.Revision);
    }

    // Warten auf Inbetriebnahme, Zählerwechsel, Abschluss

    [Fact]
    public async Task Inbetriebnahme_step_waits_for_date()
    {
        var process = Process(ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved,
            ProcessState.NetzanmeldungSubmitted, ProcessState.EinspeisezusageReceived);
        var step = new AwaitInbetriebnahmeStep(_h.Time);

        Assert.IsType<StepResult.WaitResult>(await step.ExecuteAsync(process, default));

        // Vorab gemeldetes Datum: kein E.8 vor der Inbetriebnahme.
        process.RecordInbetriebnahme(new DateOnly(2027, 1, 20), TestData.Now);
        Assert.IsType<StepResult.WaitResult>(await step.ExecuteAsync(process, default));
        Assert.Equal(ProcessState.EinspeisezusageReceived, process.State);

        // 19.01. 23:30 UTC ist in Berlin schon der 20.01.
        _h.Time.Now = new DateTimeOffset(2027, 1, 19, 22, 59, 0, TimeSpan.Zero);
        Assert.IsType<StepResult.WaitResult>(await step.ExecuteAsync(process, default));
        _h.Time.Now = new DateTimeOffset(2027, 1, 19, 23, 30, 0, TimeSpan.Zero);
        Assert.Same(StepResult.Continue, await step.ExecuteAsync(process, default));
        Assert.Equal(ProcessState.InbetriebnahmeReady, process.State);
    }

    [Fact]
    public async Task Zaehlerwechsel_step_waits_for_date()
    {
        var process = Process(ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved,
            ProcessState.NetzanmeldungSubmitted, ProcessState.EinspeisezusageReceived, ProcessState.InbetriebnahmeReady,
            ProcessState.InbetriebnahmeSubmitted, ProcessState.ZaehlerwechselRequested);
        var step = new AwaitZaehlerwechselStep(_h.Time);

        Assert.IsType<StepResult.WaitResult>(await step.ExecuteAsync(process, default));

        process.RecordZaehlerwechsel(new DateOnly(2027, 2, 1), TestData.Now);
        Assert.Same(StepResult.Continue, await step.ExecuteAsync(process, default));
        Assert.Equal(ProcessState.ZaehlerwechselCompleted, process.State);
    }

    [Fact]
    public async Task Complete_step_finishes_registered_process()
    {
        var process = ZaehlerwechselCompleted();
        process.TransitionTo(ProcessState.MastrRegistered, TestData.Now);

        await new CompleteStep(_h.Time).ExecuteAsync(process, default);

        Assert.Equal(ProcessState.Completed, process.State);
    }

    // Einreichung beim Netzbetreiber

    [Fact]
    public async Task Netzbetreiber_stage_not_needed_is_skipped()
    {
        _h.Adapter.Einreichungen = new HashSet<SubmissionKind> { SubmissionKind.Netzanmeldung, SubmissionKind.Inbetriebnahme };
        var process = Process(ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved,
            ProcessState.NetzanmeldungSubmitted, ProcessState.EinspeisezusageReceived, ProcessState.InbetriebnahmeReady,
            ProcessState.InbetriebnahmeSubmitted);

        var result = await _h.SubmissionStep(SubmissionStages.Zaehlerwechsel).ExecuteAsync(process, default);

        Assert.Same(StepResult.Continue, result);
        Assert.Equal(ProcessState.ZaehlerwechselRequested, process.State);
        Assert.Empty(_h.Adapter.FakeChannel.Sent);
    }

    [Fact]
    public async Task Netzbetreiber_validation_problem_creates_task_and_stays()
    {
        _h.Adapter.Problems.Add("Modulleistung fehlt");
        var process = Process(ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved);

        var result = await _h.SubmissionStep(SubmissionStages.Netzanmeldung).ExecuteAsync(process, default);

        var action = Assert.IsType<StepResult.HumanActionResult>(result);
        Assert.Equal($"netzanmeldung-invalid:{process.Id:N}:r0", action.TaskKey);
        Assert.Equal(ProcessState.NetzbetreiberResolved, process.State);
    }

    [Fact]
    public async Task Netzanmeldung_live_records_reference_of_netzbetreiber()
    {
        var process = Process(ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved);

        await _h.SubmissionStep(SubmissionStages.Netzanmeldung).ExecuteAsync(process, default);

        Assert.Equal(ProcessState.NetzanmeldungSubmitted, process.State);
        Assert.Equal("NB-4711", process.NetzbetreiberReferenz);
    }

    [Fact]
    public async Task Uncertain_submission_blocks_with_task()
    {
        var process = Process(ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved);
        _h.Ledger.Put(new SubmissionKey(process.Id, SubmissionKind.Netzanmeldung), SubmissionStatus.Sending);

        var result = await _h.SubmissionStep(SubmissionStages.Netzanmeldung).ExecuteAsync(process, default);

        var action = Assert.IsType<StepResult.HumanActionResult>(result);
        Assert.StartsWith("submission-uncertain:", action.TaskKey);
        Assert.Equal(ProcessState.Blocked, process.State);
        Assert.Equal(ProcessState.NetzbetreiberResolved, process.BlockedFrom);
        Assert.Empty(_h.Adapter.FakeChannel.Sent);
    }

    [Fact]
    public async Task Rerun_of_prepared_stage_in_dry_run_is_harmless()
    {
        _h.Submission.Mode = SubmissionMode.DryRun;
        var process = Process(ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved);
        var step = _h.SubmissionStep(SubmissionStages.Netzanmeldung);
        await step.ExecuteAsync(process, default);
        Assert.Equal((ProcessState.NetzanmeldungPrepared, 1), (process.State, _h.Archive.Stored));

        // Watchdog oder Ereignis plant den vorbereiteten Vorgang erneut ein.
        var result = await step.ExecuteAsync(process, default);

        Assert.IsType<StepResult.WaitResult>(result);
        Assert.Equal(ProcessState.NetzanmeldungPrepared, process.State);
        Assert.Equal(1, _h.Archive.Stored);
        Assert.Equal(SubmissionStatus.DryRun, Assert.Single(_h.Ledger.Records.Values).Status);
        Assert.Empty(_h.Adapter.FakeChannel.Sent);
    }

    [Fact]
    public async Task Prepared_stage_continues_after_switch_to_live()
    {
        _h.Submission.Mode = SubmissionMode.DryRun;
        var process = Process(ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved);
        var step = _h.SubmissionStep(SubmissionStages.Netzanmeldung);
        await step.ExecuteAsync(process, default);

        _h.Submission.Mode = SubmissionMode.Live;
        Assert.Same(StepResult.Continue, await step.ExecuteAsync(process, default));

        Assert.Equal(ProcessState.NetzanmeldungSubmitted, process.State);
        Assert.Single(_h.Adapter.FakeChannel.Sent);
    }

    [Fact]
    public async Task Failed_entry_in_dry_run_does_not_throw_and_stays_failed()
    {
        // Rückschritt von Live auf DryRun nach einem nachweislich gescheiterten Versand.
        _h.Submission.Mode = SubmissionMode.DryRun;
        var process = Process(ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved);
        _h.Ledger.Put(new SubmissionKey(process.Id, SubmissionKind.Netzanmeldung), SubmissionStatus.Failed);

        Assert.IsType<StepResult.WaitResult>(await _h.SubmissionStep(SubmissionStages.Netzanmeldung).ExecuteAsync(process, default));

        Assert.Equal(ProcessState.NetzanmeldungPrepared, process.State);
        Assert.Equal(SubmissionStatus.Failed, _h.Ledger.Records[$"{process.Id:N}:Netzanmeldung"].Status);
        Assert.Empty(_h.Adapter.FakeChannel.Sent);
    }

    [Fact]
    public async Task Missing_data_card_task_key_contains_the_revision()
    {
        var process = _h.Store.Add(new AnmeldeProcess(ProcessHarness.ProjectId, TestData.Now));
        _h.Planning.Problems.Add("Checkliste: Kaskadenschaltung fehlt");

        var result = await _h.Steps().OfType<AggregateDataCardStep>().Single().ExecuteAsync(process, default);

        Assert.Equal($"datacard-missing:{process.Id:N}:r0", Assert.IsType<StepResult.HumanActionResult>(result).TaskKey);
    }

    // MaStR

    [Fact]
    public async Task Mastr_live_registers_records_numbers_and_deletes_password()
    {
        var process = ZaehlerwechselCompleted();

        var result = await _h.MastrStep().ExecuteAsync(process, default);

        Assert.Same(StepResult.Continue, result);
        Assert.Equal(ProcessState.MastrRegistered, process.State);
        Assert.Equal(("SEE900000000001", "SSE900000000002"), (process.MastrNummerAnlage, process.MastrNummerSpeicher));
        Assert.Equal(1, _h.Registrar.Calls);
        Assert.Empty(_h.Credentials.Logins);
        var record = _h.Ledger.Records[new SubmissionKey(process.Id, SubmissionKind.Mastr).Value];
        Assert.Equal(("anlage=SEE900000000001;speicher=SSE900000000002", SubmissionStatus.Sent), (record.ExternalReference, record.Status));
    }

    [Fact]
    public async Task Mastr_without_login_asks_for_it_and_waits()
    {
        _h.Credentials.Logins.Clear();
        var process = ZaehlerwechselCompleted();

        var result = await _h.MastrStep().ExecuteAsync(process, default);

        var action = Assert.IsType<StepResult.HumanActionResult>(result);
        Assert.Equal($"mastr-login-missing:{process.Id:N}", action.TaskKey);
        Assert.Equal("MaStR-Login fehlt", action.Title);
        Assert.Equal(ProcessState.ZaehlerwechselCompleted, process.State);
        Assert.Equal(0, _h.Registrar.Calls);
        Assert.Empty(_h.Ledger.Records);
    }

    [Fact]
    public async Task Mastr_rerun_after_already_sent_restores_numbers_without_login_and_deletes_again()
    {
        // Erster Lauf hat registriert und das Passwort gelöscht, ist aber vor dem Speichern des Zustands abgebrochen.
        _h.Credentials.Logins.Clear();
        var process = ZaehlerwechselCompleted();
        _h.Ledger.Put(new SubmissionKey(process.Id, SubmissionKind.Mastr), SubmissionStatus.Sent,
            "anlage=SEE900000000001;speicher=SSE900000000002");

        var result = await _h.MastrStep().ExecuteAsync(process, default);

        Assert.Same(StepResult.Continue, result);
        Assert.Equal(ProcessState.MastrRegistered, process.State);
        Assert.Equal(("SEE900000000001", "SSE900000000002"), (process.MastrNummerAnlage, process.MastrNummerSpeicher));
        Assert.Equal(0, _h.Registrar.Calls);
        Assert.Equal(1, _h.Credentials.Deletes);
    }

    [Fact]
    public async Task Mastr_failure_after_registration_is_retried_without_second_registration()
    {
        var process = ZaehlerwechselCompleted();
        var credentials = new ThrowingOnceDeleteStore(_h.Credentials);
        var step = new MastrRegistrationStep(_h.Registrar, credentials, _h.Ledger, _h.Gate(),
            Microsoft.Extensions.Options.Options.Create(_h.Submission), _h.Time);

        await Assert.ThrowsAsync<HttpRequestException>(() => step.ExecuteAsync(process, default));
        Assert.Equal(ProcessState.ZaehlerwechselCompleted, process.State);

        await step.ExecuteAsync(process, default);

        Assert.Equal(ProcessState.MastrRegistered, process.State);
        Assert.Equal(1, _h.Registrar.Calls);
        Assert.Empty(_h.Credentials.Logins);
    }

    [Fact]
    public async Task Mastr_dry_run_needs_no_login_and_keeps_it()
    {
        _h.Submission.Mode = SubmissionMode.DryRun;
        var process = ZaehlerwechselCompleted();

        var result = await _h.MastrStep().ExecuteAsync(process, default);

        Assert.IsType<StepResult.WaitResult>(result);
        Assert.Equal(ProcessState.MastrPrepared, process.State);
        Assert.Equal(0, _h.Credentials.Reads);
        Assert.Equal(0, _h.Credentials.Deletes);
        Assert.Equal(0, _h.Registrar.Calls);
    }

    [Fact]
    public async Task Mastr_dry_run_can_continue_for_staging_demo()
    {
        _h.Submission.Mode = SubmissionMode.DryRun;
        _h.Submission.ContinueAfterDryRun = true;
        _h.Credentials.Logins.Clear();
        var process = ZaehlerwechselCompleted();

        await _h.MastrStep().ExecuteAsync(process, default);

        Assert.Equal(ProcessState.MastrRegistered, process.State);
        Assert.Null(process.MastrNummerAnlage);
        Assert.Equal(0, _h.Registrar.Calls);
    }

    [Fact]
    public async Task Mastr_review_waits_for_approval_of_its_own_stage()
    {
        _h.Submission.Mode = SubmissionMode.Review;
        var process = ZaehlerwechselCompleted();
        var step = _h.MastrStep();

        var action = Assert.IsType<StepResult.HumanActionResult>(await step.ExecuteAsync(process, default));
        Assert.Equal($"review:{process.Id:N}:Mastr:r0:20261103120000", action.TaskKey);
        Assert.Equal(ProcessState.MastrAwaitingReview, process.State);
        Assert.Contains("erst zurücksetzen", action.Body);
        // Weiterer Lauf ohne Freigabe: derselbe Schlüssel, der Sink legt keinen zweiten Task an.
        Assert.Equal(action.TaskKey,
            Assert.IsType<StepResult.HumanActionResult>(await step.ExecuteAsync(process, default)).TaskKey);

        Assert.False(process.ApproveReview(SubmissionKind.Netzanmeldung, TestData.Now));
        Assert.True(process.ApproveReview(SubmissionKind.Mastr, TestData.Now));
        await step.ExecuteAsync(process, default);

        Assert.Equal(ProcessState.MastrRegistered, process.State);
        Assert.Equal(1, _h.Registrar.Calls);
    }

    [Fact]
    public async Task Mastr_uncertain_blocks()
    {
        var process = ZaehlerwechselCompleted();
        _h.Ledger.Put(new SubmissionKey(process.Id, SubmissionKind.Mastr), SubmissionStatus.Sending);

        var result = await _h.MastrStep().ExecuteAsync(process, default);

        Assert.IsType<StepResult.HumanActionResult>(result);
        Assert.Equal(ProcessState.Blocked, process.State);
        Assert.Equal(0, _h.Registrar.Calls);
        Assert.Single(_h.Credentials.Logins);
    }

    [Fact]
    public async Task Mastr_crash_during_registration_leaves_uncertain_state()
    {
        var process = ZaehlerwechselCompleted();
        _h.Registrar.Failure = new TimeoutException("Portal antwortet nicht");

        await Assert.ThrowsAsync<TimeoutException>(() => _h.MastrStep().ExecuteAsync(process, default));
        _h.Registrar.Failure = null;
        await _h.MastrStep().ExecuteAsync(process, default);

        Assert.Equal(ProcessState.Blocked, process.State);
        Assert.Equal(0, _h.Registrar.Calls);
    }

    private sealed class ThrowingOnceDeleteStore(FakeCredentialStore inner) : PvAutopilot.Core.Mastr.IMastrCredentialStore
    {
        private bool _thrown;

        public Task<PvAutopilot.Core.Mastr.MastrCredentials?> GetAsync(string crmProjectId, CancellationToken ct) =>
            inner.GetAsync(crmProjectId, ct);

        public Task DeleteAsync(string crmProjectId, CancellationToken ct)
        {
            if (_thrown)
                return inner.DeleteAsync(crmProjectId, ct);
            _thrown = true;
            throw new HttpRequestException("HubSpot nicht erreichbar");
        }
    }
}
