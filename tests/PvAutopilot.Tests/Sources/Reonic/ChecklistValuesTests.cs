using PvAutopilot.Sources.Reonic.Api;
using PvAutopilot.Sources.Reonic.Checklist;
using static PvAutopilot.Tests.Sources.Reonic.ChecklistFactory;

namespace PvAutopilot.Tests.Sources.Reonic;

public class ChecklistValuesTests
{
    private static ChecklistValues Parse(params ReonicChecklistItem[] items) =>
        ChecklistValues.Parse(Checklist(items), NetzanmeldungChecklist.TitleFallback);

    [Theory]
    [InlineData("[key: na.kaskade]")]
    [InlineData("Hilfetext\n[key:na.kaskade]")]
    [InlineData("[key:   NA.Kaskade  ]")]
    [InlineData("Text davor [key: na.kaskade] Text danach")]
    public void Key_is_read_from_description(string description)
    {
        var values = Parse(Radio("Irgendein Titel", description, "Ja", "Ja", "Nein"));

        Assert.Equal("Ja", values.Choice("na.kaskade"));
    }

    [Fact]
    public void Key_prefix_is_case_insensitive()
    {
        var values = Parse(Radio("Irgendein Titel", "[Key: na.kaskade]", "Ja", "Ja", "Nein"));

        Assert.Equal("Ja", values.Choice("na.kaskade"));
    }

    [Fact]
    public void Explicit_key_wins_over_earlier_title_match()
    {
        // Ein Feld „Kaskadenschaltung“ ohne Key in einem anderen Block steht vor dem eigentlichen Feld.
        var values = Parse(
            Radio("Kaskadenschaltung", null, "Nein", "Ja", "Nein"),
            Radio("Irgendein Titel", "[key: na.kaskade]", "Ja", "Ja", "Nein"));

        Assert.Equal("Ja", values.Choice("na.kaskade"));
        Assert.Empty(values.DuplicateKeys);
    }

    [Fact]
    public void Title_fallback_is_used_without_key()
    {
        var values = Parse(Radio("  kaskadenSCHALTUNG ", null, "Nein", "Ja", "Nein"));

        Assert.Equal("Nein", values.Choice("na.kaskade"));
    }

    [Fact]
    public void Key_in_description_wins_over_title()
    {
        var values = Parse(Radio("Kaskadenschaltung", "[key: na.anlage.art]", "Neuerrichtung"));

        Assert.Equal("Neuerrichtung", values.Choice("na.anlage.art"));
        Assert.Null(values.Choice("na.kaskade"));
    }

    [Fact]
    public void Choice_is_returned_trimmed_but_otherwise_raw()
    {
        var values = Parse(Radio("Zähler 1: Typ Zählerwechsel", null, "  Ausbau - Zähler abmelden "));

        Assert.Equal("Ausbau - Zähler abmelden", values.Choice("na.zaehler1.typ"));
    }

