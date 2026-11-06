using PvAutopilot.Core.DataCard;
using PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms;
using static PvAutopilot.Tests.Netzbetreiber.NetzeDuisburgFixtures;
using F = PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms.NetzeDuisburgFormFields;

namespace PvAutopilot.Tests.Netzbetreiber;

public class NetzeDuisburgFormGeneratorTests
{
    private static readonly NetzeDuisburgFormGenerator Gen = Generator();

    private static PdfReadback E1(NabDataCard? card = null, params DocumentKind[] attachments) =>
        PdfReadback.Of(Gen.CreateE1(card ?? Card(), attachments, Heute).Content);

    private static PdfReadback E2(NabDataCard? card = null) => PdfReadback.Of(Gen.CreateE2(card ?? Card()).Content);

    private static PdfReadback E3(NabDataCard? card = null, params DocumentKind[] attachments) =>
        PdfReadback.Of(Gen.CreateE3(card ?? Card(speicher: Speicher), attachments).Content);

    private static PdfReadback E8(NabDataCard? card = null) =>
        PdfReadback.Of(Gen.CreateE8(card ?? Card(), new DateOnly(2027, 3, 20)).Content);

    private static PdfReadback Antrag(Zaehler zaehler, NabDataCard? card = null) =>
        PdfReadback.Of(Gen.CreateZaehlerantrag(card ?? Card(), zaehler, 1).Content);

    private static WechselrichterPosition Wr(string hersteller, string modell, decimal w, decimal anzahl, int? phasen = 3) =>
        new(hersteller, modell, null, w, anzahl, 8m, phasen);

    // ---- E.1 ----

    [Fact]
    public void E1_takes_address_operator_and_installer_from_data_card()
    {
        var e1 = E1();

        Assert.Equal("Jürgen Müller-Lüdenscheidt", e1.Text(F.E1.AnlageName));
        Assert.Equal("Äußere Gärtnerstraße 12b", e1.Text(F.E1.AnlageStrasse));
        Assert.Equal("47228 Duisburg", e1.Text(F.E1.AnlagePlzOrt));
        Assert.Equal($"{Telefon}, {Email}", e1.Text(F.E1.AnlageKontakt));
        Assert.Equal("Jürgen Müller-Lüdenscheidt", e1.Text(F.E1.BetreiberName));
        Assert.Equal("Äußere Gärtnerstraße 12b", e1.Text(F.E1.BetreiberStrasse));
        Assert.Equal("Jürgen Müller-Lüdenscheidt", e1.Text(F.E1.AnschlussnehmerName));
        Assert.Equal("Sonnenschein Elektro GmbH", e1.Text(F.E1.ErrichterFirma));
        Assert.Equal("Mülheim", e1.Text(F.E1.ErrichterOrt));
        Assert.Equal("E-4711", e1.Text(F.E1.ErrichterEintragungsnummer));
    }

    [Fact]
    public void E1_new_installation_versus_extension()
    {
        var neu = E1(Card());
        var erweitert = E1(Card() with { Installationsart = Installationsart.Erweiterung });

        Assert.True(neu.IsChecked(F.E1.AnlagenartNeuerrichtung));
        Assert.False(neu.IsChecked(F.E1.AnlagenartErweiterung));
        Assert.False(erweitert.IsChecked(F.E1.AnlagenartNeuerrichtung));
        Assert.True(erweitert.IsChecked(F.E1.AnlagenartErweiterung));
        Assert.False(neu.IsChecked(F.E1.AnlagenartRueckbau));
    }

    [Fact]
    public void E1_attachment_checkboxes_only_when_attachment_present()
    {
        var ohne = E1();
        Assert.True(ohne.IsChecked(F.E1.DatenblattBeigefuegt)); // E.2 wird immer erzeugt
        Assert.False(ohne.IsChecked(F.E1.LageplanBeigefuegt));
        Assert.False(ohne.IsChecked(F.E1.SchaltplanBeigefuegt));
        Assert.False(ohne.IsChecked(F.E1.EinheitenzertifikateLiegenVor));
        Assert.False(ohne.IsChecked(F.E1.NaSchutzZertifikatBeigefuegt));

        var mit = E1(null, DocumentKind.Lageplan, DocumentKind.Schaltplan, DocumentKind.Einheitenzertifikat, DocumentKind.NaSchutzZertifikat);
        Assert.True(mit.IsChecked(F.E1.LageplanBeigefuegt));
        Assert.True(mit.IsChecked(F.E1.SchaltplanBeigefuegt));
        Assert.True(mit.IsChecked(F.E1.EinheitenzertifikateLiegenVor));
        Assert.True(mit.IsChecked(F.E1.NaSchutzZertifikatBeigefuegt));
    }

