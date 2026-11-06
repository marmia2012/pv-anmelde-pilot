using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Notifications;
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

    /// <summary>
    /// Objekttyp-ID des CRM-Objekts, an dem der Vorgang hängt. Default <c>0-3</c> (Deal);
    /// für HubSpot-Projekte z. B. <c>0-970</c> bzw. die ID eines Custom Objects.
    /// </summary>
    public string ObjectTypeId { get; set; } = "0-3";

    /// <summary>
    /// Veralteter Schlüssel (vor <see cref="ObjectTypeId"/>). Wird nicht mehr ausgewertet; ist er gesetzt, warnt der Start,
    /// damit eine alte Konfiguration nicht unbemerkt auf den Default <c>0-3</c> fällt.
    /// </summary>
    public string? ProjectObjectTypeId { get; set; }

    /// <summary>
    /// IANA-Zeitzone der Instanz für Datum-Uhrzeit-Werte aus HubSpot (z. B. Termine): solche Werte werden in diese Zone
    /// umgerechnet, bevor der Kalendertag genommen wird. Reine Datumswerte (Mitternacht UTC, <c>yyyy-MM-dd</c>) bleiben, wie sie sind.
    /// </summary>
    public string TimeZone { get; set; } = "Europe/Berlin";

    /// <summary>Die Zeitzone aus <see cref="TimeZone"/>; die Startvalidierung stellt sicher, dass sie existiert.</summary>
    public TimeZoneInfo ResolveTimeZone() => TimeZoneInfo.FindSystemTimeZoneById(TimeZone.Trim());

    /// <summary>
    /// Schreibzugriffe (Aufgaben, Notizen, Dateien, Status) aktiv. Default <c>false</c>: es wird nur geloggt.
    /// </summary>
    public bool WriteEnabled { get; set; }

    /// <summary>Welche Audit-Ereignisse als Notiz am Deal landen: <c>Verbose</c> (alles), <c>Info</c> (wichtige Ereignisse), <c>Error</c> (nur Fehler).</summary>
    public AuditLevel AuditLevel { get; set; } = AuditLevel.Info;

    /// <summary>HubSpot-Owner-ID für Aufgaben, wenn der Deal keinen Inhaber hat. Leer = Aufgabe ohne Zuweisung.</summary>
    public string DefaultTaskOwnerId { get; set; } = string.Empty;

    /// <summary>
    /// Teil des Dateinamens, an dem die EVU-Vollmacht unter den PDF-Anhängen des Deals erkannt wird
    /// (Groß-/Kleinschreibung egal).
    /// </summary>
    public string VollmachtFileNamePattern { get; set; } = "Vollmacht";

    /// <summary>
    /// Zuordnung der internen Optionswerte der Einspeiseart-Property zu <see cref="Einspeiseart"/>
    /// (Groß-/Kleinschreibung egal). Leerer Wert am Deal = Default (Teileinspeisung), unbekannter Wert = fachliches Problem.
    /// </summary>
    public Dictionary<string, Einspeiseart> EinspeiseartValues { get; set; } = [];

    /// <summary>Welche Property-Änderung welches Ereignis auslöst (docs/konzept/prozesssteuerung.md).</summary>
    public List<HubSpotTriggerRule> TriggerRules { get; set; } = [];

    /// <summary>
    /// Zuordnung fachlicher Feldnamen (<see cref="HubSpotPropertyKeys"/>) zu HubSpot-Property-Namen
    /// (z. B. <c>ReonicProjectId</c> → <c>reonic_projekt_id</c>). Leerer Name = Feld wird nicht gelesen.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = [];

    /// <summary>Interner HubSpot-Name zum fachlichen Schlüssel; <c>null</c>, wenn nicht konfiguriert.</summary>
    public string? PropertyName(string key) =>
        Properties.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : null;
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
