namespace PvAutopilot.Core.Processes;

/// <summary>Erlaubte Zustandsübergänge der State-Machine.</summary>
public static class ProcessTransitions
{
    private static readonly Dictionary<ProcessState, ProcessState[]> Allowed = new()
    {
        [ProcessState.Created] = [ProcessState.DataCardAggregated],
        [ProcessState.DataCardAggregated] = [ProcessState.NetzbetreiberResolved],
        [ProcessState.NetzbetreiberResolved] = [ProcessState.ApplicationPrepared],
        [ProcessState.ApplicationPrepared] = [ProcessState.AwaitingReview, ProcessState.ApplicationSubmitted],
        [ProcessState.AwaitingReview] = [ProcessState.ApplicationSubmitted, ProcessState.ApplicationPrepared],
        [ProcessState.ApplicationSubmitted] = [ProcessState.RueckfrageOpen, ProcessState.EinspeisezusageReceived],
        [ProcessState.RueckfrageOpen] = [ProcessState.ApplicationSubmitted, ProcessState.ApplicationPrepared],
        [ProcessState.EinspeisezusageReceived] = [ProcessState.InbetriebnahmeSubmitted],
        [ProcessState.InbetriebnahmeSubmitted] = [ProcessState.ZaehlerwechselRequested],
        [ProcessState.ZaehlerwechselRequested] = [ProcessState.MastrRegistered],
        [ProcessState.MastrRegistered] = [ProcessState.Completed],
    };

    public static bool IsAllowed(ProcessState from, ProcessState to) =>
        to == ProcessState.Blocked
            ? from is not (ProcessState.Blocked or ProcessState.Completed)
            : Allowed.TryGetValue(from, out var targets) && targets.Contains(to);
}

public sealed class InvalidProcessTransitionException(ProcessState from, ProcessState to)
    : InvalidOperationException($"Übergang {from} → {to} ist nicht erlaubt.")
{
    public ProcessState From { get; } = from;
    public ProcessState To { get; } = to;
}