    [Fact]
    public void E1_lageplan_only_checked_for_lageplan_not_other_documents()
    {
        var e1 = E1(null, DocumentKind.Schaltplan, DocumentKind.Datenblatt, DocumentKind.FotoHauptzaehler);

        Assert.False(e1.IsChecked(F.E1.LageplanBeigefuegt));
    }

    [Fact]
    public void E1_planned_commissioning_date_and_blank_signature()
    {
        var e1 = E1();
        Assert.Equal("15.03.2027", e1.Text(F.E1.GeplanterInbetriebsetzungstermin));
        Assert.Equal("Duisburg, 06.11.2026", e1.Text(F.E1.DatumOrt));
        Assert.Null(e1.Text(F.E1.Unterschrift));

        Assert.Null(E1(Card() with { Installationstermin = null }).Text(F.E1.GeplanterInbetriebsetzungstermin));
    }

    // ---- E.2 ----

    [Fact]
    public void E2_first_inverter_in_fields_and_power_is_sum()
    {
        var card = Card() with
        {
            Wechselrichter = [Wr("Hersteller B", "WR 8K", 8000, 1), Wr("Hersteller C", "WR 5K", 5000, 2)],
        };
        var e2 = E2(card);

        Assert.Equal("Hersteller B", e2.Text(F.E2.EinheitenHersteller));
        Assert.Equal("WR 8K", e2.Text(F.E2.EinheitenTyp));
        Assert.Equal("1", e2.Text(F.E2.EinheitenAnzahl));
        Assert.Equal("18", e2.Text(F.E2.MaxWirkleistung));
        Assert.Equal("Jürgen Müller-Lüdenscheidt", e2.Text(F.E2.AnlageName));
        Assert.True(e2.IsChecked(F.E2.EnergieartSonne));
    }

    [Fact]
    public void E2_further_inverter_types_go_to_remarks_after_measurement_concept()
    {
        var card = Card() with
        {
            Messkonzept = "Überschusseinspeisung – Zweirichtungszähler",
            Wechselrichter =
            [
                Wr("Hersteller B", "WR 8K", 8000, 1), Wr("Hersteller C", "WR 5K", 5000, 2),
                Wr("Hersteller D", "WR 3K", 3000, 1), Wr("Hersteller E", "WR 2K", 2000, 1),
            ],
        };
        var e2 = E2(card);

        Assert.Equal("Messkonzept MK 2 gem. Netze-Duisburg-Messkonzepte 2026", e2.Text(F.E2.Bemerkungen[0]));
        Assert.Equal("Weitere Erzeugungseinheit: Hersteller C WR 5K, Anzahl 2", e2.Text(F.E2.Bemerkungen[1]));
        Assert.Equal("Weitere Erzeugungseinheit: Hersteller D WR 3K, Anzahl 1", e2.Text(F.E2.Bemerkungen[2]));
        Assert.Equal("Weitere Erzeugungseinheit: Hersteller E WR 2K, Anzahl 1", e2.Text(F.E2.Bemerkungen[3]));
    }

    [Fact]
    public void E2_overflowing_remarks_are_joined_in_last_line()
    {
        var card = Card() with
        {
            Messkonzept = "Volleinspeisung – Erzeugungszähler",
            Wechselrichter = Enumerable.Range(1, 6).Select(i => Wr($"H{i}", $"T{i}", 1000, 1)).ToList(),
        };
        var e2 = E2(card);

        Assert.Equal("Messkonzept MK 1 gem. Netze-Duisburg-Messkonzepte 2026", e2.Text(F.E2.Bemerkungen[0]));
        var last = e2.Text(F.E2.Bemerkungen[3])!;
        Assert.Contains("H4 T4", last);
        Assert.Contains("H6 T6", last);
        Assert.Contains("; ", last);
    }

