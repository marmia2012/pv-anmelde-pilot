using PvAutopilot.Core.Mastr;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Tests.Core;

public class AnmeldeProcessTests
{
    private static readonly DateTimeOffset Later = TestData.Now.AddHours(1);

    private static AnmeldeProcess InState(params ProcessState[] path)
    {
        var process = new AnmeldeProcess("crm-1", TestData.Now);
        foreach (var state in path)
            process.TransitionTo(state, TestData.Now);
        return process;
    }

    private static AnmeldeProcess Submitted() => InState(
        ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved, ProcessState.NetzanmeldungSubmitted);

    [Fact]
    public void TransitionTo_records_allowed_transition()
    {
        var process = new AnmeldeProcess("crm-1", TestData.Now);

        process.TransitionTo(ProcessState.DataCardAggregated, Later);

        Assert.Equal(ProcessState.DataCardAggregated, process.State);
        Assert.Equal(Later, process.StateChangedAt);
        var transition = Assert.Single(process.Transitions);
        Assert.Equal((ProcessState.Created, ProcessState.DataCardAggregated), (transition.From, transition.To));
    }

    [Fact]
    public void TransitionTo_rejects_skipping_states()
    {
        var process = new AnmeldeProcess("crm-1", TestData.Now);

        Assert.Throws<InvalidProcessTransitionException>(() =>
            process.TransitionTo(ProcessState.NetzanmeldungSubmitted, TestData.Now));
        Assert.Equal(ProcessState.Created, process.State);
    }

    [Fact]
    public void Block_and_Resume_return_to_previous_state()
    {
        var process = InState(ProcessState.DataCardAggregated);

        process.Block("Timeout", TestData.Now);
        Assert.Equal(ProcessState.Blocked, process.State);
        Assert.Equal(ProcessState.DataCardAggregated, process.BlockedFrom);

        process.Resume(TestData.Now);
        Assert.Equal(ProcessState.DataCardAggregated, process.State);
        Assert.Null(process.BlockedFrom);
        Assert.Null(process.BlockReason);
    }

    [Fact]
    public void Completed_process_cannot_be_blocked()
    {
        Assert.False(ProcessTransitions.IsAllowed(ProcessState.Completed, ProcessState.Blocked));
    }

    [Fact]
    public void Every_non_terminal_state_has_a_way_forward()
    {
        var withoutSuccessor = Enum.GetValues<ProcessState>()
            .Where(s => !s.IsTerminal())
            .Where(s => !Enum.GetValues<ProcessState>().Any(t => t is not (ProcessState.Blocked or ProcessState.Cancelled)
                && ProcessTransitions.IsAllowed(s, t)))
            .Where(s => s != ProcessState.Blocked); // Blocked geht über Resume zurück

        Assert.Empty(withoutSuccessor);
    }

