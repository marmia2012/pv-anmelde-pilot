using Microsoft.Extensions.Logging;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Submission;
using PvAutopilot.Netzbetreiber.NetzeDuisburg;
using PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms;
using static PvAutopilot.Tests.Netzbetreiber.NetzeDuisburgFixtures;
using F = PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms.NetzeDuisburgFormFields;

namespace PvAutopilot.Tests.Netzbetreiber;

public class NetzeDuisburgPackageTests
{
    private static readonly DocumentKind[] AllAttachments =
    [
        DocumentKind.Lageplan, DocumentKind.Schaltplan, DocumentKind.Datenblatt, DocumentKind.Einheitenzertifikat,
        DocumentKind.NaSchutzZertifikat, DocumentKind.NaSchutzPruefbericht, DocumentKind.EvuVollmacht,
    ];

    private static Task<ApplicationPackage> Prepare(NetzeDuisburgAdapter adapter, SubmissionKind kind, NabDataCard card, DateOnly? ibn = null) =>
        adapter.PrepareAsync(kind, Process(card, ibn), CancellationToken.None);

    // ---- Netzanmeldung ----

    [Fact]
    public async Task Netzanmeldung_contains_E1_E2_and_the_attachments_of_the_data_card()
    {
        var card = Card([.. AllAttachments, DocumentKind.FotoHauptzaehler, DocumentKind.Einspeisezusage]);
        var package = await Prepare(Adapter(), SubmissionKind.Netzanmeldung, card);

        Assert.Equal(["E1_Antragstellung.pdf", "E2_Datenblatt_Erzeugungsanlage.pdf"], package.Documents.Select(d => d.FileName));
        Assert.Equal(AllAttachments.Order(), package.Attachments.Select(a => a.Kind).Order());
        Assert.Equal(SubmissionKind.Netzanmeldung, package.Kind);
        Assert.Equal("netze-duisburg", package.NetzbetreiberCode);
    }

    [Fact]
    public async Task Netzanmeldung_with_storage_adds_E3()
    {
        var package = await Prepare(Adapter(), SubmissionKind.Netzanmeldung, Card(speicher: Speicher));

        Assert.Equal(
            ["E1_Antragstellung.pdf", "E2_Datenblatt_Erzeugungsanlage.pdf", "E3_Datenblatt_Speicher.pdf"],
            package.Documents.Select(d => d.FileName));
    }

    [Fact]
    public async Task Netzanmeldung_E1_checkboxes_reflect_the_package_attachments()
    {
        var withLageplan = await Prepare(Adapter(), SubmissionKind.Netzanmeldung, Card([DocumentKind.EvuVollmacht, DocumentKind.Schaltplan, DocumentKind.Lageplan]));
        var without = await Prepare(Adapter(), SubmissionKind.Netzanmeldung, Card());

        Assert.True(PdfReadback.Of(withLageplan.Documents[0].Content).IsChecked(F.E1.LageplanBeigefuegt));
        Assert.True(PdfReadback.Of(withLageplan.Documents[0].Content).IsChecked(F.E1.SchaltplanBeigefuegt));
        Assert.False(PdfReadback.Of(without.Documents[0].Content).IsChecked(F.E1.LageplanBeigefuegt));
        Assert.Equal("Duisburg, 06.11.2026", PdfReadback.Of(without.Documents[0].Content).Text(F.E1.DatumOrt));
    }

    [Fact]
    public async Task Required_attachment_outside_default_list_is_still_attached()
    {
        var options = new NetzeDuisburgOptions { RequiredAttachments = [DocumentKind.FotoHauptzaehler] };
        var card = Card([DocumentKind.FotoHauptzaehler, DocumentKind.Schaltplan]);

        var package = await Prepare(Adapter(options), SubmissionKind.Netzanmeldung, card);

        Assert.Equal(
            new[] { DocumentKind.FotoHauptzaehler, DocumentKind.Schaltplan }.Order(), package.Attachments.Select(a => a.Kind).Order());
    }

