using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core.Processes;

/// <summary>
/// Eine externe Einreichung durchläuft immer Bereit → Vorbereitet → (Freigabe) → Eingereicht.
/// Wird sie beim Netzbetreiber nicht separat gebraucht, geht es direkt von Bereit nach Eingereicht.
/// </summary>
public sealed record SubmissionStage(
    SubmissionKind Kind,
    ProcessState Ready,
    ProcessState Prepared,
    ProcessState AwaitingReview,
    ProcessState Submitted)
{
    public bool Handles(ProcessState state) => state == Ready || state == Prepared || state == AwaitingReview;
}

public static class SubmissionStages
{
    public static readonly SubmissionStage Netzanmeldung = new(SubmissionKind.Netzanmeldung,
        ProcessState.NetzbetreiberResolved, ProcessState.NetzanmeldungPrepared,
        ProcessState.NetzanmeldungAwaitingReview, ProcessState.NetzanmeldungSubmitted);

    public static readonly SubmissionStage Inbetriebnahme = new(SubmissionKind.Inbetriebnahme,
        ProcessState.InbetriebnahmeReady, ProcessState.InbetriebnahmePrepared,
        ProcessState.InbetriebnahmeAwaitingReview, ProcessState.InbetriebnahmeSubmitted);

    public static readonly SubmissionStage Zaehlerwechsel = new(SubmissionKind.Zaehlerwechsel,
        ProcessState.InbetriebnahmeSubmitted, ProcessState.ZaehlerwechselPrepared,
        ProcessState.ZaehlerwechselAwaitingReview, ProcessState.ZaehlerwechselRequested);

    public static readonly SubmissionStage Mastr = new(SubmissionKind.Mastr,
        ProcessState.ZaehlerwechselCompleted, ProcessState.MastrPrepared,
        ProcessState.MastrAwaitingReview, ProcessState.MastrRegistered);

    public static readonly IReadOnlyList<SubmissionStage> All = [Netzanmeldung, Inbetriebnahme, Zaehlerwechsel, Mastr];

    public static SubmissionStage For(SubmissionKind kind) => All.Single(s => s.Kind == kind);

    /// <summary>Die Stufe, deren Freigabe der Vorgang in diesem Zustand erwartet, sonst <c>null</c>.</summary>
    public static SubmissionStage? AwaitingReviewIn(ProcessState state) => All.SingleOrDefault(s => s.AwaitingReview == state);

    /// <summary>
    /// Ledger-Schlüssel der aktuellen Einreichung. Nur die Netzanmeldung wird nach einer Rückfrage neu eingereicht;
    /// E.8, Zählerantrag und MaStR gibt es je Vorgang einmal.
    /// </summary>
    public static SubmissionKey CurrentSubmissionKey(this AnmeldeProcess process, SubmissionKind kind) =>
        new(process.Id, kind, kind == SubmissionKind.Netzanmeldung ? process.Revision : 0);

    /// <summary>Fachlicher Name für Task-Texte und Audit.</summary>
    public static string Label(this SubmissionKind kind) => kind switch
    {
        SubmissionKind.Netzanmeldung => "Netzanmeldung",
        SubmissionKind.Inbetriebnahme => "Inbetriebnahme (E.8)",
        SubmissionKind.Zaehlerwechsel => "Zählerwechsel",
        SubmissionKind.Mastr => "MaStR-Registrierung",
        _ => kind.ToString(),
    };
}
