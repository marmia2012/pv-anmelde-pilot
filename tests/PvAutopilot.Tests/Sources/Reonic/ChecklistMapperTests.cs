using PvAutopilot.Core.DataCard;
using PvAutopilot.Sources.Reonic.Api;
using PvAutopilot.Sources.Reonic.Checklist;
using PvAutopilot.Sources.Reonic.Mapping;
using static PvAutopilot.Tests.Sources.Reonic.ChecklistFactory;
using Keys = PvAutopilot.Sources.Reonic.Checklist.NetzanmeldungChecklist.Keys;

namespace PvAutopilot.Tests.Sources.Reonic;

public class ChecklistMapperTests
{
    private static ChecklistData Map(Dictionary<string, ReonicChecklistItem> items) =>
        ChecklistMapper.Map(ChecklistValues.Parse(Checklist(items.Values), NetzanmeldungChecklist.TitleFallback));

    private static void Set(Dictionary<string, ReonicChecklistItem> items, IEnumerable<KeyValuePair<string, ReonicChecklistItem>> entries)
    {
        foreach (var (key, item) in entries)
            items[key] = item;
    }

    [Fact]
    public void Complete_checklist_maps_without_problems()
    {
        var data = Map(Complete());

        Assert.Empty(data.Problems);
        Assert.False(data.Kaskadenschaltung);
        Assert.Equal(Installationsart.Neuerrichtung, data.Installationsart);
        Assert.Equal("Überschusseinspeisung – Zweirichtungszähler", data.Messkonzept);
        Assert.Null(data.MesskonzeptBemerkung);

        var zaehler = Assert.Single(data.Zaehler);
        Assert.Equal(new Zaehler("1ESY0000000001", ZaehlerwechselTyp.TauschZweirichtungszaehler, "Wohnung", "Wohnung EG links", "Keller", Zaehlerbauform.Ehz), zaehler);

        Assert.Equal(5, data.Dokumente.Count);
        Assert.All(data.Dokumente, d => Assert.Equal(ChecklistMapper.DocumentSource, d.Source));
        Assert.Equal(2, data.Dokumente.Count(d => d.Kind == DocumentKind.FotoZaehlerschrank));
        var schaltplan = Assert.Single(data.Dokumente, d => d.Kind == DocumentKind.Schaltplan);
        Assert.Equal("https://files.example/schaltplan.pdf", schaltplan.Reference);
        Assert.EndsWith(".pdf", schaltplan.FileName);
        Assert.EndsWith(".jpg", Assert.Single(data.Dokumente, d => d.Kind == DocumentKind.FotoHauptzaehler).FileName);
        Assert.Single(data.Dokumente, d => d.Kind == DocumentKind.FotoHausanschlusskasten);
    }

    [Fact]
    public void Kaskade_ja_and_erweiterung_are_mapped()
    {
        var items = Complete();
        items[Keys.Kaskade] = Radio("Kaskadenschaltung", Key(Keys.Kaskade), "Ja", "Ja", "Nein");
        items[Keys.AnlageArt] = Radio("x", Key(Keys.AnlageArt), "Erweiterung einer bestehenden Anlage");

        var data = Map(items);

        Assert.True(data.Kaskadenschaltung);
        Assert.Equal(Installationsart.Erweiterung, data.Installationsart);
    }

    [Theory]
    [InlineData("na.foto.hauptzaehler", "Foto Hauptstromzähler fehlt")]
    [InlineData("na.foto.zaehlerschrank", "Foto Zählerschrank fehlt")]
    [InlineData("na.foto.hak", "Foto Hausanschlusskasten fehlt")]
    [InlineData("na.schaltplan", "Übersichtsschaltplan fehlt")]
    [InlineData("na.kaskade", "Kaskadenschaltung fehlt")]
    [InlineData("na.zaehler.anzahl", "Anzahl Zähler fehlt")]
    [InlineData("na.anlage.art", "Neuerrichtung oder Erweiterung fehlt")]
    [InlineData("na.messkonzept", "Messkonzept fehlt")]
    [InlineData("na.zaehler1.typ", "Zähler 1: Typ Zählerwechsel fehlt")]
    [InlineData("na.zaehler1.nummer", "Zähler 1: Zählernummer fehlt")]
    [InlineData("na.zaehler1.verbrauchsstelle", "Zähler 1: Lage der Verbrauchsstelle fehlt")]
    [InlineData("na.zaehler1.einbauort", "Zähler 1: Einbauort fehlt")]
    [InlineData("na.zaehler1.befestigung", "Zähler 1: Befestigung fehlt")]
    public void Each_missing_required_field_is_reported(string key, string expected)
    {
        var items = Complete();
        items.Remove(key);

        var problems = Map(items).Problems;

        var problem = Assert.Single(problems);
        Assert.Contains(expected, problem);
        Assert.StartsWith("Checkliste:", problem);
    }