    // ---- Validate ----

    [Fact]
    public void Missing_required_attachments_are_reported_by_default()
    {
        var problems = Adapter().Validate(SubmissionKind.Netzanmeldung, Card([]));

        Assert.Contains(problems, p => p.Contains("EVU-Vollmacht"));
        Assert.Contains(problems, p => p.Contains("Übersichtsschaltplan"));
    }

    [Fact]
    public void Missing_single_required_attachment_is_reported_alone()
    {
        var ohneSchaltplan = Adapter().Validate(SubmissionKind.Netzanmeldung, Card([DocumentKind.EvuVollmacht]));
        var ohneVollmacht = Adapter().Validate(SubmissionKind.Netzanmeldung, Card([DocumentKind.Schaltplan]));

        Assert.Single(ohneSchaltplan);
        Assert.Contains("Übersichtsschaltplan", ohneSchaltplan[0]);
        Assert.Single(ohneVollmacht);
        Assert.Contains("EVU-Vollmacht", ohneVollmacht[0]);
    }

    [Fact]
    public void Complete_card_has_no_problems()
    {
        Assert.Empty(Adapter().Validate(SubmissionKind.Netzanmeldung, Card()));
    }

    [Fact]
    public void Configured_required_attachments_replace_the_default()
    {
        var options = new NetzeDuisburgOptions { RequiredAttachments = [DocumentKind.Lageplan] };
        var adapter = Adapter(options);

        var problems = adapter.Validate(SubmissionKind.Netzanmeldung, Card([]));
        Assert.Single(problems);
        Assert.Contains("Lageplan", problems[0]);
        Assert.Empty(adapter.Validate(SubmissionKind.Netzanmeldung, Card([DocumentKind.Lageplan])));
    }

    [Fact]
    public void Empty_configured_required_attachments_mean_no_attachment_problems()
    {
        var adapter = Adapter(new NetzeDuisburgOptions { RequiredAttachments = [] });

        Assert.Empty(adapter.Validate(SubmissionKind.Netzanmeldung, Card([])));
    }

    [Fact]
    public void Default_required_attachments_are_vollmacht_and_schaltplan()
    {
        Assert.Equal(
            new[] { DocumentKind.EvuVollmacht, DocumentKind.Schaltplan }.Order(),
            new NetzeDuisburgOptions().EffectiveRequiredAttachments.Order());
    }

    [Fact]
    public async Task Missing_optional_attachment_is_no_problem_but_logs_a_warning()
    {
        var log = new CapturingLogger<NetzeDuisburgAdapter>();
        var adapter = Adapter(adapterLogger: log);
        var card = Card(); // Vollmacht + Schaltplan, sonst nichts

        Assert.Empty(adapter.Validate(SubmissionKind.Netzanmeldung, card));
        await Prepare(adapter, SubmissionKind.Netzanmeldung, card);

        var warnings = log.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        Assert.Contains(warnings, w => w.Contains("Lageplan"));
        Assert.Contains(warnings, w => w.Contains("Datenblatt"));
        Assert.DoesNotContain(warnings, w => w.Contains("Schaltplan"));
        Assert.DoesNotContain(warnings, w => w.Contains("EvuVollmacht"));
    }

