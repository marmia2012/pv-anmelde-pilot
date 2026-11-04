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

    /// <summary>Property, deren Änderung den Prozess startet (i. d. R. die Pipeline-Stage).</summary>
    public string TriggerPropertyName { get; set; } = "hs_pipeline_stage";

    /// <summary>Stage-ID(s) „Termine an Kunde gesendet &amp; unterschriebene Vollmacht zurück“.</summary>
    public List<string> TriggerPropertyValues { get; set; } = [];

    /// <summary>Property, mit der ein Mensch einen Antrag im Modus Review freigibt.</summary>
    public string ReviewApprovalPropertyName { get; set; } = string.Empty;

    public string ReviewApprovalPropertyValue { get; set; } = "true";

    /// <summary>Zuordnung fachlicher Feldnamen zu HubSpot-Property-Namen (z. B. <c>ReonicProjectId</c> → <c>reonic_projekt_id</c>).</summary>
    public Dictionary<string, string> Properties { get; set; } = [];
}
