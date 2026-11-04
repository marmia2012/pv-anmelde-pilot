using PvAutopilot.Core.Processes;

namespace PvAutopilot.Tests.Core;

public class AnmeldeProcessTests
{
    [Fact]
    public void TransitionTo_records_allowed_transition()
    {
        var process = new AnmeldeProcess("crm-1", TestData.Now);

        process.TransitionTo(ProcessState.DataCardAggregated, TestData.Now);

        Assert.Equal(ProcessState.DataCardAggregated, process.State);
        var transition = Assert.Single(process.Transitions);
        Assert.Equal((ProcessState.Created, ProcessState.DataCardAggregated), (transition.From, transition.To));
    }

    [Fact]
    public void TransitionTo_rejects_skipping_states()
    {
        var process = new AnmeldeProcess("crm-1", TestData.Now);

        Assert.Throws<InvalidProcessTransitionException>(() =>
            process.TransitionTo(ProcessState.ApplicationSubmitted, TestData.Now));
        Assert.Equal(ProcessState.Created, process.State);
    }

    [Fact]
    public void Block_and_Resume_return_to_previous_state()
    {
        var process = new AnmeldeProcess("crm-1", TestData.Now);
        process.TransitionTo(ProcessState.DataCardAggregated, TestData.Now);

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
            .Where(s => s is not (ProcessState.Completed or ProcessState.Blocked))
            .Where(s => !Enum.GetValues<ProcessState>().Any(t => t != ProcessState.Blocked && ProcessTransitions.IsAllowed(s, t)));

        Assert.Empty(withoutSuccessor);
    }

    [Fact]
    public void ApproveReview_only_while_awaiting_review()
    {
        var process = new AnmeldeProcess("crm-1", TestData.Now);

        Assert.Throws<InvalidOperationException>(() => process.ApproveReview(TestData.Now));
    }
}
