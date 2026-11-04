using PvAutopilot.Core.DataCard;
using PvAutopilot.Sources.Reonic.Checklist;
using Keys = PvAutopilot.Sources.Reonic.Checklist.NetzanmeldungChecklist.Keys;

namespace PvAutopilot.Sources.Reonic.Mapping;

/// <summary>Was die Checkliste „Netzanmeldung“ zur Datenkarte beiträgt.</summary>
public sealed record ChecklistData(
    bool Kaskadenschaltung,
    Installationsart? Installationsart,
    string? Messkonzept,
    string? MesskonzeptBemerkung,
    IReadOnlyList<Zaehler> Zaehler,
    IReadOnlyList<DocumentRef> Dokumente,
    IReadOnlyList<string> Problems);

/// <summary>
/// Liest die Vor-Ort-Angaben aus der Checkliste und prüft die Pflichtfelder, auch die bedingten
/// („Pflicht, wenn …“), die in Reonic bewusst nicht als Pflicht gesetzt sind.
/// Problemtexte nennen nur Feldnamen, nie Werte.
/// </summary>
public static class ChecklistMapper
{
    public const string DocumentSource = "reonic-checklist";

    public static ChecklistData Map(ChecklistValues values)
    {
        var problems = new List<string>();
        problems.AddRange(values.DuplicateKeys.Distinct().Select(k => $"Checkliste: Feld {k} ist mehrfach vorhanden"));

        var documents = new List<DocumentRef>();
        AddAttachments(values, Keys.FotoHauptzaehler, DocumentKind.FotoHauptzaehler, "Foto Hauptstromzähler", problems, documents);
        AddAttachments(values, Keys.FotoZaehlerschrank, DocumentKind.FotoZaehlerschrank, "Foto Zählerschrank", problems, documents);
        AddAttachments(values, Keys.FotoHak, DocumentKind.FotoHausanschlusskasten, "Foto Hausanschlusskasten", problems, documents);
        AddAttachments(values, Keys.Schaltplan, DocumentKind.Schaltplan, "Übersichtsschaltplan", problems, documents);

        var kaskade = Required(values, Keys.Kaskade, NetzanmeldungChecklist.JaNein, "Kaskadenschaltung", problems);
        var art = Required(values, Keys.AnlageArt, NetzanmeldungChecklist.AnlagenArten, "Neuerrichtung oder Erweiterung", problems);

        // Gespeichert wird die kanonische Schreibweise, damit Netzbetreiber-Adapter exakt zuordnen können.
        var auswahl = values.Choice(Keys.Messkonzept);
        var messkonzept = NetzanmeldungChecklist.Messkonzepte
            .FirstOrDefault(m => ChecklistText.Normalize(m) == ChecklistText.Normalize(auswahl));
        var bemerkung = values.Text(Keys.MesskonzeptBemerkung);
        if (auswahl is null)
            problems.Add("Checkliste: Messkonzept fehlt");
        else if (messkonzept is null)
            problems.Add("Checkliste: Messkonzept hat einen unbekannten Wert");
        else if (messkonzept == NetzanmeldungChecklist.MesskonzeptSonstiges && bemerkung is null)
            problems.Add("Checkliste: Bemerkung Messkonzept fehlt (Pflicht bei „Sonstiges“)");

        return new ChecklistData(
            kaskade ?? false,
            art,
            messkonzept,
            bemerkung,
            MapZaehler(values, problems),
            documents,
            problems);
    }

    private static List<Zaehler> MapZaehler(ChecklistValues values, List<string> problems)
    {
        var zaehler = new List<Zaehler>();
        var anzahlText = values.Choice(Keys.ZaehlerAnzahl);
        if (anzahlText is null)
        {
            problems.Add("Checkliste: Anzahl Zähler fehlt");
            return zaehler;
        }
        if (!int.TryParse(anzahlText, out var anzahl) || anzahl < 0 || anzahl > NetzanmeldungChecklist.MaxZaehler)
        {
            problems.Add("Checkliste: Anzahl Zähler hat einen unbekannten Wert");
            return zaehler;
        }

        for (var n = 1; n <= anzahl; n++)
        {
            var label = $"Zähler {n}";
            var typ = Required(values, Keys.ZaehlerTyp(n), NetzanmeldungChecklist.ZaehlerTypen, $"{label}: Typ Zählerwechsel", problems);
            var nummer = values.Text(Keys.ZaehlerNummer(n));
            if (nummer is null && typ is not ZaehlerwechselTyp.Neusetzung)
                problems.Add($"Checkliste: {label}: Zählernummer fehlt");

            var lage = values.Choice(Keys.ZaehlerVerbrauchsstelle(n));
            if (lage is null)
                problems.Add($"Checkliste: {label}: Lage der Verbrauchsstelle fehlt");
            var einbauort = values.Choice(Keys.ZaehlerEinbauort(n));
            if (einbauort is null)
                problems.Add($"Checkliste: {label}: Einbauort fehlt");

            Zaehlerbauform? bauform = null;
            var befestigung = values.Choice(Keys.ZaehlerBefestigung(n));
            if (befestigung is null)
                problems.Add($"Checkliste: {label}: Befestigung fehlt");
            else if (!NetzanmeldungChecklist.Befestigungen.TryGetValue(ChecklistText.Normalize(befestigung), out bauform))
                problems.Add($"Checkliste: {label}: Befestigung hat einen unbekannten Wert");

            if (typ is { } t)
                zaehler.Add(new Zaehler(nummer, t, lage, values.Text(Keys.ZaehlerVerbrauchsstelleText(n)), einbauort, bauform));
        }
        return zaehler;
    }

    private static T? Required<T>(
        ChecklistValues values, string key, IReadOnlyDictionary<string, T> options, string label, List<string> problems)
        where T : struct
    {
        var choice = values.Choice(key);
        if (choice is null)
        {
            problems.Add($"Checkliste: {label} fehlt");
            return null;
        }
        if (options.TryGetValue(ChecklistText.Normalize(choice), out var value))
            return value;

        problems.Add($"Checkliste: {label} hat einen unbekannten Wert");
        return null;
    }

    private static void AddAttachments(
        ChecklistValues values, string key, DocumentKind kind, string label, List<string> problems, List<DocumentRef> documents)
    {
        var attachments = values.Attachments(key);
        if (attachments.Count == 0)
        {
            problems.Add($"Checkliste: {label} fehlt");
            return;
        }
        for (var i = 0; i < attachments.Count; i++)
        {
            if (attachments[i].Type is not ("image" or "pdf"))
            {
                problems.Add($"Checkliste: {label} enthält eine Datei, die weder Bild noch PDF ist");
                continue;
            }
            documents.Add(new DocumentRef(kind, DocumentSource, attachments[i].Url!,
                $"{key}-{i + 1}{Extension(attachments[i])}"));
        }
    }

    /// <summary>Endung aus der URL, sonst nach Typ. Fotos kommen nicht zwingend als JPEG.</summary>
    private static string Extension(Api.ReonicAttachment attachment)
    {
        if (Uri.TryCreate(attachment.Url, UriKind.Absolute, out var uri)
            && Path.GetExtension(uri.AbsolutePath) is { Length: > 1 and <= 5 } fromUrl)
            return fromUrl.ToLowerInvariant();
        return attachment.Type == "pdf" ? ".pdf" : ".jpg";
    }
}
