using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Sources.HubSpot;

/// <summary>
/// HubSpot-Anbindung. Objekttypen, Property-Namen und Pipeline-Stages sind kundenspezifisch
/// und kommen ausschließlich aus der Konfiguration.
/// </summary>
public sealed class HubSpotOptions
{
    public const string SectionName = "HubSpot";

    public Uri BaseUrl { get; set; } = new("https://api.hubapi.com/");

    /// <summary>Token der Private App, nur über Umgebungsvariable <c>HubSpot__AccessToken</c>.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Client-Secret der App für die Webhook-Signatur v3, nur über Umgebungsvariable.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Objekttyp-ID der Projekte.</summary>
    public string ProjectObjectTypeId { get; set; } = string.Empty;

    /// <summary>Welche Property-Änderung welches Ereignis auslöst (docs/konzept/prozesssteuerung.md).</summary>
    public List<HubSpotTriggerRule> TriggerRules { get; set; } = [];

    /// <summary>Zuordnung fachlicher Feldnamen zu HubSpot-Property-Namen (z. B. <c>ReonicProjectId</c> → <c>reonic_projekt_id</c>).</summary>
    public Dictionary<string, string> Properties { get; set; } = [];
}

/// <summary>Eine Property-Änderung am Projekt, die ein Ereignis auslöst.</summary>
public sealed class HubSpotTriggerRule
{
    public ProcessTriggerKind Kind { get; set; }

    /// <summary>Interner HubSpot-Property-Name. Leer = Regel noch nicht konfiguriert, wird ignoriert.</summary>
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>
    /// Werte, die auslösen (z. B. Stage-IDs, <c>true</c>). Leer = jeder nicht-leere Wert;
    /// für <see cref="ProcessTriggerKind.Start"/> und <see cref="ProcessTriggerKind.Cancelled"/> Pflicht.
    /// </summary>
    public List<string> Values { get; set; } = [];

    /// <summary>Bei <see cref="ProcessTriggerKind.ReviewApproved"/>: welche Stufe freigegeben wird.</summary>
    public SubmissionKind? Submission { get; set; }

    /// <summary>Bei <see cref="ProcessTriggerKind.RueckfrageResolved"/>: wie die Rückfrage erledigt wurde.</summary>
    public RueckfrageResolution? Resolution { get; set; }

    /// <summary>Der Property-Wert ist ein Datum (Epoch-Millisekunden oder <c>yyyy-MM-dd</c>) und wird zu <see cref="ProcessTrigger.Date"/>.</summary>
    public bool IsDate { get; set; }

    /// <summary>
    /// Bei Pipeline-Stages würde „jeder Wert“ bei jedem Stage-Wechsel auslösen. Start und Storno
    /// brauchen deshalb explizite Werte; ohne sie bleibt die Regel inaktiv.
    /// </summary>
    internal bool RequiresValues => Kind is ProcessTriggerKind.Start or ProcessTriggerKind.Cancelled;

    internal bool IsActive => !string.IsNullOrWhiteSpace(PropertyName) && (!RequiresValues || Values.Count > 0);
}
