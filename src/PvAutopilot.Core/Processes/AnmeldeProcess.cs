using PvAutopilot.Core.DataCard;

namespace PvAutopilot.Core.Processes;

/// <summary>
/// Ein Anmeldevorgang für eine PV-Anlage. Zustandsänderungen nur über die Methoden,
/// damit jeder Übergang geprüft und protokolliert wird.
/// </summary>
public sealed class AnmeldeProcess
{
    private readonly List<ProcessTransition> _transitions = [];

    private AnmeldeProcess() { } // EF Core

    public AnmeldeProcess(string crmProjectId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(crmProjectId);
        CrmProjectId = crmProjectId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>ID des Projekts im CRM (HubSpot), das den Vorgang ausgelöst hat.</summary>
    public string CrmProjectId { get; private set; } = string.Empty;

    public ProcessState State { get; private set; } = ProcessState.Created;
    public ProcessState? BlockedFrom { get; private set; }

    /// <summary>Technischer Grund der Blockade. Darf keine personenbezogenen Daten enthalten.</summary>
    public string? BlockReason { get; private set; }

    public string? NetzbetreiberCode { get; private set; }
    public NabDataCard? DataCard { get; private set; }
    public DateTimeOffset? ReviewApprovedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<ProcessTransition> Transitions => _transitions;

    public void TransitionTo(ProcessState target, DateTimeOffset now, string? note = null)
    {
        if (target == ProcessState.Blocked)
            throw new InvalidOperationException($"Für {ProcessState.Blocked} {nameof(Block)} verwenden.");
        if (!ProcessTransitions.IsAllowed(State, target))
            throw new InvalidProcessTransitionException(State, target);

        Record(target, now, note);
    }

    public void Block(string reason, DateTimeOffset now)
    {
        if (State == ProcessState.Blocked)
            return;
        if (!ProcessTransitions.IsAllowed(State, ProcessState.Blocked))
            throw new InvalidProcessTransitionException(State, ProcessState.Blocked);

        BlockedFrom = State;
        BlockReason = reason;
        Record(ProcessState.Blocked, now, reason);
    }

    /// <summary>Setzt einen blockierten Vorgang auf den Zustand vor der Blockade zurück.</summary>
    public void Resume(DateTimeOffset now)
    {
        if (State != ProcessState.Blocked || BlockedFrom is not { } target)
            throw new InvalidOperationException("Nur blockierte Vorgänge können fortgesetzt werden.");

        BlockedFrom = null;
        BlockReason = null;
        Record(target, now, "fortgesetzt");
    }

    public void AttachDataCard(NabDataCard card, DateTimeOffset now)
    {
        DataCard = card;
        UpdatedAt = now;
    }

    public void AssignNetzbetreiber(string code, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        NetzbetreiberCode = code;
        UpdatedAt = now;
    }

    public void ApproveReview(DateTimeOffset now)
    {
        if (State != ProcessState.AwaitingReview)
            throw new InvalidOperationException($"Freigabe nur im Zustand {ProcessState.AwaitingReview} möglich.");
        ReviewApprovedAt = now;
        UpdatedAt = now;
    }

    private void Record(ProcessState target, DateTimeOffset now, string? note)
    {
        _transitions.Add(new ProcessTransition(State, target, now, note));
        State = target;
        UpdatedAt = now;
    }
}

public sealed record ProcessTransition(ProcessState From, ProcessState To, DateTimeOffset At, string? Note);