    [Fact]
    public async Task No_warning_when_all_attachments_present()
    {
        var log = new CapturingLogger<NetzeDuisburgAdapter>();

        await Prepare(Adapter(adapterLogger: log), SubmissionKind.Netzanmeldung, Card(AllAttachments));

        Assert.DoesNotContain(log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Required_attachment_is_not_reported_as_optional_warning()
    {
        var log = new CapturingLogger<NetzeDuisburgAdapter>();
        var adapter = Adapter(new NetzeDuisburgOptions { RequiredAttachments = [DocumentKind.Lageplan] }, adapterLogger: log);

        await Prepare(adapter, SubmissionKind.Netzanmeldung, Card([DocumentKind.Lageplan]));

        Assert.DoesNotContain(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Lageplan"));
    }

    [Theory]
    [InlineData(60, 8000, true)]   // 27 kWp
    [InlineData(20, 8000, false)]  // 9 kWp
    [InlineData(55, 8000, false)]  // 24,75 kWp
    [InlineData(20, 25000, true)]  // WR exakt 25 kW
    [InlineData(20, 24990, false)]
    public void Power_limit_of_25_kw_uses_the_stricter_of_kwp_and_inverter_power(int module, int wrWatt, bool blocked)
    {
        var card = Card() with
        {
            Module = [new ModulPosition("A", "M", 450, module)],
            Wechselrichter = [new WechselrichterPosition("B", "W", null, wrWatt, 1, null)],
        };

        var problems = Adapter().Validate(SubmissionKind.Netzanmeldung, card);

        Assert.Equal(blocked, problems.Any(p => p.Contains("25 kW")));
    }

    [Fact]
    public void Exactly_25_kwp_is_blocked()
    {
        var card = Card() with { Module = [new ModulPosition("A", "M", 500, 50)] };

        Assert.Contains(Adapter().Validate(SubmissionKind.Netzanmeldung, card), p => p.Contains("25 kW"));
    }

    [Fact]
    public void Missing_modules_and_inverters_are_reported()
    {
        var problems = Adapter().Validate(SubmissionKind.Netzanmeldung, Card() with { Module = [], Wechselrichter = [] });

        Assert.Contains(problems, p => p.Contains("Module"));
        Assert.Contains(problems, p => p.Contains("Wechselrichter"));
    }

    // ---- Inbetriebnahme ----

    [Fact]
    public async Task Inbetriebnahme_contains_E8_and_one_meter_application_per_meter_and_no_attachments()
    {
        var card = Card(zaehler: [Zaehler(), Zaehler(ZaehlerwechselTyp.Neusetzung, Zaehlerbauform.DreiPunkt, null)]);

        var package = await Prepare(Adapter(), SubmissionKind.Inbetriebnahme, card, new DateOnly(2027, 3, 20));

        Assert.Equal(
            ["E8_Inbetriebsetzungsprotokoll.pdf", "Zaehlerantrag_1.pdf", "Zaehlerantrag_2.pdf"],
            package.Documents.Select(d => d.FileName));
        Assert.Empty(package.Attachments);
        Assert.Equal("20.03.2027", PdfReadback.Of(package.Documents[0].Content).Text(F.E8.Inbetriebsetzungsdatum));
        Assert.Equal("4", PdfReadback.Of(package.Documents[1].Content).Radio(F.Zaehlerantrag.Auftrag));
        Assert.Equal("2", PdfReadback.Of(package.Documents[2].Content).Radio(F.Zaehlerantrag.Auftrag));
    }

    [Fact]
    public async Task Inbetriebnahme_without_date_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Prepare(Adapter(), SubmissionKind.Inbetriebnahme, Card()));
    }

    [Fact]
    public async Task Prepare_without_data_card_throws()
    {
        var adapter = Adapter();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.PrepareAsync(SubmissionKind.Netzanmeldung, Process(null), CancellationToken.None));
    }

