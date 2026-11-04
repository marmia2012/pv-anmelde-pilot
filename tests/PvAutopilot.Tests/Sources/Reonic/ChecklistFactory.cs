using PvAutopilot.Sources.Reonic.Api;
using Keys = PvAutopilot.Sources.Reonic.Checklist.NetzanmeldungChecklist.Keys;

namespace PvAutopilot.Tests.Sources.Reonic;

/// <summary>Baut Reonic-Checklisten für Tests. Nur fiktive Werte.</summary>
internal static class ChecklistFactory
{
    public static string Key(string key) => $"Bitte vollständig ausfüllen.\n[key: {key}]";

    public static ReonicChecklistItem Radio(string? title, string? description, string? selected, params string[] options)
    {
        var all = options.ToList();
        if (selected is not null && !all.Contains(selected))
            all.Add(selected);
        return new ReonicChecklistItem("radio", title, description, null, false,
            all.Select(o => new ReonicChecklistSubItem(o, o == selected, null, null, false, null)).ToList());
    }

    public static ReonicChecklistItem FreeText(string? title, string? description, string? text) =>
        new("freetext", title, description, text, false, null);

    public static ReonicChecklistItem Image(string? title, params ReonicChecklistSubItem[] requests) =>
        new("image", title, null, null, false, requests);

    public static ReonicChecklistSubItem Request(string? title, string? description, string type, params string[] urls) =>
        new(null, false, title, description, false, urls.Select(u => new ReonicAttachment(type, u)).ToList());

    public static ReonicChecklist Checklist(IEnumerable<ReonicChecklistItem> items) =>
        new("offer", [new ReonicChecklistBlock("b1", "Netzanmeldung", [new ReonicChecklistSegment("s1", "NA – Zähleranlage", null, items.ToList())])]);

    public static ReonicChecklist Checklist(params ReonicChecklistItem[] items) => Checklist((IEnumerable<ReonicChecklistItem>)items);

    /// <summary>Vollständig ausgefüllte Checkliste mit einem Zähler. Schlüssel → Element, damit Tests einzelne Felder ersetzen/entfernen.</summary>
    public static Dictionary<string, ReonicChecklistItem> Complete()
    {
        var items = new Dictionary<string, ReonicChecklistItem>
        {
            [Keys.FotoHauptzaehler] = Image("Fotos Zähleranlage", Request("Foto Hauptstromzähler (Nummer lesbar)", Key(Keys.FotoHauptzaehler), "image", "https://files.example/hz.jpg")),
            [Keys.FotoZaehlerschrank] = Image("Fotos Zählerschrank", Request("Foto Zählerschrank (geöffnet, komplett)", Key(Keys.FotoZaehlerschrank), "image", "https://files.example/zs1.jpg", "https://files.example/zs2.jpg")),
            [Keys.FotoHak] = Image("Fotos HAK", Request("Foto Hausanschlusskasten (HAK) mit Sicherungen", Key(Keys.FotoHak), "image", "https://files.example/hak.jpg")),
            [Keys.Kaskade] = Radio("Kaskadenschaltung", Key(Keys.Kaskade), "Nein", "Ja", "Nein"),
            [Keys.ZaehlerAnzahl] = Radio("Anzahl Zähler mit Wechsel, Abmeldung oder Neusetzung", Key(Keys.ZaehlerAnzahl), "1", "0", "1", "2", "3", "4"),
            [Keys.AnlageArt] = Radio("Neuerrichtung oder Erweiterung", Key(Keys.AnlageArt), "Neuerrichtung", "Neuerrichtung", "Erweiterung einer bestehenden Anlage"),
            [Keys.Messkonzept] = Radio("Messkonzept", Key(Keys.Messkonzept), "Überschusseinspeisung – Zweirichtungszähler"),
            [Keys.MesskonzeptBemerkung] = FreeText("Bemerkung Messkonzept", Key(Keys.MesskonzeptBemerkung), null),
            [Keys.Schaltplan] = Image("Unterlagen", Request("Übersichtsschaltplan", Key(Keys.Schaltplan), "pdf", "https://files.example/schaltplan.pdf")),
        };
        foreach (var (key, item) in Zaehler(1, "Tausch gegen Zweirichtungszähler", "1ESY0000000001"))
            items[key] = item;
        return items;
    }

    public static IEnumerable<KeyValuePair<string, ReonicChecklistItem>> Zaehler(int n, string typ, string? nummer, string befestigung = "eHZ")
    {
        yield return new(Keys.ZaehlerTyp(n), Radio($"Zähler {n}: Typ Zählerwechsel", Key(Keys.ZaehlerTyp(n)), typ));
        yield return new(Keys.ZaehlerNummer(n), FreeText($"Zähler {n}: Zählernummer", Key(Keys.ZaehlerNummer(n)), nummer));
        yield return new(Keys.ZaehlerVerbrauchsstelle(n), Radio($"Zähler {n}: Lage der Verbrauchsstelle", Key(Keys.ZaehlerVerbrauchsstelle(n)), "Wohnung"));
        yield return new(Keys.ZaehlerVerbrauchsstelleText(n), FreeText($"Zähler {n}: Bezeichnung Verbrauchsstelle", Key(Keys.ZaehlerVerbrauchsstelleText(n)), "Wohnung EG links"));
        yield return new(Keys.ZaehlerEinbauort(n), Radio($"Zähler {n}: Einbauort", Key(Keys.ZaehlerEinbauort(n)), "Keller"));
        yield return new(Keys.ZaehlerBefestigung(n), Radio($"Zähler {n}: Befestigung", Key(Keys.ZaehlerBefestigung(n)), befestigung, "3-Punkt", "eHZ", "Unbekannt"));
    }
}