    [Fact]
    public void Every_non_terminal_state_can_be_cancelled_and_terminal_states_cannot_change()
    {
        foreach (var state in Enum.GetValues<ProcessState>())
        {
            Assert.Equal(!state.IsTerminal(), ProcessTransitions.IsAllowed(state, ProcessState.Cancelled));
            if (state.IsTerminal())
                Assert.DoesNotContain(Enum.GetValues<ProcessState>(), t => ProcessTransitions.IsAllowed(state, t));
        }
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public void Submission_stage_transitions(SubmissionKind kind)
    {
        var stage = SubmissionStages.For(kind);

        Assert.True(ProcessTransitions.IsAllowed(stage.Ready, stage.Prepared));
        Assert.True(ProcessTransitions.IsAllowed(stage.Ready, stage.Submitted)); // Stufe beim Netzbetreiber nicht nötig
        Assert.True(ProcessTransitions.IsAllowed(stage.Prepared, stage.AwaitingReview));
        Assert.True(ProcessTransitions.IsAllowed(stage.Prepared, stage.Submitted)); // DryRun mit ContinueAfterDryRun
        Assert.True(ProcessTransitions.IsAllowed(stage.AwaitingReview, stage.Submitted));
        Assert.False(ProcessTransitions.IsAllowed(stage.Submitted, stage.Prepared));
        Assert.Equal(stage, SubmissionStages.AwaitingReviewIn(stage.AwaitingReview));
    }

    public static TheoryData<SubmissionKind> Stages() => new(Enum.GetValues<SubmissionKind>());

    [Theory]
    [InlineData(ProcessState.NetzanmeldungSubmitted, ProcessState.RueckfrageOpen, true)]
    [InlineData(ProcessState.NetzanmeldungSubmitted, ProcessState.EinspeisezusageReceived, true)]
    [InlineData(ProcessState.RueckfrageOpen, ProcessState.NetzanmeldungSubmitted, true)]
    [InlineData(ProcessState.RueckfrageOpen, ProcessState.EinspeisezusageReceived, true)]
    [InlineData(ProcessState.RueckfrageOpen, ProcessState.Created, true)]
    [InlineData(ProcessState.EinspeisezusageReceived, ProcessState.InbetriebnahmeReady, true)]
    [InlineData(ProcessState.ZaehlerwechselRequested, ProcessState.ZaehlerwechselCompleted, true)]
    [InlineData(ProcessState.MastrRegistered, ProcessState.Completed, true)]
    [InlineData(ProcessState.NetzanmeldungSubmitted, ProcessState.Created, false)]
    [InlineData(ProcessState.EinspeisezusageReceived, ProcessState.InbetriebnahmeSubmitted, false)]
    [InlineData(ProcessState.Blocked, ProcessState.Cancelled, true)]
    [InlineData(ProcessState.Blocked, ProcessState.Blocked, false)]
    public void Waiting_state_transitions(ProcessState from, ProcessState to, bool allowed)
    {
        Assert.Equal(allowed, ProcessTransitions.IsAllowed(from, to));
    }

    [Fact]
    public void ApproveReview_only_for_the_stage_awaiting_review_and_expires_with_the_state()
    {
        var process = InState(ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved,
            ProcessState.NetzanmeldungPrepared);
        Assert.False(process.ApproveReview(SubmissionKind.Netzanmeldung, TestData.Now));

        process.TransitionTo(ProcessState.NetzanmeldungAwaitingReview, TestData.Now);
        Assert.False(process.ApproveReview(SubmissionKind.Inbetriebnahme, TestData.Now));
        Assert.True(process.ApproveReview(SubmissionKind.Netzanmeldung, TestData.Now));
        Assert.True(process.IsReviewApproved(SubmissionKind.Netzanmeldung));

        process.TransitionTo(ProcessState.NetzanmeldungSubmitted, TestData.Now);
        Assert.False(process.IsReviewApproved(SubmissionKind.Netzanmeldung));
    }

    [Fact]
    public void Duplicate_rueckfrage_while_open_keeps_first_timestamp_and_recorded_resolution()
    {
        var process = Submitted();

        Assert.False(process.ResolveRueckfrage(RueckfrageResolution.AnsweredDirectly, TestData.Now));

        Assert.True(process.RecordRueckfrage(TestData.Now));
        Assert.True(process.ResolveRueckfrage(RueckfrageResolution.AnsweredDirectly, Later));
        // Doppelt zugestellt, bevor der Lauf die Erledigung umgesetzt hat: die Erledigung darf nicht verloren gehen.
        Assert.True(process.RecordRueckfrage(Later, occurredAt: TestData.Now));

        Assert.Equal(TestData.Now, process.RueckfrageOffenSeit);
        Assert.Equal(RueckfrageResolution.AnsweredDirectly, process.RueckfrageErledigung);

        process.CloseRueckfrage(Later);
        Assert.Null(process.RueckfrageOffenSeit);
        Assert.Equal(Later, process.RueckfrageGeschlossenAm);
    }

    [Fact]
    public void Rueckfrage_after_close_only_counts_if_it_occurred_after_the_close()
    {
        var process = Submitted();
        process.RecordRueckfrage(TestData.Now);
        process.CloseRueckfrage(Later);

        // Verspätet oder erneut zugestellt: Ereignis von vor dem Schließen.
        Assert.False(process.RecordRueckfrage(Later.AddMinutes(5), occurredAt: TestData.Now.AddMinutes(1)));
        Assert.False(process.RecordRueckfrage(Later.AddMinutes(5), occurredAt: Later));
        Assert.Null(process.RueckfrageOffenSeit);

        // Neue Rückfrage nach dem Schließen.
        Assert.True(process.RecordRueckfrage(Later.AddMinutes(10), occurredAt: Later.AddMinutes(9)));
        Assert.Equal(Later.AddMinutes(10), process.RueckfrageOffenSeit);
        Assert.Null(process.RueckfrageErledigung);
    }

    [Fact]
    public void Rueckfrage_without_occurred_at_is_always_accepted()
    {
        var process = Submitted();
        process.RecordRueckfrage(TestData.Now);
        process.CloseRueckfrage(Later);

        Assert.True(process.RecordRueckfrage(Later));
        Assert.NotNull(process.RueckfrageOffenSeit);
    }

    [Fact]
    public void ApproveReview_ignores_events_from_before_the_review_state()
    {
        var process = InState(ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved,
            ProcessState.NetzanmeldungPrepared);
        process.TransitionTo(ProcessState.NetzanmeldungAwaitingReview, Later);

        // Altes Freigabe-Ereignis (z. B. der vorigen Revision) wird erneut zugestellt.
        Assert.False(process.ApproveReview(SubmissionKind.Netzanmeldung, Later.AddMinutes(1), occurredAt: TestData.Now));
        Assert.False(process.IsReviewApproved(SubmissionKind.Netzanmeldung));

        Assert.True(process.ApproveReview(SubmissionKind.Netzanmeldung, Later.AddMinutes(1), occurredAt: Later));
        Assert.True(process.IsReviewApproved(SubmissionKind.Netzanmeldung));
    }

    [Fact]
    public void StartNewRevision_only_from_RueckfrageOpen_and_resets_data_card()
    {
        var process = Submitted();
        process.AttachDataCard(TestData.Card(), TestData.Now);
        Assert.Throws<InvalidOperationException>(() => process.StartNewRevision(TestData.Now));

        process.RecordRueckfrage(TestData.Now);
        process.TransitionTo(ProcessState.RueckfrageOpen, TestData.Now);
        process.StartNewRevision(Later);

        Assert.Equal(ProcessState.Created, process.State);
        Assert.Equal(1, process.Revision);
        Assert.Null(process.DataCard);
        Assert.Null(process.RueckfrageOffenSeit);
    }

    [Fact]
    public void Facts_are_idempotent_and_later_events_correct_earlier_ones()
    {
        var process = new AnmeldeProcess("crm-1", TestData.Now);

        process.RecordEinspeisezusage(new DateOnly(2026, 11, 10), "NB-1", TestData.Now);
        process.RecordEinspeisezusage(new DateOnly(2026, 11, 11), null, Later);
        process.RecordMastrRegistration("SEE1", "SSE1", TestData.Now);
        process.RecordMastrRegistration("SEE1", null, Later);
        process.RequestCancellation(TestData.Now);
        process.RequestCancellation(Later);

        Assert.Equal(new DateOnly(2026, 11, 11), process.EinspeisezusageDatum);
        Assert.Equal("NB-1", process.NetzbetreiberReferenz);
        Assert.Equal(("SEE1", "SSE1"), (process.MastrNummerAnlage, process.MastrNummerSpeicher));
        Assert.Equal(TestData.Now, process.CancellationRequestedAt);
        Assert.Equal(ProcessState.Created, process.State);
    }

    [Theory]
    [InlineData("SEE900000000001", "SSE900000000002", "anlage=SEE900000000001;speicher=SSE900000000002")]
    [InlineData("SEE900000000001", null, "anlage=SEE900000000001")]
    [InlineData(null, null, "")]
    public void Mastr_reference_round_trip(string? anlage, string? speicher, string reference)
    {
        var registration = new MastrRegistration(anlage, speicher);

        Assert.Equal(reference, registration.ToReference());
        Assert.Equal(registration, MastrRegistration.FromReference(reference));
    }

    [Fact]
    public void Mastr_reference_tolerates_garbage()
    {
        Assert.Equal(new MastrRegistration(null, null), MastrRegistration.FromReference(null));
        Assert.Equal(new MastrRegistration("SEE1", null), MastrRegistration.FromReference(" Anlage = SEE1 ;foo;speicher="));
    }
}
