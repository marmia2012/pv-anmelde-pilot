namespace PvAutopilot.Core.Processes;

/// <summary>
/// Zustände eines Anmeldevorgangs (eine PV-Anlage = ein Vorgang).
/// Die Werte werden als Text in der Datenbank gespeichert – Namen nicht umbenennen.
/// </summary>
public enum ProcessState
{
    Created,
    DataCardAggregated,
    NetzbetreiberResolved,

    /// <summary>Antragspaket erzeugt; im DryRun endet der Vorgang hier.</summary>
    ApplicationPrepared,

    /// <summary>Modus Review: wartet auf Freigabe in HubSpot.</summary>
    AwaitingReview,

    /// <summary>Antrag ist beim Netzbetreiber; wartet auf Einspeisezusage oder Rückfrage.</summary>
    ApplicationSubmitted,
    RueckfrageOpen,
    EinspeisezusageReceived,
    InbetriebnahmeSubmitted,
    ZaehlerwechselRequested,
    MastrRegistered,
    Completed,

    /// <summary>Technischer Fehler nach allen Retries oder unklarer Versandstatus – braucht einen Menschen.</summary>
    Blocked,
}