    [Theory]
    [InlineData("Volleinspeisung – Erzeugungszähler", "Messkonzept MK 1 gem. Netze-Duisburg-Messkonzepte 2026")]
    [InlineData("Kaskade – Zweirichtungs- + Erzeugungszähler", "Messkonzept MK 3 gem. Netze-Duisburg-Messkonzepte 2026")]
    [InlineData("Überschuss + separater Zähler steuerbare Verbrauchseinrichtung (§ 14a)", "Messkonzept MK 8 gem. Netze-Duisburg-Messkonzepte 2026")]
    [InlineData("Etwas Neues", "Messkonzept: Etwas Neues")]
    public void E2_measurement_concept_text(string messkonzept, string expected)
    {
        var e2 = E2(Card() with { Messkonzept = messkonzept });

        Assert.Equal(expected, e2.Text(F.E2.Bemerkungen[0]));
    }

    [Fact]
    public void E2_other_measurement_concept_uses_free_text_and_none_leaves_remarks_empty()
    {
        var sonstiges = E2(Card() with { Messkonzept = "Sonstiges (siehe Bemerkung)", MesskonzeptBemerkung = "Sonderfall Ä" });
        Assert.Equal("Messkonzept: Sonderfall Ä", sonstiges.Text(F.E2.Bemerkungen[0]));

        Assert.Null(E2().Text(F.E2.Bemerkungen[0]));
    }

    [Fact]
    public void E2_partial_versus_full_feed_in()
    {
        var teil = E2(Card() with { Einspeiseart = Einspeiseart.Teileinspeisung });
        Assert.True(teil.IsChecked(F.E2.UeberschusseinspeisungJa));
        Assert.False(teil.IsChecked(F.E2.UeberschusseinspeisungNein));
        Assert.False(teil.IsChecked(F.E2.VolleinspeisungJa));
        Assert.True(teil.IsChecked(F.E2.VolleinspeisungNein));

        var voll = E2(Card() with { Einspeiseart = Einspeiseart.Volleinspeisung });
        Assert.False(voll.IsChecked(F.E2.UeberschusseinspeisungJa));
        Assert.True(voll.IsChecked(F.E2.UeberschusseinspeisungNein));
        Assert.True(voll.IsChecked(F.E2.VolleinspeisungJa));
        Assert.False(voll.IsChecked(F.E2.VolleinspeisungNein));
    }

    [Fact]
    public void E2_fixed_values_and_intentionally_empty_fields()
    {
        var e2 = E2();

        Assert.True(e2.IsChecked(F.E2.InselbetriebJa));
        Assert.False(e2.IsChecked(F.E2.InselbetriebNein));
        Assert.True(e2.IsChecked(F.E2.MotorischerAnlaufNein));
        Assert.False(e2.IsChecked(F.E2.MotorischerAnlaufJa));
        Assert.True(e2.IsChecked(F.E2.BlindleistungskompensationNichtVorhanden));
        Assert.Null(e2.Text(F.E2.MaxScheinleistung)); // Datenkarte hat 8 kVA, trotzdem leer (Datenblattwert)
        Assert.Null(e2.Text(F.E2.BlindleistungskompensationStufen));
        Assert.False(e2.IsChecked(F.E2.EnergieartWind));
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(2, "2")]
    [InlineData(3, "D")]
    public void E2_phases_follow_inverter_phases(int phasen, string expected)
    {
        var card = Card() with { Wechselrichter = [Wr("H", "T", 5000, 1, phasen)] };
        var e2 = E2(card);

        Assert.Equal(expected == "1", e2.IsChecked(F.E2.Netzeinspeisung1Phasig));
        Assert.Equal(expected == "2", e2.IsChecked(F.E2.Netzeinspeisung2Phasig));
        Assert.Equal(expected == "D", e2.IsChecked(F.E2.NetzeinspeisungDrehstrom));
        Assert.False(e2.IsChecked(F.E2.Netzeinspeisung3Phasig));
    }

    [Fact]
    public void E2_phases_left_empty_when_unknown_or_mixed()
    {
        foreach (var wr in new[] { new[] { Wr("H", "T", 5000, 1, null) }, new[] { Wr("H", "T", 5000, 1, 1), Wr("H", "U", 5000, 1, 3) } })
        {
            var e2 = E2(Card() with { Wechselrichter = wr });
            Assert.False(e2.IsChecked(F.E2.Netzeinspeisung1Phasig));
            Assert.False(e2.IsChecked(F.E2.NetzeinspeisungDrehstrom));
        }
    }