    [Theory]
    [InlineData("Ausbau – Zähler abmelden", "ausbau - zähler abmelden")]
    [InlineData("  A   B\tC ", "a b c")]
    [InlineData("x—y‐z", "x-y-z")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalize(string? input, string expected)
    {
        Assert.Equal(expected, ChecklistText.Normalize(input));
    }

    [Fact]
    public void Radio_without_checked_option_has_no_value()
    {
        var values = Parse(Radio("Kaskadenschaltung", Key("na.kaskade"), null, "Ja", "Nein"));

        Assert.Null(values.Choice("na.kaskade"));
        Assert.True(values.Contains("na.kaskade"));
    }

    [Fact]
    public void Freetext_is_trimmed_and_empty_is_null()
    {
        var values = Parse(
            FreeText("a", Key("na.zaehler1.nummer"), "  1ESY0001  "),
            FreeText("b", Key("na.zaehler2.nummer"), "   "),
            FreeText("c", Key("na.zaehler3.nummer"), null));

        Assert.Equal("1ESY0001", values.Text("na.zaehler1.nummer"));
        Assert.Null(values.Text("na.zaehler2.nummer"));
        Assert.Null(values.Text("na.zaehler3.nummer"));
    }

    [Fact]
    public void Image_requests_each_have_their_own_key()
    {
        var values = Parse(Image("Fotos Zähleranlage",
            Request("a", Key("na.foto.hauptzaehler"), "image", "https://files.example/1.jpg"),
            Request("b", Key("na.foto.zaehlerschrank"), "image", "https://files.example/2.jpg", "https://files.example/3.jpg"),
            Request("Foto Hausanschlusskasten (HAK) mit Sicherungen", null, "image", "https://files.example/4.jpg")));

        Assert.Single(values.Attachments("na.foto.hauptzaehler"));
        Assert.Equal(2, values.Attachments("na.foto.zaehlerschrank").Count);
        Assert.Equal("https://files.example/4.jpg", Assert.Single(values.Attachments("na.foto.hak")).Url);
    }

    [Fact]
    public void Image_attachments_without_url_are_ignored()
    {
        var request = new ReonicChecklistSubItem(null, false, "x", Key("na.schaltplan"), false,
            [new ReonicAttachment("pdf", null), new ReonicAttachment("pdf", " ")]);

        var values = Parse(Image("Unterlagen", request));

        Assert.Empty(values.Attachments("na.schaltplan"));
    }

    [Fact]
    public void Marked_irrelevant_fields_count_as_empty()
    {
        var radio = Radio("x", Key("na.kaskade"), "Ja", "Ja", "Nein") with { MarkedIrrelevant = true };
        var text = FreeText("x", Key("na.zaehler1.nummer"), "1ESY") with { MarkedIrrelevant = true };
        var wholeImage = Image("x", Request("x", Key("na.foto.hak"), "image", "https://files.example/1.jpg")) with { MarkedIrrelevant = true };
        var singleRequest = Image("y",
            Request("y", Key("na.foto.hauptzaehler"), "image", "https://files.example/2.jpg") with { MarkedIrrelevant = true },
            Request("z", Key("na.foto.zaehlerschrank"), "image", "https://files.example/3.jpg"));

        var values = Parse(radio, text, wholeImage, singleRequest);

        Assert.Null(values.Choice("na.kaskade"));
        Assert.Null(values.Text("na.zaehler1.nummer"));
        Assert.Empty(values.Attachments("na.foto.hak"));
        Assert.Empty(values.Attachments("na.foto.hauptzaehler"));
        Assert.Single(values.Attachments("na.foto.zaehlerschrank"));
    }

    [Fact]
    public void Duplicate_keys_first_wins_and_are_reported()
    {
        var values = Parse(
            Radio("x", Key("na.kaskade"), "Ja", "Ja", "Nein"),
            Radio("y", Key("na.kaskade"), "Nein", "Ja", "Nein"));

        Assert.Equal("Ja", values.Choice("na.kaskade"));
        Assert.Equal(["na.kaskade"], values.DuplicateKeys);
    }

    [Fact]
    public void Foreign_fields_and_display_items_are_ignored()
    {
        var values = Parse(
            Radio("Dachneigung", "Hilfetext ohne Schlüssel", "30°", "30°", "45°"),
            FreeText("Bemerkung Dach", null, "steil"),
            new ReonicChecklistItem("title", "Überschrift", null, null, false, null),
            new ReonicChecklistItem("circuitPlan", "Schaltplan", null, null, false, null),
            new ReonicChecklistItem("checkbox", "Kaskadenschaltung", null, null, false,
                [new ReonicChecklistSubItem("Ja", true, null, null, false, null)]));

        Assert.False(values.Contains("na.kaskade"));
        Assert.Empty(values.DuplicateKeys);
    }

    [Fact]
    public void Fields_from_all_blocks_and_segments_are_read()
    {
        var checklist = new ReonicChecklist("offer",
        [
            new ReonicChecklistBlock("b1", "Allgemein", [new ReonicChecklistSegment("s1", "A", null, [Radio("x", Key("na.kaskade"), "Ja")])]),
            new ReonicChecklistBlock("b2", "Netzanmeldung",
            [
                new ReonicChecklistSegment("s2", "B", null, []),
                new ReonicChecklistSegment("s3", "C", DateTimeOffset.UnixEpoch, [Radio("y", Key("na.anlage.art"), "Neuerrichtung")]),
            ]),
        ]);

        var values = ChecklistValues.Parse(checklist, NetzanmeldungChecklist.TitleFallback);

        Assert.Equal("Ja", values.Choice("na.kaskade"));
        Assert.Equal("Neuerrichtung", values.Choice("na.anlage.art"));
    }
}
