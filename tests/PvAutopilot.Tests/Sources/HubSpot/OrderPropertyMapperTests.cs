using System.Globalization;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Orders;
using PvAutopilot.Sources.HubSpot;
using PvAutopilot.Sources.HubSpot.Mapping;
using K = PvAutopilot.Sources.HubSpot.HubSpotPropertyKeys;

namespace PvAutopilot.Tests.Sources.HubSpot;

public class OrderPropertyMapperTests
{
    private static HubSpotOptions Options() => HubSpotRig.DefaultOptions();

    private static SignedOrder Order(OrderSummary? summary = null, DateTimeOffset? signedAt = null) => new()
    {
        PlanningProjectId = "p1",
        SignatureRequestId = "s1",
        IsSigned = true,
        SignedAt = signedAt,
        Summary = summary ?? OrderSummary.Empty,
    };

    private static OrderSummary FullSummary() => new(
        [new ModulPosition("Hersteller A", "Modul 450", 450m, 20m)],
        [new WechselrichterPosition("Hersteller B", "WR 8K", null, 8000m, 1m, null)],
        new Batteriespeicher("Hersteller C", "Speicher 2.56", 10.24m, null),
        new Wallbox("Hersteller D", "Wallbox 11", 11m));

    private static Dictionary<string, string> Map(SignedOrder order, HubSpotOptions? options = null) =>
        OrderPropertyMapper.Map(order, options ?? Options());

    private static T WithCulture<T>(string culture, Func<T> action)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void Full_order_maps_every_property_in_the_documented_format()
    {
        var map = Map(Order(FullSummary(), new DateTimeOffset(2026, 11, 9, 23, 30, 0, TimeSpan.Zero)));

        Assert.Equal(new Dictionary<string, string>
        {
            ["auftrag_unterschriftsdatum"] = "2026-11-10",
            ["auftrag_kwp"] = "9",
            ["auftrag_module"] = "20 × Hersteller A Modul 450 (450 Wp)",
            ["auftrag_wechselrichter"] = "1 × Hersteller B WR 8K (8 kW)",
            ["auftrag_wr_leistung_kw"] = "8",
            ["auftrag_speicher_vorhanden"] = "true",
            ["auftrag_speicher"] = "Hersteller C Speicher 2.56 (10.24 kWh)",
            ["auftrag_speicher_kwh"] = "10.24",
            ["auftrag_wallbox_vorhanden"] = "true",
            ["auftrag_wallbox"] = "1 × Hersteller D Wallbox 11 (11 kW)",
            ["auftrag_wallbox_kw"] = "11",
        }, map);
    }

    [Theory]
    [InlineData("2026-11-09T23:30:00+00:00", "2026-11-10")] // Winter: 00:30 MEZ am Folgetag
    [InlineData("2026-11-09T22:59:00+00:00", "2026-11-09")] // 23:59 MEZ
    [InlineData("2026-11-09T23:00:00+00:00", "2026-11-10")] // 00:00 MEZ
    [InlineData("2026-07-01T22:30:00+00:00", "2026-07-02")] // Sommer: 00:30 MESZ
    [InlineData("2026-07-01T21:59:00+00:00", "2026-07-01")] // 23:59 MESZ
    [InlineData("2026-11-10T08:00:00+05:00", "2026-11-10")] // fremder Offset, 04:00 MEZ
    [InlineData("2026-11-10T01:00:00+05:00", "2026-11-09")] // 21:00 MEZ des Vortags
    public void Date_is_the_calendar_day_in_the_instance_time_zone(string signedAt, string expected)
    {
        var map = Map(Order(signedAt: DateTimeOffset.Parse(signedAt, CultureInfo.InvariantCulture)));

        Assert.Equal(expected, map["auftrag_unterschriftsdatum"]);
    }

    [Fact]
    public void Date_follows_the_configured_time_zone()
    {
        var options = Options();
        options.TimeZone = "UTC";

        var map = Map(Order(signedAt: new DateTimeOffset(2026, 11, 9, 23, 30, 0, TimeSpan.Zero)), options);

        Assert.Equal("2026-11-09", map["auftrag_unterschriftsdatum"]);
    }

    [Fact]
    public void Unknown_signing_time_writes_no_date() =>
        Assert.DoesNotContain("auftrag_unterschriftsdatum", Map(Order(FullSummary())).Keys);

    [Fact]
    public void Numbers_use_a_dot_even_with_german_culture()
    {
        var summary = new OrderSummary(
            [new ModulPosition("A", "M", 360m, 12m)],
            [new WechselrichterPosition("B", "W", null, 5500m, 1m, null)],
            new Batteriespeicher("C", "S", 5.12m, null),
            new Wallbox("D", "W", 11.5m, 2m));

        var map = WithCulture("de-DE", () => Map(Order(summary)));

        Assert.Equal("4.32", map["auftrag_kwp"]);
        Assert.Equal("5.5", map["auftrag_wr_leistung_kw"]);
        Assert.Equal("5.12", map["auftrag_speicher_kwh"]);
        Assert.Equal("11.5", map["auftrag_wallbox_kw"]);
        Assert.Equal("12 × A M (360 Wp)", map["auftrag_module"]);
        Assert.Equal("1 × B W (5.5 kW)", map["auftrag_wechselrichter"]);
        Assert.Equal("2 × D W (11.5 kW)", map["auftrag_wallbox"]);
    }