    [Fact]
    public void E2_number_format_is_german()
    {
        var e2 = E2(Card() with { Wechselrichter = [Wr("H", "T", 8500, 1)] });

        Assert.Equal("8,5", e2.Text(F.E2.MaxWirkleistung));
    }

    // ---- E.3 ----

    [Fact]
    public void E3_requires_storage()
    {
        Assert.Throws<InvalidOperationException>(() => Gen.CreateE3(Card(), []));
    }

    [Fact]
    public void E3_fixed_values()
    {
        var e3 = E3();

        Assert.Equal("8", e3.Text(F.E3.Anschlusskonzept));
        Assert.True(e3.IsChecked(F.E3.AllpoligeTrennung));
        Assert.True(e3.IsChecked(F.E3.NaSchutzVorhanden));
        Assert.True(e3.IsChecked(F.E3.FerngesteuertNein));
        Assert.False(e3.IsChecked(F.E3.FerngesteuertJa));
        Assert.Equal("60", e3.Text(F.E3.DauerhaftBegrenztProzent));
        Assert.True(e3.IsChecked(F.E3.DauerhaftBegrenztJa));
        Assert.Equal("Sonne", e3.Text(F.E3.Primaerenergietraeger));
        Assert.True(e3.IsChecked(F.E3.SpeicherNichtVomNetz));
    }

    [Fact]
    public void E3_storage_errichter_and_address()
    {
        var e3 = E3();

        Assert.Equal("Speicher GmbH Akku 10", e3.Text(F.E3.SpeicherHerstellerTyp));
        Assert.Equal("1", e3.Text(F.E3.SpeicherAnzahl));
        Assert.Equal("Jürgen Müller-Lüdenscheidt", e3.Text(F.E3.AnlageName));
        Assert.Equal("Sonnenschein Elektro GmbH, Mülheim", e3.Text(F.E3.ErrichterFirmaOrt));
        Assert.Equal("E-4711", e3.Text(F.E3.ErrichterAusweisNr));
        Assert.Equal("netze-duisburg", e3.Text(F.E3.ErrichterBeiNetzbetreiber));
    }

    [Theory]
    [InlineData(Speicherkopplung.Ac, true, false)]
    [InlineData(Speicherkopplung.Dc, false, true)]
    [InlineData(null, false, false)]
    public void E3_coupling_only_when_known(Speicherkopplung? kopplung, bool ac, bool dc)
    {
        var e3 = E3(Card(speicher: Speicher with { Kopplung = kopplung }));

        Assert.Equal(ac, e3.IsChecked(F.E3.AcGekoppelt));
        Assert.Equal(dc, e3.IsChecked(F.E3.DcGekoppelt));
    }

    [Fact]
    public void E3_converter_and_capacity_fields_stay_empty()
    {
        var e3 = E3();

        Assert.Null(e3.Text(F.E3.NutzbareKapazitaet));
        Assert.Null(e3.Text(F.E3.UmrichterHerstellerTyp));
        Assert.Null(e3.Text(F.E3.UmrichterAnzahl));
        Assert.Null(e3.Text(F.E3.UmrichterVerschiebungsfaktor));
        Assert.Null(e3.Text(F.E3.UmrichterScheinleistung));
        Assert.Null(e3.Text(F.E3.UmrichterWirkleistung));
        Assert.Null(e3.Text(F.E3.UmrichterBemessungsstrom));
        Assert.Null(e3.Text(F.E3.UnterschriftErrichter));
    }

    [Fact]
    public void E3_attachment_checkboxes_follow_attachments()
    {
        Assert.False(E3().IsChecked(F.E3.SchaltplanBeigefuegt));
        Assert.False(E3().IsChecked(F.E3.EinheitenzertifikateLiegenVor));

        var mit = E3(null, DocumentKind.Schaltplan, DocumentKind.Einheitenzertifikat);
        Assert.True(mit.IsChecked(F.E3.SchaltplanBeigefuegt));
        Assert.True(mit.IsChecked(F.E3.EinheitenzertifikateLiegenVor));
    }

    // ---- E.8 ----

