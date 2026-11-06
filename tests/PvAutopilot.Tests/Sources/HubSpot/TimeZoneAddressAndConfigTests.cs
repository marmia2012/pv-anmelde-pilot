using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Sources;
using PvAutopilot.Sources.HubSpot;
using PvAutopilot.Sources.HubSpot.Mapping;
using K = PvAutopilot.Sources.HubSpot.HubSpotPropertyKeys;

namespace PvAutopilot.Tests.Sources.HubSpot;

public class HubSpotDateTimeZoneTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static long Epoch(int y, int m, int d, int h = 0, int min = 0) =>
        new DateTimeOffset(y, m, d, h, min, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private static DateOnly Parse(string value, TimeZoneInfo? zone = null)
    {
        Assert.True(HubSpotDate.TryParse(value, zone ?? Berlin, out var date));
        return date;
    }

    [Theory]
    [InlineData("2026-11-20T23:30:00Z", 2026, 11, 21)]
    [InlineData("2026-11-20T23:30:00.000Z", 2026, 11, 21)]
    [InlineData("2026-11-20T22:59:00Z", 2026, 11, 20)]
    [InlineData("2026-07-20T22:30:00Z", 2026, 7, 21)]
    [InlineData("2026-07-20T21:59:00Z", 2026, 7, 20)]
    [InlineData("2026-11-20T10:00:00Z", 2026, 11, 20)]
    [InlineData("2026-11-20T23:30:00", 2026, 11, 21)]
    public void Iso_timestamps_are_converted_to_the_instance_time_zone(string value, int y, int m, int d)
    {
        Assert.Equal(new DateOnly(y, m, d), Parse(value));
    }

    [Fact]
    public void Epoch_at_midnight_utc_is_a_plain_date_and_keeps_the_utc_day()
    {
        Assert.Equal(new DateOnly(2026, 11, 20), Parse(Epoch(2026, 11, 20).ToString()));
        Assert.Equal(new DateOnly(2026, 7, 20), Parse(Epoch(2026, 7, 20).ToString()));
    }

    [Fact]
    public void Epoch_with_time_of_day_is_converted_to_the_instance_time_zone()
    {
        Assert.Equal(new DateOnly(2026, 11, 21), Parse(Epoch(2026, 11, 20, 23, 30).ToString()));
        Assert.Equal(new DateOnly(2026, 11, 20), Parse(Epoch(2026, 11, 20, 12, 0).ToString()));
    }

    [Fact]
    public void Plain_date_string_is_unchanged()
    {
        Assert.Equal(new DateOnly(2026, 11, 20), Parse("2026-11-20"));
        Assert.Equal(new DateOnly(2026, 11, 20), Parse("2026-11-20", TimeZoneInfo.Utc));
    }

    [Fact]
    public void Utc_zone_keeps_the_utc_day()
    {
        Assert.Equal(new DateOnly(2026, 11, 20), Parse("2026-11-20T23:30:00Z", TimeZoneInfo.Utc));
    }

    // Webhook (IsDate-Regel)

    private static IReadOnlyList<ProcessTrigger> Webhook(string value, string? timeZone = null)
    {
        var options = new HubSpotOptions
        {
            TriggerRules = [new() { Kind = ProcessTriggerKind.EinspeisezusageReceived, PropertyName = "zusage_datum", IsDate = true }],
        };
        if (timeZone is not null)
            options.TimeZone = timeZone;
        var body = $$"""[{"subscriptionType": "deal.propertyChange", "objectId": 7, "propertyName": "zusage_datum", "propertyValue": "{{value}}"}]""";
        return new HubSpotTriggerEvaluator(HubSpotRig.Wrap(options), NullLogger<HubSpotTriggerEvaluator>.Instance).Evaluate(body);
    }

    [Theory]
    [InlineData("2026-11-20T23:30:00Z", 2026, 11, 21)]
    [InlineData("2026-11-20", 2026, 11, 20)]
    public void Webhook_date_rule_uses_the_instance_time_zone(string value, int y, int m, int d)
    {
        Assert.Equal(new DateOnly(y, m, d), Assert.Single(Webhook(value)).Date);
    }

    [Fact]
    public void Webhook_epoch_midnight_utc_is_the_utc_day_and_epoch_with_time_the_next_day()
    {
        Assert.Equal(new DateOnly(2026, 11, 20), Assert.Single(Webhook(Epoch(2026, 11, 20).ToString())).Date);
        Assert.Equal(new DateOnly(2026, 11, 21), Assert.Single(Webhook(Epoch(2026, 11, 20, 23, 30).ToString())).Date);
    }

    [Fact]
    public void Webhook_honors_a_configured_time_zone()
    {
        Assert.Equal(new DateOnly(2026, 11, 20), Assert.Single(Webhook("2026-11-20T23:30:00Z", "UTC")).Date);
    }

    // DealMapper

    private static async Task<CrmProjectData> Read(Action<Dictionary<string, string?>> edit, Action<HubSpotOptions>? options = null)
    {
        var rig = new HubSpotRig();
        rig.AddAttachments("1001", "f1", "2026-11-01T10:00:00Z");
        rig.AddFile("f1", "Vollmacht", "pdf", "2026-11-01T10:00:00Z");
        options?.Invoke(rig.Options);
        var deal = HubSpotRig.BaseDeal();
        edit(deal);
        rig.SetDeal(deal);
        return await rig.ReadAsync();
    }

    [Fact]
    public async Task Installation_date_is_converted_to_the_instance_time_zone_before_comparing()
    {
        var data = await Read(d =>
        {
            d[K.TerminElektroinstallation] = "2026-11-20T23:30:00Z";
            d[K.TerminDachmontage] = Epoch(2026, 11, 20).ToString();
        });

        Assert.Equal(new DateOnly(2026, 11, 21), data.Installationstermin);
    }

    [Fact]
    public async Task Installation_date_from_epoch_midnight_is_the_utc_day()
    {
        var data = await Read(d => d[K.TerminDachmontage] = Epoch(2026, 11, 20).ToString());

        Assert.Equal(new DateOnly(2026, 11, 20), data.Installationstermin);
    }

    [Fact]
    public async Task Installation_date_with_utc_configured_keeps_the_utc_day()
    {
        var data = await Read(d => d[K.TerminDachmontage] = "2026-11-20T23:30:00Z", o => o.TimeZone = "UTC");

        Assert.Equal(new DateOnly(2026, 11, 20), data.Installationstermin);
    }

    // Konfiguration

    [Fact]
    public void Default_time_zone_is_berlin_and_valid()
    {
        Assert.Equal("Europe/Berlin", new HubSpotOptions().TimeZone);
        Assert.Empty(HubSpotOptionsValidator.Errors(HubSpotRig.DefaultOptions()));
    }

    [Theory]
    [InlineData("Mars/Olympus")]
    [InlineData("")]
    [InlineData("  ")]
    public void Unknown_or_empty_time_zone_is_a_startup_error(string zone)
    {
        var options = HubSpotRig.DefaultOptions();
        options.TimeZone = zone;

        Assert.Contains(HubSpotOptionsValidator.Errors(options), e => e.Contains("TimeZone"));
    }

    [Fact]
    public void Unknown_time_zone_in_configuration_fails_on_resolve()
    {
        using var provider = Provider(("HubSpot:TimeZone", "Mars/Olympus"));

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<HubSpotOptions>>().Value);
        Assert.Contains("TimeZone", error.Message);
    }

    internal static ServiceProvider Provider(params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["HubSpot:TriggerRules:0:Kind"] = "Start",
            ["HubSpot:TriggerRules:0:PropertyName"] = "dealstage",
            ["HubSpot:TriggerRules:0:Values:0"] = "stage-start",
        };
        foreach (var (key, value) in settings)
            values[key] = value;
        return new ServiceCollection().AddLogging()
            .AddHubSpot(new ConfigurationBuilder().AddInMemoryCollection(values).Build())
            .BuildServiceProvider();
    }

    // Veralteter Schlüssel ProjectObjectTypeId

    [Fact]
    public void Deprecated_project_object_type_key_warns_but_is_no_startup_error()
    {
        var options = HubSpotRig.DefaultOptions();
        options.ProjectObjectTypeId = "0-970";

        Assert.Empty(HubSpotOptionsValidator.Errors(options));
        Assert.Contains(HubSpotOptionsValidator.Warnings(options), w => w.Contains("ProjectObjectTypeId") && w.Contains("veraltet"));
    }

    [Fact]
    public void Without_the_deprecated_key_there_is_no_warning()
    {
        Assert.DoesNotContain(HubSpotOptionsValidator.Warnings(HubSpotRig.DefaultOptions()), w => w.Contains("ProjectObjectTypeId"));
    }

    [Fact]
    public void Deprecated_key_in_configuration_resolves_without_error_and_does_not_change_the_object_type()
    {
        using var provider = Provider(("HubSpot:ProjectObjectTypeId", "0-970"));

        var o = provider.GetRequiredService<IOptions<HubSpotOptions>>().Value;

        Assert.Equal("0-3", o.ObjectTypeId);
    }

    [Fact]
    public async Task Startup_check_logs_the_warning_for_the_deprecated_key()
    {
        var options = HubSpotRig.DefaultOptions();
        options.ProjectObjectTypeId = "0-970";
        var log = new CapturingLogger<HubSpotTriggerRulesStartupCheck>();

        await new HubSpotTriggerRulesStartupCheck(HubSpotRig.Wrap(options), log).StartAsync(default);

        Assert.Contains(log.Messages, m => m.StartsWith("Warning") && m.Contains("ProjectObjectTypeId"));
    }

    // Betreiberadresse

    [Fact]
    public async Task Operator_address_is_the_customer_address_even_for_a_different_operator()
    {
        var data = await Read(d =>
        {
            d[K.BetreiberVorname] = "Hans";
            d[K.BetreiberNachname] = "Beispiel";
        });

        var kunde = new PostalAddress("Beispielweg", "7", "47051", "Duisburg");
        Assert.Equal(kunde, data.Betreiberadresse);
        Assert.Equal(kunde, data.Anlagenadresse);
        Assert.Empty(data.Problems);
    }

    [Fact]
    public async Task Different_plant_location_keeps_the_customer_address_for_the_operator()
    {
        var data = await Read(d =>
        {
            d[K.AnlageStrasse] = "Sonnenallee";
            d[K.AnlageHausnummer] = "3b";
            d[K.AnlagePlz] = "47137";
            d[K.AnlageOrt] = "Duisburg";
        });

        Assert.Empty(data.Problems);
        Assert.Equal(new PostalAddress("Beispielweg", "7", "47051", "Duisburg"), data.Betreiberadresse);
        Assert.Equal(new PostalAddress("Sonnenallee", "3b", "47137", "Duisburg"), data.Anlagenadresse);
    }

    [Fact]
    public async Task Missing_customer_address_with_a_different_plant_location_is_no_problem()
    {
        var data = await Read(d =>
        {
            foreach (var key in new[] { K.KundeStrasse, K.KundeHausnummer, K.KundePlz, K.KundeOrt })
                d.Remove(key);
            d[K.AnlageAdresse] = "Sonnenallee 3b, 47137 Duisburg";
        });

        Assert.Empty(data.Problems);
        Assert.Null(data.Betreiberadresse);
        Assert.Equal(new PostalAddress("Sonnenallee", "3b", "47137", "Duisburg"), data.Anlagenadresse);
    }

    [Fact]
    public async Task Incomplete_customer_address_with_a_different_plant_location_is_no_problem()
    {
        var data = await Read(d =>
        {
            d.Remove(K.KundeOrt);
            d[K.AnlageAdresse] = "Sonnenallee 3b, 47137 Duisburg";
        });

        Assert.Empty(data.Problems);
        Assert.Null(data.Betreiberadresse);
    }

    [Fact]
    public async Task Missing_customer_address_without_plant_location_is_still_a_problem()
    {
        var data = await Read(d =>
        {
            foreach (var key in new[] { K.KundeStrasse, K.KundeHausnummer, K.KundePlz, K.KundeOrt })
                d.Remove(key);
        });

        Assert.Null(data.Anlagenadresse);
        Assert.NotEmpty(data.Problems);
    }

    private static async Task<DataCardAggregation> Aggregate(CrmProjectData crm) =>
        await new NabDataCardAggregator(new Crm(crm), new Planning(), new NoDocuments(), Options.Create(new ErrichterOptions { Firma = "Instanz GmbH", Ort = "Duisburg" }))
            .AggregateAsync("crm-1", default);

    private static CrmProjectData BaseCrm() => new()
    {
        PlanningProjectId = "reonic-1",
        Anlagenbetreiber = TestData.Card().Anlagenbetreiber,
        Anlagenadresse = new PostalAddress("Sonnenallee", "3b", "47137", "Duisburg"),
    };

    [Fact]
    public async Task Aggregator_uses_the_operator_address_when_present()
    {
        var kunde = new PostalAddress("Beispielweg", "7", "47051", "Duisburg");

        var result = await Aggregate(BaseCrm() with { Betreiberadresse = kunde });

        Assert.True(result.IsComplete);
        Assert.Equal(kunde, result.Card!.Betreiberadresse);
        Assert.Equal(new PostalAddress("Sonnenallee", "3b", "47137", "Duisburg"), result.Card.Anlagenadresse);
    }

    [Fact]
    public async Task Aggregator_falls_back_to_the_plant_address_without_a_problem()
    {
        var result = await Aggregate(BaseCrm());

        Assert.True(result.IsComplete);
        Assert.Empty(result.MissingFields);
        Assert.Equal(result.Card!.Anlagenadresse, result.Card.Betreiberadresse);
    }

    private sealed class Crm(CrmProjectData data) : ICrmProjectSource
    {
        public Task<CrmProjectData> GetProjectAsync(string crmProjectId, CancellationToken ct) => Task.FromResult(data);
    }

    private sealed class Planning : IPlanningDataSource
    {
        public Task<PlanningData> GetPlanningDataAsync(string planningProjectId, CancellationToken ct)
        {
            var card = TestData.Card();
            return Task.FromResult(new PlanningData
            {
                Installationsart = Installationsart.Neuerrichtung,
                Hauptzaehlernummer = card.Hauptzaehlernummer,
                Module = card.Module,
                Wechselrichter = card.Wechselrichter,
            });
        }
    }

    private sealed class NoDocuments : IProductDocumentSource
    {
        public Task<IReadOnlyList<DocumentRef>> GetProductDocumentsAsync(string hersteller, string modell, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DocumentRef>>([]);
    }

    // AddressParser: Land ohne Trenner, Straßenfeld mit Nummer

    [Theory]
    [InlineData("Musterstraße 12, 47051 Duisburg Deutschland")]
    [InlineData("Musterstraße 12, 47051 Duisburg Germany")]
    [InlineData("Musterstraße 12, 47051 Duisburg DE")]
    [InlineData("Musterstraße 12 47051 Duisburg deutschland")]
    [InlineData("Musterstraße 12, 47051 Duisburg, Deutschland")]
    public void Trailing_country_is_cut_off_with_or_without_separator(string line)
    {
        Assert.Equal(new PostalAddress("Musterstraße", "12", "47051", "Duisburg"), AddressParser.ParseSingleLine(line));
    }

    [Fact]
    public void City_with_several_words_keeps_all_words()
    {
        Assert.Equal(new PostalAddress("Musterstraße", "12", "47051", "Duisburg Neudorf"),
            AddressParser.ParseSingleLine("Musterstraße 12, 47051 Duisburg Neudorf Deutschland"));
    }

    [Fact]
    public async Task Street_ending_in_a_number_with_a_house_number_field_is_a_problem_naming_only_fields()
    {
        var data = await Read(d =>
        {
            d[K.KundeStrasse] = "Geheimweg 12";
            d[K.KundeHausnummer] = "14";
        });

        Assert.Null(data.Anlagenadresse);
        var problem = Assert.Single(data.Problems);
        Assert.Contains("Straßenfeld", problem);
        Assert.Contains(K.KundeHausnummer, problem);
        Assert.DoesNotContain("Geheimweg", problem);
        Assert.DoesNotContain("12", problem);
        Assert.DoesNotContain("14", problem);
    }

    [Theory]
    [InlineData("Bahnstraße", "12")]
    [InlineData("Straße des 17. Juni", "5")]
    public async Task Street_without_a_trailing_number_with_a_house_number_field_is_fine(string street, string number)
    {
        var data = await Read(d =>
        {
            d[K.KundeStrasse] = street;
            d[K.KundeHausnummer] = number;
        });

        Assert.Empty(data.Problems);
        Assert.Equal(new PostalAddress(street, number, "47051", "Duisburg"), data.Anlagenadresse);
    }

    private static long Epoch(int y, int m, int d) => new DateTimeOffset(y, m, d, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
}
