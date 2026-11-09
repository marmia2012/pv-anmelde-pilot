using System.Net;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Sources;
using PvAutopilot.Sources.HubSpot;
using K = PvAutopilot.Sources.HubSpot.HubSpotPropertyKeys;

namespace PvAutopilot.Tests.Sources.HubSpot;

public class HubSpotCrmProjectSourceTests
{
    private readonly HubSpotRig _rig = new();

    public HubSpotCrmProjectSourceTests()
    {
        // Standardfall: eine passende Vollmacht hängt am Deal.
        _rig.AddAttachments("1001", "f1", "2026-11-01T10:00:00Z");
        _rig.AddFile("f1", "Vollmacht_Test", "pdf", "2026-11-01T10:00:00Z");
    }

    private async Task<CrmProjectData> Read(Action<Dictionary<string, string?>> edit)
    {
        var deal = HubSpotRig.BaseDeal();
        edit(deal);
        _rig.SetDeal(deal);
        return await _rig.ReadAsync();
    }

    private static long Epoch(int year, int month, int day) => new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    // Mapping

    [Fact]
    public async Task Base_deal_maps_customer_as_operator_and_customer_address_as_plant_address()
    {
        var data = await _rig.ReadAsync();

        Assert.Empty(data.Problems);
        Assert.Equal("reonic-42", data.PlanningProjectId);
        Assert.Equal(new Person("Frau", "Erika", "Mustermann", "erika.mustermann@example.org", "0203 555010", "DE02120300000000202051"),
            data.Anlagenbetreiber);
        Assert.Equal(new PostalAddress("Beispielweg", "7", "47051", "Duisburg"), data.Anlagenadresse);
        Assert.Null(data.Errichter);
        Assert.Null(data.Einspeiseart);
        Assert.Null(data.NhSicherungAmpere);
        Assert.Null(data.BestandsanlageVorhanden);
        Assert.Null(data.Installationstermin);
        var vollmacht = Assert.Single(data.Dokumente);
        Assert.Equal(DocumentKind.EvuVollmacht, vollmacht.Kind);
    }

    [Fact]
    public async Task Fully_filled_deal_maps_every_field()
    {
        var data = await Read(d =>
        {
            d[K.BetreiberVorname] = "Hans";
            d[K.BetreiberNachname] = "Beispiel";
            d[K.AnlageStrasse] = "Sonnenallee";
            d[K.AnlageHausnummer] = "3b";
            d[K.AnlagePlz] = "47137";
            d[K.AnlageOrt] = "Duisburg";
            d[K.ErrichterFirma] = "Deal Elektro GmbH";
            d[K.ErrichterOrt] = "Essen";
            d[K.ErrichterEintragungsnummer] = "E-999";
            d[K.ErrichterNetzbetreiber] = "rheinnetz";
            d[K.Einspeiseart] = "VOLL";
            d[K.NhSicherungAmpere] = "80 A";
            d[K.BestandsanlageVorhanden] = "Ja";
            d[K.TerminElektroinstallation] = "2026-12-01";
        });

        Assert.Empty(data.Problems);
        // Abweichender Betreiber: Name vom Betreiber, Kontakt und IBAN bleiben die des Kunden.
        Assert.Equal(new Person(null, "Hans", "Beispiel", "erika.mustermann@example.org", "0203 555010", "DE02120300000000202051"),
            data.Anlagenbetreiber);
        Assert.Equal(new PostalAddress("Sonnenallee", "3b", "47137", "Duisburg"), data.Anlagenadresse);
        Assert.Equal(new Anlagenerrichter("Deal Elektro GmbH", "Essen", "E-999", "rheinnetz"), data.Errichter);
        Assert.Equal(Einspeiseart.Volleinspeisung, data.Einspeiseart);
        Assert.Equal(80, data.NhSicherungAmpere);
        Assert.True(data.BestandsanlageVorhanden);
        Assert.Equal(new DateOnly(2026, 12, 1), data.Installationstermin);
    }

    [Fact]
    public async Task Partial_operator_falls_back_to_a_problem_instead_of_mixing_customer_and_operator()
    {
        var data = await Read(d => d[K.BetreiberVorname] = "Hans");

        Assert.Null(data.Anlagenbetreiber);
        Assert.Contains(data.Problems, p => p.Contains("Anlagenbetreiber") && p.Contains(K.BetreiberNachname));
    }

    [Fact]
    public async Task Empty_operator_values_count_as_empty()
    {
        var data = await Read(d =>
        {
            d[K.BetreiberVorname] = "  ";
            d[K.BetreiberNachname] = null;
        });

        Assert.Equal("Erika", data.Anlagenbetreiber!.Vorname);
    }