    [Fact]
    public void E8_fixed_values_and_card_values()
    {
        var e8 = E8();

        Assert.Equal("Jürgen Müller-Lüdenscheidt", e8.Text(F.E8.AnlageName));
        Assert.Equal("Sonnenschein Elektro GmbH, Mülheim", e8.Text(F.E8.ErrichterFirmaOrt));
        Assert.Equal("8", e8.Text(F.E8.MaxWirkleistung));
        Assert.Equal("9", e8.Text(F.E8.Modulleistung));
        Assert.Equal("20.03.2027", e8.Text(F.E8.Inbetriebsetzungsdatum));
        Assert.True(e8.IsChecked(F.E8.UebereinstimmungE2E3));
        Assert.True(e8.IsChecked(F.E8.Abrechnungsmessung));
        Assert.True(e8.IsChecked(F.E8.EinheitenzertifikateVorhanden));
        Assert.False(e8.IsChecked(F.E8.LeistungsflussueberwachungZertifikat));
        Assert.True(e8.IsChecked(F.E8.NaSchutzZertifikatVorhanden));
        Assert.True(e8.IsChecked(F.E8.DrosselungImUmrichter));
        Assert.True(e8.IsChecked(F.E8.Energieflussrichtungssensor));
    }

    [Fact]
    public void E8_symmetry_section_and_apparent_power_stay_empty()
    {
        var e8 = E8();

        Assert.Null(e8.Text(F.E8.MaxScheinleistung));
        Assert.Null(e8.Text(F.E8.IntegrierterNaSchutzUMax));
        foreach (var box in new[]
        {
            F.E8.SymmetrieDreiphasig, F.E8.SymmetrieEinrichtung, F.E8.BlindleistungQu, F.E8.BlindleistungCosPhiP,
            F.E8.BlindleistungFesterCosPhi, F.E8.TfSperrenGefordertJa, F.E8.TfSperrenGefordertNein,
            F.E8.TfSperrenEingebaut, F.E8.TfSperrenPruefprotokoll, F.E8.PaveFunktionstest,
            F.E8.ZertifizierteSteuerungDrosselung, F.E8.FernsteuerbareLeistungsreduzierung,
        })
            Assert.False(e8.IsChecked(box), box);
        foreach (var text in new[]
        {
            F.E8.SymmetrieAufteilung, F.E8.SymmetrieVorhandenL1, F.E8.SymmetrieNeuL1, F.E8.BlindleistungFesterCosPhiWert,
            F.E8.PaveWirkleistung, F.E8.UnterschriftBetreiber, F.E8.UnterschriftErrichter, F.E8.OrtDatum,
        })
            Assert.Null(e8.Text(text));
    }

    // ---- Zählerantrag ----

    [Theory]
    [InlineData(ZaehlerwechselTyp.TauschZweirichtungszaehler, "4")]
    [InlineData(ZaehlerwechselTyp.UmbauKaskade, "4")]
    [InlineData(ZaehlerwechselTyp.Ausbau, "3")]
    [InlineData(ZaehlerwechselTyp.Neusetzung, "2")]
    [InlineData(ZaehlerwechselTyp.KeinWechsel, "1")]
    public void Zaehlerantrag_order_follows_change_type(ZaehlerwechselTyp typ, string expectedAuftrag)
    {
        var antrag = Antrag(Zaehler(typ));

        Assert.Equal(expectedAuftrag, antrag.Radio(F.Zaehlerantrag.Auftrag));
    }

    [Fact]
    public void Zaehlerantrag_meter_number_goes_to_the_field_of_the_order()
    {
        var wechsel = Antrag(Zaehler(ZaehlerwechselTyp.TauschZweirichtungszaehler));
        Assert.Equal(Zaehlernummer, wechsel.Text(F.Zaehlerantrag.ZaehlernummerWechsel));
        Assert.Null(wechsel.Text(F.Zaehlerantrag.ZaehlernummerAusbau));

        var ausbau = Antrag(Zaehler(ZaehlerwechselTyp.Ausbau));
        Assert.Equal(Zaehlernummer, ausbau.Text(F.Zaehlerantrag.ZaehlernummerAusbau));
        Assert.Null(ausbau.Text(F.Zaehlerantrag.ZaehlernummerWechsel));

        var neu = Antrag(Zaehler(ZaehlerwechselTyp.Neusetzung, nummer: null));
        Assert.Null(neu.Text(F.Zaehlerantrag.ZaehlernummerAusbau));
        Assert.Null(neu.Text(F.Zaehlerantrag.ZaehlernummerWechsel));
    }

