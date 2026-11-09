namespace PvAutopilot.Core.DataCard;

/// <summary>
/// Anlagenerrichter der Instanz (in der Regel der Solarteur selbst). Gilt für jeden Vorgang,
/// sofern das CRM am Vorgang keinen abweichenden Errichter liefert.
/// </summary>
public sealed class ErrichterOptions
{
    public const string SectionName = "Errichter";

    public string Firma { get; set; } = string.Empty;
    public string Ort { get; set; } = string.Empty;

    /// <summary>Eintragungsnummer im Installateurverzeichnis.</summary>
    public string Eintragungsnummer { get; set; } = string.Empty;

    /// <summary>Netzbetreiber, bei dem der Errichter eingetragen ist.</summary>
    public string EingetragenBeiNetzbetreiber { get; set; } = string.Empty;

    /// <summary><c>null</c>, solange Firma oder Ort nicht konfiguriert sind.</summary>
    public Anlagenerrichter? ToErrichter() =>
        string.IsNullOrWhiteSpace(Firma) || string.IsNullOrWhiteSpace(Ort)
            ? null
            : new Anlagenerrichter(Firma.Trim(), Ort.Trim(), NullIfEmpty(Eintragungsnummer), NullIfEmpty(EingetragenBeiNetzbetreiber));

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