    [Fact]
    public void Kwp_is_the_sum_over_all_module_positions_and_texts_are_joined()
    {
        var summary = new OrderSummary(
            [new ModulPosition("A", "M1", 400m, 10m), new ModulPosition("B", "M2", 500m, 4m)],
            [new WechselrichterPosition("C", "W1", null, 4000m, 1m, null), new WechselrichterPosition("D", "W2", null, 3000m, 2m, null)],
            null, null);

        var map = Map(Order(summary));

        Assert.Equal("6", map["auftrag_kwp"]);
        Assert.Equal("10 × A M1 (400 Wp); 4 × B M2 (500 Wp)", map["auftrag_module"]);
        Assert.Equal("10", map["auftrag_wr_leistung_kw"]);
        Assert.Equal("1 × C W1 (4 kW); 2 × D W2 (3 kW)", map["auftrag_wechselrichter"]);
    }

    [Fact]
    public void Storage_and_wallbox_flags_are_true_when_present()
    {
        var map = Map(Order(FullSummary()));

        Assert.Equal("true", map["auftrag_speicher_vorhanden"]);
        Assert.Equal("true", map["auftrag_wallbox_vorhanden"]);
    }

    [Fact]
    public void Missing_storage_and_wallbox_write_false_and_no_detail_values()
    {
        var summary = FullSummary() with { Speicher = null, Wallbox = null };

        var map = Map(Order(summary));

        Assert.Equal("false", map["auftrag_speicher_vorhanden"]);
        Assert.Equal("false", map["auftrag_wallbox_vorhanden"]);
        foreach (var key in new[] { "auftrag_speicher", "auftrag_speicher_kwh", "auftrag_wallbox", "auftrag_wallbox_kw" })
            Assert.DoesNotContain(key, map.Keys);
    }

    [Fact]
    public void Storage_only_leaves_the_wallbox_false()
    {
        var map = Map(Order(FullSummary() with { Wallbox = null }));

        Assert.Equal("true", map["auftrag_speicher_vorhanden"]);
        Assert.Equal("false", map["auftrag_wallbox_vorhanden"]);
        Assert.Contains("auftrag_speicher_kwh", map.Keys);
        Assert.DoesNotContain("auftrag_wallbox_kw", map.Keys);
    }

    [Fact]
    public void Without_modules_and_inverters_no_power_texts_are_written()
    {
        var map = Map(Order(OrderSummary.Empty));

        foreach (var key in new[] { "auftrag_kwp", "auftrag_module", "auftrag_wechselrichter", "auftrag_wr_leistung_kw" })
            Assert.DoesNotContain(key, map.Keys);
        // Nur die Ja/Nein-Felder stehen immer drin.
        Assert.Equal(["auftrag_speicher_vorhanden", "auftrag_wallbox_vorhanden"], map.Keys.Order());
    }

    [Fact]
    public void Unconfigured_keys_are_missing()
    {
        var options = Options();
        options.Properties.Remove(K.AuftragKwp);
        options.Properties[K.AuftragWallbox] = "  ";
        options.Properties[K.AuftragSpeicherVorhanden] = "";

        var map = Map(Order(FullSummary(), TestData.Now), options);

        Assert.DoesNotContain("auftrag_kwp", map.Keys);
        Assert.DoesNotContain("auftrag_wallbox", map.Keys);
        Assert.DoesNotContain("auftrag_speicher_vorhanden", map.Keys);
        Assert.Equal("true", map["auftrag_wallbox_vorhanden"]);
        Assert.Equal(8, map.Count);
    }

    [Fact]
    public void Configured_names_are_used_as_they_are_configured()
    {
        var options = new HubSpotOptions { Properties = { [K.AuftragKwp] = " meine_kwp " } };

        var map = Map(Order(FullSummary()), options);

        Assert.Equal(new Dictionary<string, string> { ["meine_kwp"] = "9" }, map);
    }

    [Fact]
    public void No_auftrag_property_configured_gives_an_empty_map() =>
        Assert.Empty(Map(Order(FullSummary(), TestData.Now), new HubSpotOptions()));

    [Theory]
    [InlineData("1.5000", "1.5")]
    [InlineData("10", "10")]
    [InlineData("0.12349", "0.123")]
    [InlineData("0.1236", "0.124")]
    [InlineData("1234.5", "1234.5")]
    public void Number_has_at_most_three_decimals_without_trailing_zeros(string value, string expected) =>
        Assert.Equal(expected, WithCulture("de-DE", () => OrderPropertyMapper.Number(decimal.Parse(value, CultureInfo.InvariantCulture))));
}