    [Theory]
    [InlineData(Zaehlerbauform.Ehz, "8")]
    [InlineData(Zaehlerbauform.DreiPunkt, "2")]
    public void Zaehlerantrag_meter_type_follows_design(Zaehlerbauform bauform, string expected)
    {
        var antrag = Antrag(Zaehler(ZaehlerwechselTyp.TauschZweirichtungszaehler, bauform));

        Assert.Equal(expected, antrag.Radio(F.Zaehlerantrag.Zaehler));
    }

    [Theory]
    [InlineData(ZaehlerwechselTyp.Ausbau)]
    [InlineData(ZaehlerwechselTyp.KeinWechsel)]
    public void Zaehlerantrag_no_meter_type_when_no_new_meter_is_set(ZaehlerwechselTyp typ)
    {
        var antrag = Antrag(Zaehler(typ, Zaehlerbauform.Ehz));

        Assert.Null(antrag.Radio(F.Zaehlerantrag.Zaehler));
    }

    [Fact]
    public void Zaehlerantrag_fixed_values_and_card_values()
    {
        var antrag = Antrag(Zaehler());

        Assert.Equal("1", antrag.Radio(F.Zaehlerantrag.Messebene)); // Niederspannung
        Assert.Equal("5", antrag.Radio(F.Zaehlerantrag.Massnahme));
        Assert.Equal("6", antrag.Radio(F.Zaehlerantrag.Nutzungsart)); // Eigenerzeugung
        Assert.Equal("63", antrag.Text(F.Zaehlerantrag.SicherungHak));
        Assert.Equal("9", antrag.Text(F.Zaehlerantrag.EegPeakleistung)); // = kWp
        Assert.Equal("Jürgen Müller-Lüdenscheidt", antrag.Text(F.Zaehlerantrag.Grundstueckseigentuemer));
        Assert.Equal("Äußere Gärtnerstraße 12b", antrag.Text(F.Zaehlerantrag.StandortStrasse));
        Assert.Equal("47228 Duisburg", antrag.Text(F.Zaehlerantrag.StandortPlzOrt));
        Assert.Equal("Keller, Wohnung links", antrag.Text(F.Zaehlerantrag.LageVerbrauchsstelle));
        Assert.Equal("Zählerschrank", antrag.Text(F.Zaehlerantrag.Einbauort));
        Assert.Equal(Email, antrag.Text(F.Zaehlerantrag.AnschlussnutzerEmail));
        Assert.Equal(Telefon, antrag.Text(F.Zaehlerantrag.AnschlussnutzerTelefon));
        Assert.Null(antrag.Text(F.Zaehlerantrag.Installateur));
        Assert.Null(antrag.Text(F.Zaehlerantrag.Terminwunsch));
    }

    [Fact]
    public void Zaehlerantrag_fuse_follows_configured_nh_fuse()
    {
        var antrag = Antrag(Zaehler(), Card() with { NhSicherungAmpere = 100 });

        Assert.Equal("100", antrag.Text(F.Zaehlerantrag.SicherungHak));
    }

    [Fact]
    public void Zaehlerantrag_file_name_carries_the_number()
    {
        var doc = Gen.CreateZaehlerantrag(Card(), Zaehler(), 3);

        Assert.Equal("Zaehlerantrag_3.pdf", doc.FileName);
        Assert.Equal("application/pdf", doc.ContentType);
    }

    [Fact]
    public void Generated_documents_are_pdfs_with_template_names()
    {
        var card = Card(speicher: Speicher);

        Assert.Equal("E1_Antragstellung.pdf", Gen.CreateE1(card, [], Heute).FileName);
        Assert.Equal("E2_Datenblatt_Erzeugungsanlage.pdf", Gen.CreateE2(card).FileName);
        Assert.Equal("E3_Datenblatt_Speicher.pdf", Gen.CreateE3(card, []).FileName);
        Assert.Equal("E8_Inbetriebsetzungsprotokoll.pdf", Gen.CreateE8(card, Heute).FileName);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(Gen.CreateE2(card).Content, 0, 4));
    }
}
