using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core.Processes;

/// <summary>
/// Ein Anmeldevorgang für eine PV-Anlage. Zustandsänderungen nur über die Methoden,
/// damit jeder Übergang geprüft und protokolliert wird.
/// </summary>
/// <remarks>
/// Ereignisse von außen halten nur Fakten fest (<c>Record…</c>-Methoden); welcher Zustand daraus folgt,
/// entscheiden die Schritte. So ist die Reihenfolge der Ereignisse egal (docs/konzept/prozesssteuerung.md).
/// </remarks>
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
        StateChangedAt = now;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>ID des Projekts im CRM (HubSpot), das den Vorgang ausgelöst hat.</summary>
    public string CrmProjectId { get; private set; } = string.Empty;

    public ProcessState State { get; private set; } = ProcessState.Created;
    public DateTimeOffset StateChangedAt { get; private set; }
    public ProcessState? BlockedFrom { get; private set; }

    /// <summary>Technischer Grund der Blockade. Darf keine personenbezogenen Daten enthalten.</summary>
    public string? BlockReason { get; private set; }

    /// <summary>Zählt Neueinreichungen der Netzanmeldung nach einer Rückfrage; Teil des Idempotenz-Schlüssels.</summary>
    public int Revision { get; private set; }

    public string? NetzbetreiberCode { get; private set; }
    public NabDataCard? DataCard { get; private set; }

    /// <summary>Stufe, die im Modus Review freigegeben wurde, solange sie auf Freigabe wartet.</summary>
    public SubmissionKind? ApprovedSubmission { get; private set; }
    public DateTimeOffset? ReviewApprovedAt { get; private set; }

    // Fakten aus Ereignissen

    /// <summary>Vorgangs- bzw. Aktenzeichen des Netzbetreibers (keine personenbezogenen Daten).</summary>
    public string? NetzbetreiberReferenz { get; private set; }
    public DateOnly? EinspeisezusageDatum { get; private set; }
    public DateTimeOffset? RueckfrageOffenSeit { get; private set; }
    public RueckfrageResolution? RueckfrageErledigung { get; private set; }

    /// <summary>Wann zuletzt eine Rückfrage geschlossen wurde; ältere Rückfrage-Ereignisse gelten als überholt.</summary>
    public DateTimeOffset? RueckfrageGeschlossenAm { get; private set; }

    public DateOnly? Inbetriebnahmedatum { get; private set; }
    public DateOnly? ZaehlerwechselDatum { get; private set; }
    public string? MastrNummerAnlage { get; private set; }
    public string? MastrNummerSpeicher { get; private set; }
    public DateTimeOffset? CancellationRequestedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    /// <summary>
    /// Vollständige Historie aller Zustandswechsel, nur anhängend. Wird nie gekürzt oder umgeschrieben: der Schlüssel
    /// der HubSpot-Audit-Notizen enthält <c>Transitions.Count</c> (<c>HubSpotAuditSink.Key</c>); eine gekürzte Liste
    /// ergäbe alte Schlüssel erneut, und neue Notizen würden als Dublette verworfen.
    /// </summary>
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

    /// <summary>
    /// Freigabe einer Stufe. <c>false</c>, wenn der Vorgang gerade nicht auf diese Freigabe wartet oder das Ereignis
    /// (<paramref name="occurredAt"/>) älter ist als der Eintritt in den Freigabe-Zustand – sonst gäbe ein verspätet
    /// oder erneut zugestelltes Ereignis einer früheren Freigabe eine neue Revision bzw. eine fortgesetzte Stufe frei.
    /// </summary>
    public bool ApproveReview(SubmissionKind kind, DateTimeOffset now, DateTimeOffset? occurredAt = null)
    {
        if (SubmissionStages.AwaitingReviewIn(State)?.Kind != kind)
            return false;
        if (occurredAt < StateChangedAt)
            return false;
        ApprovedSubmission = kind;
        ReviewApprovedAt = now;
        UpdatedAt = now;
        return true;
    }

    public bool IsReviewApproved(SubmissionKind kind) => ApprovedSubmission == kind;

    // Fakten: idempotent, unabhängig vom Zustand. Ein späteres Ereignis überschreibt (Korrektur in HubSpot).

    public void RecordNetzbetreiberReferenz(string referenz, DateTimeOffset now)
    {
        NetzbetreiberReferenz = referenz;
        UpdatedAt = now;
    }

    public void RecordEinspeisezusage(DateOnly datum, string? netzbetreiberReferenz, DateTimeOffset now)
    {
        EinspeisezusageDatum = datum;
        NetzbetreiberReferenz = netzbetreiberReferenz ?? NetzbetreiberReferenz;
        UpdatedAt = now;
    }

    /// <summary>
    /// Neue Rückfrage. Ist schon eine offen, ändert sich nichts – auch eine bereits erfasste Erledigung bleibt.
    /// <c>false</c>, wenn das Ereignis (<paramref name="occurredAt"/>) nicht nach dem Schließen der letzten Rückfrage
    /// liegt: dann ist es ein verspätet oder erneut zugestelltes altes Ereignis.
    /// </summary>
    public bool RecordRueckfrage(DateTimeOffset now, DateTimeOffset? occurredAt = null)
    {
        if (RueckfrageOffenSeit is not null)
            return true;
        if (occurredAt <= RueckfrageGeschlossenAm)
            return false;

        RueckfrageOffenSeit = now;
        RueckfrageErledigung = null;
        UpdatedAt = now;
        return true;
    }

    /// <summary><c>false</c>, wenn keine Rückfrage offen ist.</summary>
    public bool ResolveRueckfrage(RueckfrageResolution resolution, DateTimeOffset now)
    {
        if (RueckfrageOffenSeit is null)
            return false;
        RueckfrageErledigung = resolution;
        UpdatedAt = now;
        return true;
    }

    public void RecordInbetriebnahme(DateOnly datum, DateTimeOffset now)
    {
        Inbetriebnahmedatum = datum;
        UpdatedAt = now;
    }

    public void RecordZaehlerwechsel(DateOnly datum, DateTimeOffset now)
    {
        ZaehlerwechselDatum = datum;
        UpdatedAt = now;
    }

    public void RecordMastrRegistration(string? anlage, string? speicher, DateTimeOffset now)
    {
        MastrNummerAnlage = anlage ?? MastrNummerAnlage;
        MastrNummerSpeicher = speicher ?? MastrNummerSpeicher;
        UpdatedAt = now;
    }

    public void RequestCancellation(DateTimeOffset now)
    {
        CancellationRequestedAt ??= now;
        UpdatedAt = now;
    }

    // Nur für Schritte

    /// <summary>Rückfrage ist erledigt (direkt beantwortet oder durch die Zusage überholt).</summary>
    public void CloseRueckfrage(DateTimeOffset now)
    {
        RueckfrageOffenSeit = null;
        RueckfrageErledigung = null;
        RueckfrageGeschlossenAm = now;
        UpdatedAt = now;
    }

    /// <summary>Neueinreichung nach Rückfrage: neue Revision, Datenkarte wird neu aggregiert.</summary>
    public void StartNewRevision(DateTimeOffset now)
    {
        if (State != ProcessState.RueckfrageOpen)
            throw new InvalidOperationException($"Neueinreichung nur aus {ProcessState.RueckfrageOpen}.");

        Revision++;
        DataCard = null;
        CloseRueckfrage(now);
        Record(ProcessState.Created, now, $"Neueinreichung (Revision {Revision})");
    }

    private void Record(ProcessState target, DateTimeOffset now, string? note)
    {
        _transitions.Add(new ProcessTransition(State, target, now, note));
        State = target;
        StateChangedAt = now;
        UpdatedAt = now;
        // Eine Freigabe gilt nur für den Zustand, in dem sie erteilt wurde.
        ApprovedSubmission = null;
    }
}

public sealed record ProcessTransition(ProcessState From, ProcessState To, DateTimeOffset At, string? Note);

public enum RueckfrageResolution
{
    /// <summary>Ein Mensch hat die Rückfrage direkt beantwortet; weiter auf die Zusage warten.</summary>
    AnsweredDirectly,

    /// <summary>Antrag mit korrigierten Daten neu einreichen.</summary>
    Resubmit,
}