    [Fact]
    public async Task Empty_plant_address_falls_back_to_customer_address()
    {
        var data = await Read(d =>
        {
            d[K.AnlageStrasse] = "";
            d[K.AnlageOrt] = " ";
        });

        Assert.Equal(new PostalAddress("Beispielweg", "7", "47051", "Duisburg"), data.Anlagenadresse);
    }

    [Theory]
    [InlineData("2026-12-01", "2026-12-10", 2026, 12, 10)]
    [InlineData("2026-12-10", "2026-12-01", 2026, 12, 10)]
    [InlineData("2026-12-10", "2026-12-10", 2026, 12, 10)]
    [InlineData("2026-12-05", null, 2026, 12, 5)]
    [InlineData(null, "2026-12-07", 2026, 12, 7)]
    public async Task Installation_date_is_the_later_of_electric_and_roof_date(string? elektro, string? dach, int y, int m, int d)
    {
        var data = await Read(deal =>
        {
            deal[K.TerminElektroinstallation] = elektro;
            deal[K.TerminDachmontage] = dach;
        });

        Assert.Equal(new DateOnly(y, m, d), data.Installationstermin);
    }

    [Fact]
    public async Task Dates_as_epoch_milliseconds_and_iso_timestamps_are_understood()
    {
        var data = await Read(d =>
        {
            d[K.TerminElektroinstallation] = Epoch(2026, 12, 3).ToString();
            d[K.TerminDachmontage] = "2026-12-02T08:00:00.000Z";
        });

        Assert.Equal(new DateOnly(2026, 12, 3), data.Installationstermin);
    }

    [Fact]
    public async Task Invalid_date_is_a_problem()
    {
        var data = await Read(d => d[K.TerminDachmontage] = "demnächst");

        Assert.Null(data.Installationstermin);
        Assert.Contains(data.Problems, p => p.Contains(K.TerminDachmontage));
    }

    [Theory]
    [InlineData("voll", Einspeiseart.Volleinspeisung)]
    [InlineData("Teil", Einspeiseart.Teileinspeisung)]
    [InlineData(" TEIL ", Einspeiseart.Teileinspeisung)]
    public async Task Feed_in_type_is_mapped_through_the_configured_option_values(string value, Einspeiseart expected)
    {
        var data = await Read(d => d[K.Einspeiseart] = value);

        Assert.Empty(data.Problems);
        Assert.Equal(expected, data.Einspeiseart);
    }