    [Theory]
    [InlineData(SubmissionKind.Zaehlerwechsel)]
    [InlineData(SubmissionKind.Mastr)]
    public async Task Prepare_for_other_kinds_throws(SubmissionKind kind)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Prepare(Adapter(), kind, Card(), Heute));
    }

    [Fact]
    public void Only_netzanmeldung_and_inbetriebnahme_are_submissions()
    {
        Assert.Equal(
            new[] { SubmissionKind.Netzanmeldung, SubmissionKind.Inbetriebnahme }.Order(),
            Adapter().Einreichungen.Order());
    }

    [Fact]
    public void Inbetriebnahme_validation_requires_meters()
    {
        var problems = Adapter().Validate(SubmissionKind.Inbetriebnahme, Card(zaehler: []));

        Assert.Contains(problems, p => p.Contains("Keine Zähler"));
    }

    [Theory]
    [InlineData(ZaehlerwechselTyp.TauschZweirichtungszaehler, true)]
    [InlineData(ZaehlerwechselTyp.Ausbau, true)]
    [InlineData(ZaehlerwechselTyp.UmbauKaskade, true)]
    [InlineData(ZaehlerwechselTyp.Neusetzung, false)]
    [InlineData(ZaehlerwechselTyp.KeinWechsel, false)]
    public void Inbetriebnahme_validation_requires_meter_number_for_exchange_and_removal(ZaehlerwechselTyp typ, bool required)
    {
        var problems = Adapter().Validate(SubmissionKind.Inbetriebnahme, Card(zaehler: [Zaehler(typ, Zaehlerbauform.Ehz, null)]));

        Assert.Equal(required, problems.Any(p => p.Contains("Zählernummer")));
    }

    [Theory]
    [InlineData(ZaehlerwechselTyp.TauschZweirichtungszaehler, true)]
    [InlineData(ZaehlerwechselTyp.Neusetzung, true)]
    [InlineData(ZaehlerwechselTyp.UmbauKaskade, true)]
    [InlineData(ZaehlerwechselTyp.Ausbau, false)]
    [InlineData(ZaehlerwechselTyp.KeinWechsel, false)]
    public void Inbetriebnahme_validation_requires_design_when_a_new_meter_is_set(ZaehlerwechselTyp typ, bool required)
    {
        var problems = Adapter().Validate(SubmissionKind.Inbetriebnahme, Card(zaehler: [Zaehler(typ, null)]));

        Assert.Equal(required, problems.Any(p => p.Contains("Zählerbauform")));
    }

    [Fact]
    public void Inbetriebnahme_validation_names_the_meter_index()
    {
        var card = Card(zaehler: [Zaehler(), Zaehler(ZaehlerwechselTyp.Ausbau, null, null)]);

        var problems = Adapter().Validate(SubmissionKind.Inbetriebnahme, card);

        Assert.Single(problems);
        Assert.Contains("Zähler 2", problems[0]);
    }

    [Fact]
    public void Complete_inbetriebnahme_has_no_problems()
    {
        Assert.Empty(Adapter().Validate(SubmissionKind.Inbetriebnahme, Card()));
    }

    // ---- Keine PII in Logs ----

    [Fact]
    public async Task Logs_contain_no_personal_data()
    {
        var formLog = new CapturingLogger<NetzeDuisburgFormGenerator>();
        var adapterLog = new CapturingLogger<NetzeDuisburgAdapter>();
        var adapter = Adapter(formLogger: formLog, adapterLogger: adapterLog);
        var card = Card(speicher: Speicher, zaehler: [Zaehler(), Zaehler(ZaehlerwechselTyp.Ausbau, null, "1EMH0099999999")]);

        adapter.Validate(SubmissionKind.Netzanmeldung, card);
        adapter.Validate(SubmissionKind.Inbetriebnahme, card);
        await Prepare(adapter, SubmissionKind.Netzanmeldung, card);
        await Prepare(adapter, SubmissionKind.Inbetriebnahme, card, new DateOnly(2027, 3, 20));

        var logs = formLog.Entries.Concat(adapterLog.Entries).Select(e => e.Message).ToList();
        Assert.NotEmpty(logs);
        string[] pii =
        [
            Vorname, Nachname, Strasse, "Gärtner", Hausnummer, Email, "example.org", Telefon, "123456",
            Zaehlernummer, "1EMH0099999999", Hauptzaehlernummer, "DE00 0000", "Keller", "Wohnung links",
            "Sonnenschein", "E-4711", "Akku 10",
        ];
        foreach (var message in logs)
            foreach (var value in pii)
                Assert.DoesNotContain(value, message, StringComparison.OrdinalIgnoreCase);
    }
}
