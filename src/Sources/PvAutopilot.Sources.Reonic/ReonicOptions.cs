namespace PvAutopilot.Sources.Reonic;

public sealed class ReonicOptions
{
    public const string SectionName = "Reonic";

    public Uri BaseUrl { get; set; } = new("https://api.reonic.com/rest/v3/");

    /// <summary>v3-Key (<c>rnc_v3_…</c>), nur über Umgebungsvariable <c>Reonic__ApiKey</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Reonic cacht GET-Antworten bis zu 1 h. Für Checklisten-Werte deshalb ohne Cache lesen
    /// (zählt gegen das kleinere „uncached“-Rate-Limit).
    /// </summary>
    public bool BypassCache { get; set; } = true;

    /// <summary>Phase, deren Checkliste den Block „Netzanmeldung“ enthält (<c>request</c>, <c>offer</c>, <c>installation</c>).</summary>
    public string ChecklistStage { get; set; } = "offer";

    /// <summary>
    /// Secret des Reonic-Webhooks (HMAC-SHA256, Header <c>X-Reonic-Signature</c>), nur über Umgebungsvariable
    /// <c>Reonic__WebhookSecret</c>. Leer = Webhook aus: <c>POST /webhooks/reonic</c> lehnt alles mit 401 ab.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;
}