    [Fact]
    public void Missing_verbrauchsstelle_text_is_optional()
    {
        var items = Complete();
        items.Remove(Keys.ZaehlerVerbrauchsstelleText(1));

        var data = Map(items);

        Assert.Empty(data.Problems);
        Assert.Null(Assert.Single(data.Zaehler).BezeichnungVerbrauchsstelle);
    }

    [Fact]
    public void Meter_fields_beyond_the_count_are_ignored()
    {
        var items = Complete();
        Set(items, Zaehler(3, "Ausbau – Zähler abmelden", "1ESY0000000003"));

        var data = Map(items);

        Assert.Empty(data.Problems);
        Assert.Single(data.Zaehler);
    }

    [Fact]
    public void Meter_fields_up_to_the_count_are_required()
    {
        var items = Complete();
        items[Keys.ZaehlerAnzahl] = Radio("x", Key(Keys.ZaehlerAnzahl), "2", "0", "1", "2", "3", "4");

        var problems = Map(items).Problems;

        Assert.Equal(5, problems.Count);
        Assert.All(problems, p => Assert.Contains("Zähler 2:", p));
    }

    [Fact]
    public void Four_meters_are_mapped_in_order()
    {
        var items = Complete();
        items[Keys.ZaehlerAnzahl] = Radio("x", Key(Keys.ZaehlerAnzahl), "4");
        Set(items, Zaehler(2, "Ausbau – Zähler abmelden", "1ESY0000000002", "3-Punkt"));
        Set(items, Zaehler(3, "Umbau auf Kaskade (Erzeugungszähler)", "1ESY0000000003"));
        Set(items, Zaehler(4, "Kein Wechsel (nur Bestand)", "1ESY0000000004"));

        var data = Map(items);

        Assert.Empty(data.Problems);
        Assert.Equal(
            [ZaehlerwechselTyp.TauschZweirichtungszaehler, ZaehlerwechselTyp.Ausbau, ZaehlerwechselTyp.UmbauKaskade, ZaehlerwechselTyp.KeinWechsel],
            data.Zaehler.Select(z => z.Wechseltyp));
        Assert.Equal(Zaehlerbauform.DreiPunkt, data.Zaehler[1].Bauform);
        Assert.True(data.Zaehler[1].Abmelden);
    }

    [Fact]
    public void Zero_meters_is_valid()
    {
        var items = Complete();
        items[Keys.ZaehlerAnzahl] = Radio("x", Key(Keys.ZaehlerAnzahl), "0");

        var data = Map(items);

        Assert.Empty(data.Problems);
        Assert.Empty(data.Zaehler);
    }

    [Fact]
    public void Meter_number_is_optional_for_neusetzung()
    {
        var items = Complete();
        Set(items, Zaehler(1, "Neusetzung zusätzlicher Zähler", null));

        var data = Map(items);

        Assert.Empty(data.Problems);
        var zaehler = Assert.Single(data.Zaehler);
        Assert.Equal(ZaehlerwechselTyp.Neusetzung, zaehler.Wechseltyp);
        Assert.Null(zaehler.Zaehlernummer);
    }

    [Fact]
    public void Choice_values_tolerate_dash_and_case_variants()
    {
        var items = Complete();
        Set(items, Zaehler(1, "ausbau - zähler abmelden", "1ESY0000000001", "EHZ"));

        var data = Map(items);

        Assert.Empty(data.Problems);
        Assert.Equal(ZaehlerwechselTyp.Ausbau, Assert.Single(data.Zaehler).Wechseltyp);
        Assert.Equal(Zaehlerbauform.Ehz, data.Zaehler[0].Bauform);
    }

    [Fact]
    public void Befestigung_unbekannt_maps_to_null_without_problem()
    {
        var items = Complete();
        Set(items, Zaehler(1, "Tausch gegen Zweirichtungszähler", "1ESY0000000001", "Unbekannt"));

        var data = Map(items);

        Assert.Empty(data.Problems);
        Assert.Null(Assert.Single(data.Zaehler).Bauform);
    }

    [Theory]
    [InlineData("na.kaskade", "Vielleicht", "Kaskadenschaltung hat einen unbekannten Wert")]
    [InlineData("na.anlage.art", "Abriss", "Neuerrichtung oder Erweiterung hat einen unbekannten Wert")]
    [InlineData("na.messkonzept", "Eigenes Konzept", "Messkonzept hat einen unbekannten Wert")]
    [InlineData("na.zaehler.anzahl", "viele", "Anzahl Zähler hat einen unbekannten Wert")]
    [InlineData("na.zaehler.anzahl", "5", "Anzahl Zähler hat einen unbekannten Wert")]
    [InlineData("na.zaehler.anzahl", "-1", "Anzahl Zähler hat einen unbekannten Wert")]
    [InlineData("na.zaehler1.typ", "Wandlermessung", "Zähler 1: Typ Zählerwechsel hat einen unbekannten Wert")]
    [InlineData("na.zaehler1.befestigung", "Hutschiene", "Zähler 1: Befestigung hat einen unbekannten Wert")]
    public void Unknown_choice_values_are_reported(string key, string value, string expected)
    {
        var items = Complete();
        items[key] = Radio("x", Key(key), value);

        var problems = Map(items).Problems;

        Assert.Contains(problems, p => p.Contains(expected));
        Assert.DoesNotContain(problems, p => p.Contains(value, StringComparison.OrdinalIgnoreCase) && value.Length > 2);
    }

