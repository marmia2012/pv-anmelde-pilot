namespace PvAutopilot.Core.Processes;

/// <summary>Erlaubte Zustandsübergänge der State-Machine.</summary>
public static class ProcessTransitions
{
    private static readonly Dictionary<ProcessState, HashSet<ProcessState>> Allowed = Build();

    private static Dictionary<ProcessState, HashSet<ProcessState>> Build()
    {
        var edges = new Dictionary<ProcessState, HashSet<ProcessState>>();
        void Add(ProcessState from, params ProcessState[] to)
        {
            if (!edges.TryGetValue(from, out var set))
                edges[from] = set = [];
            set.UnionWith(to);
        }

        Add(ProcessState.Created, ProcessState.DataCardAggregated);
        Add(ProcessState.DataCardAggregated, ProcessState.NetzbetreiberResolved);

        foreach (var stage in SubmissionStages.All)
        {
            Add(stage.Ready, stage.Prepared, stage.Submitted);
            Add(stage.Prepared, stage.AwaitingReview, stage.Submitted);
            Add(stage.AwaitingReview, stage.Submitted, stage.Prepared);
        }

        Add(ProcessState.NetzanmeldungSubmitted, ProcessState.RueckfrageOpen, ProcessState.EinspeisezusageReceived);
        // Neueinreichung nach Rückfrage beginnt mit frischer Datenkarte.
        Add(ProcessState.RueckfrageOpen,
            ProcessState.NetzanmeldungSubmitted, ProcessState.EinspeisezusageReceived, ProcessState.Created);
        Add(ProcessState.EinspeisezusageReceived, ProcessState.InbetriebnahmeReady);
        Add(ProcessState.ZaehlerwechselRequested, ProcessState.ZaehlerwechselCompleted);
        Add(ProcessState.MastrRegistered, ProcessState.Completed);

        return edges;
    }

    public static bool IsAllowed(ProcessState from, ProcessState to) => to switch
    {
        _ when from.IsTerminal() => false,
        ProcessState.Cancelled => true,
        ProcessState.Blocked => from != ProcessState.Blocked,
        _ => Allowed.TryGetValue(from, out var targets) && targets.Contains(to),
    };
}

public sealed class InvalidProcessTransitionException(ProcessState from, ProcessState to)
    : InvalidOperationException($"Übergang {from} → {to} ist nicht erlaubt.")
{
    public ProcessState From { get; } = from;
    public ProcessState To { get; } = to;
}
