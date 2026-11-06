namespace PvAutopilot.Core.Processes;

/// <summary>
/// Zustände eines Anmeldevorgangs (eine PV-Anlage = ein Vorgang). Ablauf: docs/konzept/prozesssteuerung.md.
/// Die Werte werden als Text in der Datenbank gespeichert – Namen nicht umbenennen.
/// </summary>
public enum ProcessState
{
    Created,
    DataCardAggregated,
    NetzbetreiberResolved,

    /// <summary>Antragspaket erzeugt; im DryRun endet der Vorgang hier.</summary>
    NetzanmeldungPrepared,
    NetzanmeldungAwaitingReview,

    /// <summary>Antrag ist beim Netzbetreiber; wartet auf Einspeisezusage oder Rückfrage.</summary>
    NetzanmeldungSubmitted,
    RueckfrageOpen,

    /// <summary>Zusage liegt vor; wartet auf das Inbetriebnahmedatum.</summary>
    EinspeisezusageReceived,

    InbetriebnahmeReady,
    InbetriebnahmePrepared,
    InbetriebnahmeAwaitingReview,
    InbetriebnahmeSubmitted,

    ZaehlerwechselPrepared,
    ZaehlerwechselAwaitingReview,

    /// <summary>Zählerantrag ist raus (separat oder mit dem E.8); wartet auf den Zählerwechsel.</summary>
    ZaehlerwechselRequested,

    /// <summary>Zähler gewechselt; bereit für MaStR (wartet ggf. auf den Kundenlogin).</summary>
    ZaehlerwechselCompleted,

    MastrPrepared,
    MastrAwaitingReview,
    MastrRegistered,

    Completed,

    /// <summary>Technischer Fehler nach allen Retries oder unklarer Versandstatus – braucht einen Menschen.</summary>
    Blocked,

    /// <summary>Projekt storniert.</summary>
    Cancelled,
}

public static class ProcessStates
{
    /// <summary>Endzustände: hier passiert nichts mehr.</summary>
    public static bool IsTerminal(this ProcessState state) => state is ProcessState.Completed or ProcessState.Cancelled;

    /// <summary>
    /// Zustände, die ein Schritt ohne äußeres Ereignis sofort weiterführt. Steht ein Vorgang hier länger,
    /// ist ein Job verloren gegangen und der Watchdog plant ihn neu ein.
    /// </summary>
    /// <remarks>
    /// „Vorbereitet“ zählt dazu: Im DryRun endet der Vorgang dort, nach einem Moduswechsel auf Review/Live
    /// muss er aber weiterlaufen. Im DryRun plant der Watchdog diese Zustände nicht neu ein (<see cref="IsPrepared"/>).
    /// </remarks>
    public static bool IsActive(this ProcessState state) => state is
        ProcessState.Created or ProcessState.DataCardAggregated or ProcessState.NetzbetreiberResolved
        or ProcessState.InbetriebnahmeReady or ProcessState.InbetriebnahmeSubmitted or ProcessState.MastrRegistered
        || state.IsPrepared();

    /// <summary>Einreichung erzeugt, aber weder zur Freigabe vorgelegt noch versendet (Endpunkt im DryRun).</summary>
    public static bool IsPrepared(this ProcessState state) => state is
        ProcessState.NetzanmeldungPrepared or ProcessState.InbetriebnahmePrepared
        or ProcessState.ZaehlerwechselPrepared or ProcessState.MastrPrepared;
}
