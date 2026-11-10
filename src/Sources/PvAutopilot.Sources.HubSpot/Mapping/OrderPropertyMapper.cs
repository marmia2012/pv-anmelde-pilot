using System.Globalization;
using PvAutopilot.Core.Orders;

namespace PvAutopilot.Sources.HubSpot.Mapping;

/// <summary>
/// Auftrag → Deal-Properties (<c>Auftrag*</c> in <see cref="HubSpotOptions.Properties"/>); leerer Name = nicht geschrieben.
/// Formate: Datum <c>yyyy-MM-dd</c> (Kalendertag in <see cref="HubSpotOptions.TimeZone"/>), Zahlen invariant mit Punkt,
/// Booleans <c>true</c>/<c>false</c>, Texte z. B. „40 × Hersteller Modell (450 Wp)“, mehrere mit „; “.
/// Ja/Nein-Felder werden immer geschrieben, Texte und Zahlen nur, wenn vorhanden – ein fehlender Wert überschreibt nichts.
/// </summary>
public static class OrderPropertyMapper
{
    public const string Separator = "; ";

    public static Dictionary<string, string> Map(SignedOrder order, HubSpotOptions options)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        void Set(string key, string? value)
        {
            if (options.PropertyName(key) is { } name && !string.IsNullOrWhiteSpace(value))
                properties[name] = value;
        }

        var s = order.Summary;
        if (order.SignedAt is { } signedAt)
            Set(HubSpotPropertyKeys.AuftragUnterschriftsdatum, SignedDay(signedAt, options.ResolveTimeZone()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        if (s.Module.Count > 0)
        {
            Set(HubSpotPropertyKeys.AuftragKwp, Number(s.LeistungKwp));
            Set(HubSpotPropertyKeys.AuftragModule, string.Join(Separator,
                s.Module.Select(m => $"{Number(m.Anzahl)} × {Label(m.Hersteller, m.Modell)} ({Number(m.LeistungWp)} Wp)")));
        }

        if (s.Wechselrichter.Count > 0)
        {
            Set(HubSpotPropertyKeys.AuftragWrLeistungKw, Number(s.WechselrichterLeistungKw));
            Set(HubSpotPropertyKeys.AuftragWechselrichter, string.Join(Separator,
                s.Wechselrichter.Select(w => $"{Number(w.Anzahl)} × {Label(w.Hersteller, w.Modell)} ({Number(w.LeistungW / 1000m)} kW)")));
        }

        Set(HubSpotPropertyKeys.AuftragSpeicherVorhanden, Bool(s.Speicher is not null));
        if (s.Speicher is { } speicher)
        {
            Set(HubSpotPropertyKeys.AuftragSpeicher, $"{Label(speicher.Hersteller, speicher.Modell)} ({Number(speicher.KapazitaetKwh)} kWh)");
            Set(HubSpotPropertyKeys.AuftragSpeicherKwh, Number(speicher.KapazitaetKwh));
        }

        Set(HubSpotPropertyKeys.AuftragWallboxVorhanden, Bool(s.Wallbox is not null));
        if (s.Wallbox is { } wallbox)
        {
            Set(HubSpotPropertyKeys.AuftragWallbox, $"{Number(wallbox.Anzahl)} × {Label(wallbox.Hersteller, wallbox.Modell)} ({Number(wallbox.LeistungKw)} kW)");
            Set(HubSpotPropertyKeys.AuftragWallboxKw, Number(wallbox.LeistungKw));
        }

        return properties;
    }

    /// <summary>Kalendertag der Unterschrift in der Instanz-Zeitzone (eine Unterschrift um 00:30 deutscher Zeit ist in UTC noch der Vortag).</summary>
    public static DateOnly SignedDay(DateTimeOffset signedAt, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(signedAt, timeZone).DateTime);

    /// <summary>Invariant mit Punkt, höchstens drei Nachkommastellen, ohne Nullen am Ende.</summary>
    public static string Number(decimal value) => decimal.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Label(string brand, string model) => $"{brand} {model}".Trim();
}