    [Fact]
    public async Task Unknown_feed_in_type_is_a_problem_without_the_value()
    {
        var data = await Read(d => d[K.Einspeiseart] = "Geheimwert-4711");

        Assert.Null(data.Einspeiseart);
        var problem = Assert.Single(data.Problems);
        Assert.Contains("Einspeiseart", problem);
        Assert.DoesNotContain("Geheimwert-4711", problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Empty_feed_in_type_is_no_problem_and_stays_open_for_the_default(string value)
    {
        var data = await Read(d => d[K.Einspeiseart] = value);

        Assert.Empty(data.Problems);
        Assert.Null(data.Einspeiseart);
    }

    [Theory]
    [InlineData("63", 63)]
    [InlineData("100 A", 100)]
    [InlineData("35a", 35)]
    [InlineData("63,0", 63)]
    public async Task Fuse_rating_accepts_whole_amperes(string value, int expected)
    {
        var data = await Read(d => d[K.NhSicherungAmpere] = value);

        Assert.Empty(data.Problems);
        Assert.Equal(expected, data.NhSicherungAmpere);
    }

    [Theory]
    [InlineData("viel")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("63,5")]
    public async Task Invalid_fuse_rating_is_a_problem(string value)
    {
        var data = await Read(d => d[K.NhSicherungAmpere] = value);

        Assert.Null(data.NhSicherungAmpere);
        Assert.Contains(data.Problems, p => p.Contains(K.NhSicherungAmpere));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("Ja", true)]
    [InlineData("false", false)]
    [InlineData("nein", false)]
    public async Task Existing_plant_flag_accepts_yes_no_values(string value, bool expected)
    {
        var data = await Read(d => d[K.BestandsanlageVorhanden] = value);

        Assert.Empty(data.Problems);
        Assert.Equal(expected, data.BestandsanlageVorhanden);
    }

    [Fact]
    public async Task Unclear_existing_plant_flag_is_a_problem()
    {
        var data = await Read(d => d[K.BestandsanlageVorhanden] = "vielleicht");

        Assert.Null(data.BestandsanlageVorhanden);
        Assert.Contains(data.Problems, p => p.Contains(K.BestandsanlageVorhanden));
    }

    [Fact]
    public async Task Missing_reonic_id_leaves_the_planning_project_empty()
    {
        var data = await Read(d => d.Remove(K.ReonicProjectId));

        Assert.Null(data.PlanningProjectId);
    }

    [Fact]
    public async Task Deal_errichter_without_location_is_a_problem()
    {
        var data = await Read(d => d[K.ErrichterFirma] = "Deal Elektro GmbH");

        Assert.Null(data.Errichter);
        Assert.Contains(data.Problems, p => p.Contains(K.ErrichterOrt));
    }

    [Fact]
    public async Task Missing_customer_name_is_a_problem_naming_the_fields()
    {
        var data = await Read(d => d.Remove(K.KundeNachname));

        Assert.Null(data.Anlagenbetreiber);
        Assert.Contains(data.Problems, p => p.Contains("Anlagenbetreiber") && p.Contains(K.KundeNachname));
    }

    // Adressen

    [Fact]
    public async Task Separate_address_fields_win_over_the_single_line()
    {
        var data = await Read(d => d[K.KundeAdresse] = "Andereweg 99, 40210 Düsseldorf");

        Assert.Equal(new PostalAddress("Beispielweg", "7", "47051", "Duisburg"), data.Anlagenadresse);
    }

    [Fact]
    public async Task Missing_house_number_is_taken_from_the_street_field()
    {
        var data = await Read(d =>
        {
            d[K.KundeStrasse] = "Beispielweg 7a";
            d.Remove(K.KundeHausnummer);
        });

        Assert.Empty(data.Problems);
        Assert.Equal(new PostalAddress("Beispielweg", "7a", "47051", "Duisburg"), data.Anlagenadresse);
    }

    [Fact]
    public async Task Single_line_customer_address_is_split()
    {
        var data = await Read(d =>
        {
            foreach (var key in new[] { K.KundeStrasse, K.KundeHausnummer, K.KundePlz, K.KundeOrt })
                d.Remove(key);
            d[K.KundeAdresse] = "Beispielweg 7a, 47051 Duisburg";
        });

        Assert.Empty(data.Problems);
        Assert.Equal(new PostalAddress("Beispielweg", "7a", "47051", "Duisburg"), data.Anlagenadresse);
    }

    [Fact]
    public async Task Single_line_plant_address_is_split_and_used_instead_of_the_customer_address()
    {
        var data = await Read(d => d[K.AnlageAdresse] = "Sonnenallee 3 b\n47137 Duisburg");

        Assert.Empty(data.Problems);
        Assert.Equal(new PostalAddress("Sonnenallee", "3 b", "47137", "Duisburg"), data.Anlagenadresse);
    }

    [Fact]
    public async Task Unsplittable_single_line_is_a_problem_without_the_text()
    {
        var data = await Read(d => d[K.AnlageAdresse] = "Geheimgasse ohne Hausnummer");

        Assert.Null(data.Anlagenadresse);
        var problem = Assert.Single(data.Problems);
        Assert.Contains("Anlagenadresse", problem);
        Assert.DoesNotContain("Geheimgasse", problem);
    }

    [Fact]
    public async Task Incomplete_separate_address_fields_are_a_problem()
    {
        var data = await Read(d => d.Remove(K.KundeOrt));

        Assert.Null(data.Anlagenadresse);
        Assert.Contains(data.Problems, p => p.Contains(K.KundeOrt));
    }

    [Fact]
    public async Task Invalid_postcode_is_a_problem()
    {
        var data = await Read(d => d[K.KundePlz] = "4705");

        Assert.Null(data.Anlagenadresse);
        Assert.Contains(data.Problems, p => p.Contains("PLZ"));
    }

    [Fact]
    public async Task No_address_at_all_is_a_problem()
    {
        var data = await Read(d =>
        {
            foreach (var key in new[] { K.KundeStrasse, K.KundeHausnummer, K.KundePlz, K.KundeOrt })
                d.Remove(key);
        });

        Assert.Null(data.Anlagenadresse);
        Assert.Contains(data.Problems, p => p.Contains("Anlagenadresse") && p.Contains("fehlt"));
    }

    // Anfrage

    [Fact]
    public async Task Only_configured_data_card_properties_are_requested_and_never_the_mastr_login()
    {
        _rig.Options.Properties[K.KundeTelefon] = "";
        _rig.Options.Properties.Remove(K.BestandsanlageVorhanden);

        await _rig.ReadAsync();

        var request = Assert.Single(_rig.Handler.Requests, r => r.Path == $"crm/v3/objects/0-3/{HubSpotRig.DealId}");
        var expected = K.DataCardKeys
            .Where(k => k is not (K.KundeTelefon or K.BestandsanlageVorhanden))
            .Select(HubSpotRig.PropertyName).ToHashSet();
        Assert.True(expected.SetEquals(request.RequestedProperties), string.Join(",", request.RequestedProperties));
        Assert.DoesNotContain("mastr_passwort", request.RequestedProperties);
        Assert.DoesNotContain("mastr_benutzername", request.RequestedProperties);
        Assert.DoesNotContain("status", request.RequestedProperties);
        Assert.Empty(_rig.Handler.Writes);
    }

    [Fact]
    public async Task Reading_uses_the_configured_object_type()
    {
        _rig.Options.ObjectTypeId = "2-77";
        _rig.Handler.Get($"crm/v3/objects/2-77/{HubSpotRig.DealId}", new { id = HubSpotRig.DealId, properties = new Dictionary<string, string?>() });

        await _rig.ReadAsync();

        Assert.Contains(_rig.Handler.Requests, r => r.Path == $"crm/v3/objects/2-77/{HubSpotRig.DealId}");
    }

    // Fehler

    [Fact]
    public async Task Missing_deal_is_a_business_problem_not_an_exception()
    {
        _rig.Handler.Get($"crm/v3/objects/0-3/{HubSpotRig.DealId}", HttpStatusCode.NotFound, """{"message":"Geheim"}""");

        var data = await _rig.ReadAsync();

        Assert.Equal([HubSpotCrmProjectSource.DealNotFound], data.Problems);
        Assert.Contains("Deal nicht gefunden", data.Problems[0]);
        Assert.Null(data.PlanningProjectId);
        Assert.Empty(data.Dokumente);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Server_and_auth_errors_are_technical_and_never_carry_the_response_body(HttpStatusCode status)
    {
        _rig.Handler.Get($"crm/v3/objects/0-3/{HubSpotRig.DealId}", status, """{"message":"Erika Mustermann, erika.mustermann@example.org"}""");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => _rig.ReadAsync());

        Assert.Equal(status, error.StatusCode);
        Assert.Contains(((int)status).ToString(), error.Message);
        Assert.DoesNotContain("Mustermann", error.Message);
        Assert.DoesNotContain("example.org", error.Message);
    }

    // Vollmacht im Zusammenspiel

    [Fact]
    public async Task Missing_vollmacht_is_a_problem_naming_the_pattern()
    {
        var rig = new HubSpotRig();

        var data = await rig.ReadAsync();

        Assert.Empty(data.Dokumente);
        Assert.Equal([HubSpotCrmProjectSource.VollmachtMissing("Vollmacht")], data.Problems);
    }

    // Keine personenbezogenen Daten im Log

    [Fact]
    public async Task Logs_and_problems_contain_no_personal_data()
    {
        var rig = new HubSpotRig();
        var deal = HubSpotRig.BaseDeal();
        deal[K.AnlageAdresse] = "Geheimgasse ohne Hausnummer";
        deal[K.Einspeiseart] = "Geheimwert-4711";
        deal[K.BetreiberVorname] = "Hans";
        deal[K.BetreiberNachname] = "Beispielmann";
        rig.SetDeal(deal);
        rig.AddAttachments("1001", "f1", "2026-11-01T10:00:00Z");
        rig.AddFile("f1", "Vollmacht_Erika_Mustermann", "pdf", "2026-11-01T10:00:00Z");

        var data = await rig.ReadAsync();

        var everything = rig.SourceLog.All + "\n" + rig.FinderLog.All + "\n" + string.Join('\n', data.Problems);
        Assert.NotEmpty(rig.SourceLog.Messages);
        Assert.NotEmpty(rig.FinderLog.Messages);
        foreach (var secret in new[]
                 {
                     "Erika", "Mustermann", "Beispielmann", "Hans", "erika.mustermann", "example.org", "DE02", "1203 0000", "555010",
                     "Beispielweg", "Geheimgasse", "Geheimwert", "Vollmacht_Erika",
                 })
            Assert.DoesNotContain(secret, everything);
    }

    [Fact]
    public async Task Log_names_the_deal_and_counts_but_no_values()
    {
        await _rig.ReadAsync();

        Assert.Contains(_rig.SourceLog.Messages, m => m.Contains(HubSpotRig.DealId));
    }
}
