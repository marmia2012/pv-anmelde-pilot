using Microsoft.Extensions.Logging;
using PdfSharp.Pdf;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;
using PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms;
using static PvAutopilot.Tests.Netzbetreiber.NetzeDuisburgFixtures;
using F = PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms.NetzeDuisburgFormFields;

namespace PvAutopilot.Tests.Netzbetreiber;

/// <summary>Verhalten nach den Review-Fixes: stabiles Antragsdatum, MK 8, KeinWechsel, Maßnahme, /AP, MaxLen.</summary>
public class NetzeDuisburgReviewFixTests
{
    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, TimeSpan.Zero);

    private static readonly string Mk8Konzept = "Überschuss + separater Zähler steuerbare Verbrauchseinrichtung (§ 14a)";

    private static async Task<string?> DatumOrt(AnmeldeProcess process)
    {
        var package = await Adapter().PrepareAsync(SubmissionKind.Netzanmeldung, process, CancellationToken.None);
        return PdfReadback.Of(package.Documents[0].Content).Text(F.E1.DatumOrt);
    }

    private static AnmeldeProcess ProcessUntilResolved(DateTimeOffset resolvedAt)
    {
        var process = Process(Card());
        process.TransitionTo(ProcessState.DataCardAggregated, At(4, 8));
        process.TransitionTo(ProcessState.NetzbetreiberResolved, resolvedAt);
        return process;
    }

    // ---- 1. Antragsdatum ----

    [Fact]
    public async Task Antragsdatum_is_first_transition_into_NetzbetreiberResolved()
    {
        var process = ProcessUntilResolved(At(10, 9));
        process.TransitionTo(ProcessState.NetzanmeldungPrepared, At(20, 9)); // später, darf nicht zählen

        Assert.Equal("Duisburg, 10.11.2026", await DatumOrt(process));
    }

    [Fact]
    public async Task Antragsdatum_is_unchanged_by_resume_after_block()
    {
        var process = ProcessUntilResolved(At(10, 9));
        process.Block("technischer Fehler", At(12, 9));
        process.Resume(At(13, 9)); // Resume landet wieder in NetzbetreiberResolved, ist aber kein neuer Eintritt

        Assert.Equal(ProcessState.NetzbetreiberResolved, process.State);
        Assert.Equal("Duisburg, 10.11.2026", await DatumOrt(process));
    }

    [Fact]
    public async Task Antragsdatum_without_transition_is_creation_date()
    {
        var process = Process(Card()); // angelegt TestData.Now = 03.11.2026

        Assert.Equal("Duisburg, 03.11.2026", await DatumOrt(process));
    }

    [Fact]
    public async Task Antragsdatum_uses_berlin_calendar_day()
    {
        var process = ProcessUntilResolved(At(10, 23, 30)); // 23:30Z = 00:30 MEZ am 11.11.

        Assert.Equal("Duisburg, 11.11.2026", await DatumOrt(process));
    }

    [Fact]
    public async Task Antragsdatum_only_counts_the_current_revision()
    {
        var process = ProcessUntilResolved(At(10, 9));
        process.TransitionTo(ProcessState.NetzanmeldungPrepared, At(10, 10));
        process.TransitionTo(ProcessState.NetzanmeldungSubmitted, At(10, 11));
        process.TransitionTo(ProcessState.RueckfrageOpen, At(12, 9));
        process.StartNewRevision(At(14, 9));
        process.AttachDataCard(Card(), At(14, 9));
        process.TransitionTo(ProcessState.DataCardAggregated, At(14, 10));
        process.TransitionTo(ProcessState.NetzbetreiberResolved, At(15, 10));

        Assert.Equal("Duisburg, 15.11.2026", await DatumOrt(process));
    }

    [Fact]
    public async Task Antragsdatum_is_identical_on_repeated_preparation()
    {
        var process = ProcessUntilResolved(At(10, 9));
        var adapter = Adapter();

        var first = await adapter.PrepareAsync(SubmissionKind.Netzanmeldung, process, CancellationToken.None);
        var second = await adapter.PrepareAsync(SubmissionKind.Netzanmeldung, process, CancellationToken.None);

        Assert.Equal(
            PdfReadback.Of(first.Documents[0].Content).Text(F.E1.DatumOrt),
            PdfReadback.Of(second.Documents[0].Content).Text(F.E1.DatumOrt));
    }

    // ---- 2. MK 8 ----

    [Fact]
    public void Mk8_additional_meter_has_no_meter_type_usage_or_peak_but_main_meter_does()
    {
        var card = Card(zaehler:
        [
            Zaehler(ZaehlerwechselTyp.TauschZweirichtungszaehler, Zaehlerbauform.Ehz),
            Zaehler(ZaehlerwechselTyp.Neusetzung, Zaehlerbauform.DreiPunkt, null),
        ]) with { Messkonzept = Mk8Konzept };

        var docs = Generator().CreateZaehlerantraege(card);

        Assert.Equal(2, docs.Count);
        var haupt = PdfReadback.Of(docs[0].Content);
        Assert.Equal("4", haupt.Radio(F.Zaehlerantrag.Auftrag));
        Assert.Equal("8", haupt.Radio(F.Zaehlerantrag.Zaehler));
        Assert.Equal("6", haupt.Radio(F.Zaehlerantrag.Nutzungsart));
        Assert.Equal("9", haupt.Text(F.Zaehlerantrag.EegPeakleistung));

        var zusatz = PdfReadback.Of(docs[1].Content);
        Assert.Equal("2", zusatz.Radio(F.Zaehlerantrag.Auftrag)); // Zählereinbau
        Assert.Null(zusatz.Radio(F.Zaehlerantrag.Zaehler));
        Assert.Null(zusatz.Radio(F.Zaehlerantrag.Nutzungsart));
        Assert.Null(zusatz.Text(F.Zaehlerantrag.EegPeakleistung));
        Assert.Equal("1", zusatz.Radio(F.Zaehlerantrag.Messebene));
    }

    [Fact]
    public void Neusetzung_outside_mk8_keeps_meter_type_usage_and_peak()
    {
        var card = Card(zaehler: [Zaehler(ZaehlerwechselTyp.Neusetzung, Zaehlerbauform.DreiPunkt, null)])
            with { Messkonzept = "Volleinspeisung – Erzeugungszähler" };

        var antrag = PdfReadback.Of(Generator().CreateZaehlerantraege(card)[0].Content);

        Assert.Equal("2", antrag.Radio(F.Zaehlerantrag.Zaehler));
        Assert.Equal("6", antrag.Radio(F.Zaehlerantrag.Nutzungsart));
        Assert.Equal("9", antrag.Text(F.Zaehlerantrag.EegPeakleistung));
    }

    // ---- 3. KeinWechsel ----

    [Fact]
    public void KeinWechsel_only_yields_one_completion_notice_with_main_meter_number()
    {
        var card = Card(zaehler: [Zaehler(ZaehlerwechselTyp.KeinWechsel, null, null)]);

        var docs = Generator().CreateZaehlerantraege(card);

        var antrag = Assert.Single(docs);
        Assert.Equal("Zaehlerantrag_1.pdf", antrag.FileName);
        var read = PdfReadback.Of(antrag.Content);
        Assert.Equal("1", read.Radio(F.Zaehlerantrag.Auftrag));
        Assert.Equal($"Hauptzähler {Hauptzaehlernummer}", read.Text(F.Zaehlerantrag.BemerkungenInstallateur));
    }

    [Fact]
    public void Empty_meter_list_also_yields_one_completion_notice()
    {
        var docs = Generator().CreateZaehlerantraege(Card(zaehler: []));

        Assert.Equal("1", PdfReadback.Of(Assert.Single(docs).Content).Radio(F.Zaehlerantrag.Auftrag));
    }

    [Fact]
    public void KeinWechsel_entries_next_to_changed_meters_create_no_own_application()
    {
        var card = Card(zaehler:
        [
            Zaehler(ZaehlerwechselTyp.KeinWechsel, null, null),
            Zaehler(ZaehlerwechselTyp.TauschZweirichtungszaehler),
            Zaehler(ZaehlerwechselTyp.KeinWechsel, null, null),
        ]);

        var docs = Generator().CreateZaehlerantraege(card);

        var antrag = Assert.Single(docs);
        var read = PdfReadback.Of(antrag.Content);
        Assert.Equal("4", read.Radio(F.Zaehlerantrag.Auftrag));
        Assert.Equal(Zaehlernummer, read.Text(F.Zaehlerantrag.ZaehlernummerWechsel));
        Assert.Null(read.Text(F.Zaehlerantrag.BemerkungenInstallateur));
    }

    [Fact]
    public void Changed_meters_are_numbered_consecutively()
    {
        var card = Card(zaehler:
        [
            Zaehler(ZaehlerwechselTyp.KeinWechsel, null, null),
            Zaehler(ZaehlerwechselTyp.TauschZweirichtungszaehler),
            Zaehler(ZaehlerwechselTyp.Ausbau, null, "1EMH0077777777"),
        ]);

        Assert.Equal(["Zaehlerantrag_1.pdf", "Zaehlerantrag_2.pdf"], Generator().CreateZaehlerantraege(card).Select(d => d.FileName));
    }

    [Fact]
    public async Task Inbetriebnahme_package_with_KeinWechsel_has_E8_and_one_application_and_validates()
    {
        var card = Card(zaehler: [Zaehler(ZaehlerwechselTyp.KeinWechsel, null, null)]);
        var adapter = Adapter();

        Assert.Empty(adapter.Validate(SubmissionKind.Inbetriebnahme, card));
        var package = await adapter.PrepareAsync(SubmissionKind.Inbetriebnahme, Process(card, Heute), CancellationToken.None);

        Assert.Equal(["E8_Inbetriebsetzungsprotokoll.pdf", "Zaehlerantrag_1.pdf"], package.Documents.Select(d => d.FileName));
    }

    [Fact]
    public void Inbetriebnahme_validation_without_any_meter_has_no_problems()
    {
        Assert.Empty(Adapter().Validate(SubmissionKind.Inbetriebnahme, Card(zaehler: [])));
    }

    // ---- 4. Maßnahme ----

    private static PdfReadback Single(NabDataCard card) => PdfReadback.Of(Generator().CreateZaehlerantraege(card)[0].Content);

    [Fact]
    public void Removal_is_measure_7_without_usage_or_peak()
    {
        var antrag = Single(Card(zaehler: [Zaehler(ZaehlerwechselTyp.Ausbau, null)]));

        Assert.Equal("3", antrag.Radio(F.Zaehlerantrag.Auftrag));
        Assert.Equal("7", antrag.Radio(F.Zaehlerantrag.Massnahme));
        Assert.Null(antrag.Radio(F.Zaehlerantrag.Nutzungsart));
        Assert.Null(antrag.Text(F.Zaehlerantrag.EegPeakleistung));
    }

    [Fact]
    public void Extension_is_measure_6_and_new_installation_is_measure_5()
    {
        var erweiterung = Single(Card() with { Installationsart = Installationsart.Erweiterung });
        var neu = Single(Card() with { Installationsart = Installationsart.Neuerrichtung });

        Assert.Equal("6", erweiterung.Radio(F.Zaehlerantrag.Massnahme));
        Assert.Equal("5", neu.Radio(F.Zaehlerantrag.Massnahme));
        Assert.Equal("6", erweiterung.Radio(F.Zaehlerantrag.Nutzungsart));
    }

    [Fact]
    public void Removal_in_an_extension_is_still_measure_7()
    {
        var antrag = Single(Card(zaehler: [Zaehler(ZaehlerwechselTyp.Ausbau, null)]) with { Installationsart = Installationsart.Erweiterung });

        Assert.Equal("7", antrag.Radio(F.Zaehlerantrag.Massnahme));
    }

    // ---- 5. /AP ----

    private static byte[] TemplateBytes(NetzeDuisburgForm form)
    {
        using var stream = NetzeDuisburgTemplates.Open(form);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Set_text_fields_lose_their_appearance_and_unset_fields_keep_it()
    {
        var template = TemplateBytes(NetzeDuisburgForm.Zaehlerantrag);
        // Ausgangslage: die Vorlage liefert für beide Felder eine Darstellung (sonst wäre der Test leer).
        Assert.True(PdfAppearance.Has(template, F.Zaehlerantrag.Grundstueckseigentuemer));
        Assert.True(PdfAppearance.Has(template, F.Zaehlerantrag.StandortStrasse));

        using var stream = NetzeDuisburgTemplates.Open(NetzeDuisburgForm.Zaehlerantrag);
        using var filler = PdfFormFiller.Open("Zaehlerantrag", stream);
        filler.SetText(F.Zaehlerantrag.Grundstueckseigentuemer, "Jürgen Müller-Lüdenscheidt");
        var saved = filler.Save();

        Assert.False(PdfAppearance.Has(saved, F.Zaehlerantrag.Grundstueckseigentuemer));
        Assert.True(PdfAppearance.Has(saved, F.Zaehlerantrag.StandortStrasse));
    }

    // ---- 6. MaxLen ----

    /// <summary>Synthetisches Formular: Feld "D" mit eigenem /MaxLen 5, Feld "P.c" erbt /MaxLen 4 vom Elternfeld.</summary>
    private static byte[] MaxLenPdf()
    {
        using var doc = new PdfDocument();
        doc.AddPage();

        PdfDictionary Add(PdfDictionary dict) { doc.Internals.AddObject(dict); return dict; }
        PdfDictionary Text(string name)
        {
            var d = new PdfDictionary(doc);
            d.Elements["/Type"] = new PdfName("/Annot");
            d.Elements["/Subtype"] = new PdfName("/Widget");
            d.Elements["/FT"] = new PdfName("/Tx");
            d.Elements["/T"] = new PdfString(name);
            return Add(d);
        }

        var direct = Text("D");
        direct.Elements["/MaxLen"] = new PdfInteger(5);
        var child = Text("c");
        var parent = new PdfDictionary(doc);
        parent.Elements["/T"] = new PdfString("P");
        parent.Elements["/MaxLen"] = new PdfInteger(4);
        Add(parent);
        var kids = new PdfArray(doc);
        kids.Elements.Add(child.Reference!);
        parent.Elements["/Kids"] = kids;
        child.Elements["/Parent"] = parent.Reference!;

        var fields = new PdfArray(doc);
        fields.Elements.Add(direct.Reference!);
        fields.Elements.Add(parent.Reference!);
        var acro = new PdfDictionary(doc);
        acro.Elements["/Fields"] = fields;
        doc.Internals.Catalog.Elements["/AcroForm"] = acro;

        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    [Fact]
    public void Too_long_text_is_truncated_to_MaxLen_own_and_inherited()
    {
        using var stream = new MemoryStream(MaxLenPdf());
        using var filler = PdfFormFiller.Open("Test", stream);

        filler.SetText("D", "0123456789");
        filler.SetText("P.c", "abcdefghij");
        var read = PdfReadback.Of(filler.Save());

        Assert.Equal("01234", read.Text("D"));
        Assert.Equal("abcd", read.Text("P.c"));
        Assert.Equal(2, filler.TruncatedCount);
    }

    [Fact]
    public void Text_within_MaxLen_is_not_truncated_or_counted()
    {
        using var stream = new MemoryStream(MaxLenPdf());
        using var filler = PdfFormFiller.Open("Test", stream);

        filler.SetText("D", "01234");
        filler.SetText("P.c", "ab");

        Assert.Equal(0, filler.TruncatedCount);
        Assert.Equal(2, filler.SetCount);
    }

    [Fact]
    public void Truncation_log_contains_count_but_not_the_content()
    {
        // Die Vorlagen kennen /MaxLen nicht in jedem Feld; ein 5000 Zeichen langer Name trifft sicher eines mit Grenze,
        // falls die Vorlage eine setzt. Unabhängig davon muss die Logzeile die Anzahl führen und nie den Inhalt.
        var log = new CapturingLogger<NetzeDuisburgFormGenerator>();
        var card = Card() with
        {
            Anlagenbetreiber = new Person("Herr", "Wunibald", new string('Q', 5000), Email, Telefon, null),
        };

        Generator(log).CreateE1(card, [], Heute);

        var entry = Assert.Single(log.Entries, e => e.Message.Contains("Formular erzeugt"));
        Assert.Contains("gekürzt", entry.Message);
        Assert.Contains("GekuerzteFelder=", entry.Message);
        Assert.DoesNotContain("QQQ", entry.Message);
        Assert.DoesNotContain("Wunibald", entry.Message);
        Assert.Equal(LogLevel.Information, entry.Level);
    }
}