    [Fact]
    public void Unknown_meter_type_does_not_add_the_meter()
    {
        var items = Complete();
        items[Keys.ZaehlerTyp(1)] = Radio("x", Key(Keys.ZaehlerTyp(1)), "Wandlermessung");

        Assert.Empty(Map(items).Zaehler);
    }

    [Fact]
    public void Messkonzept_is_stored_in_canonical_spelling()
    {
        var items = Complete();
        items[Keys.Messkonzept] = Radio("x", Key(Keys.Messkonzept), "ÜBERSCHUSSEINSPEISUNG - Zweirichtungszähler");

        var data = Map(items);

        Assert.Empty(data.Problems);
        Assert.Equal("Überschusseinspeisung – Zweirichtungszähler", data.Messkonzept);
    }

    [Fact]
    public void Attachment_that_is_neither_image_nor_pdf_is_reported()
    {
        var items = Complete();
        items[Keys.Schaltplan] = Image("Unterlagen",
            Request("Übersichtsschaltplan", Key(Keys.Schaltplan), "zip", "https://files.example/plan.zip"));

        var data = Map(items);

        Assert.Contains(data.Problems, p => p.Contains("Übersichtsschaltplan enthält eine Datei, die weder Bild noch PDF ist"));
        Assert.DoesNotContain(data.Dokumente, d => d.Kind == DocumentKind.Schaltplan);
    }

    [Fact]
    public void File_extension_is_taken_from_url()
    {
        var items = Complete();
        items[Keys.FotoHak] = Image("Fotos HAK",
            Request("Foto Hausanschlusskasten (HAK) mit Sicherungen", Key(Keys.FotoHak), "image", "https://files.example/hak.PNG?sig=1"));

        var hak = Assert.Single(Map(items).Dokumente, d => d.Kind == DocumentKind.FotoHausanschlusskasten);

        Assert.EndsWith(".png", hak.FileName);
    }

    [Fact]
    public void Messkonzept_sonstiges_requires_bemerkung()
    {
        var items = Complete();
        items[Keys.Messkonzept] = Radio("x", Key(Keys.Messkonzept), "Sonstiges (siehe Bemerkung)");

        Assert.Contains(Map(items).Problems, p => p.Contains("Bemerkung Messkonzept fehlt"));

        items[Keys.MesskonzeptBemerkung] = FreeText("x", Key(Keys.MesskonzeptBemerkung), "Zusätzlicher Unterzähler für Gewerbe");
        var data = Map(items);

        Assert.Empty(data.Problems);
        Assert.Equal("Zusätzlicher Unterzähler für Gewerbe", data.MesskonzeptBemerkung);
    }

    [Fact]
    public void All_messkonzept_options_are_accepted()
    {
        foreach (var option in NetzanmeldungChecklist.Messkonzepte)
        {
            var items = Complete();
            items[Keys.Messkonzept] = Radio("x", Key(Keys.Messkonzept), option);
            items[Keys.MesskonzeptBemerkung] = FreeText("x", Key(Keys.MesskonzeptBemerkung), "Hinweis");

            Assert.Empty(Map(items).Problems);
        }
    }

    [Fact]
    public void Problems_never_contain_values()
    {
        var items = Complete();
        items[Keys.ZaehlerAnzahl] = Radio("x", Key(Keys.ZaehlerAnzahl), "2");
        Set(items, Zaehler(1, "GEHEIMTYP-4711", "SECRET-ZAEHLER-0815"));
        items[Keys.ZaehlerNummer(2)] = FreeText("x", Key(Keys.ZaehlerNummer(2)), "SECRET-ZAEHLER-0816");
        items[Keys.MesskonzeptBemerkung] = FreeText("x", Key(Keys.MesskonzeptBemerkung), "SECRET-Bemerkung Familie Mustermann");
        items[Keys.Messkonzept] = Radio("x", Key(Keys.Messkonzept), "SECRET-Konzept");

        var problems = Map(items).Problems;

        Assert.NotEmpty(problems);
        Assert.DoesNotContain(problems, p => p.Contains("SECRET") || p.Contains("GEHEIM") || p.Contains("Mustermann"));
    }

    [Fact]
    public void Duplicate_keys_are_reported_once()
    {
        var items = Complete().Values.ToList();
        items.Add(Radio("x", Key(Keys.Kaskade), "Ja"));
        items.Add(Radio("y", Key(Keys.Kaskade), "Ja"));

        var data = ChecklistMapper.Map(ChecklistValues.Parse(Checklist(items), NetzanmeldungChecklist.TitleFallback));

        var problem = Assert.Single(data.Problems);
        Assert.Contains("na.kaskade", problem);
        Assert.Contains("mehrfach", problem);
        Assert.False(data.Kaskadenschaltung); // erstes Vorkommen („Nein“) gewinnt
    }
}
