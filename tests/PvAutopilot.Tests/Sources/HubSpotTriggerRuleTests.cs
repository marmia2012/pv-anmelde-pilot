using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;
using PvAutopilot.Sources.HubSpot;

namespace PvAutopilot.Tests.Sources;

public class HubSpotTriggerRuleTests
{
    private static readonly HubSpotOptions Options = new()
    {
        TriggerRules =
        [
            new() { Kind = ProcessTriggerKind.Start, PropertyName = "hs_pipeline_stage", Values = ["stage-vollmacht"] },
            new() { Kind = ProcessTriggerKind.Cancelled, PropertyName = "hs_pipeline_stage", Values = ["stage-storno"] },
            new() { Kind = ProcessTriggerKind.ReviewApproved, Submission = SubmissionKind.Inbetriebnahme, PropertyName = "e8_freigabe", Values = ["true"] },
            new() { Kind = ProcessTriggerKind.EinspeisezusageReceived, PropertyName = "zusage_datum", IsDate = true },
            new() { Kind = ProcessTriggerKind.RueckfrageResolved, Resolution = RueckfrageResolution.AnsweredDirectly, PropertyName = "rueckfrage_erledigt", Values = ["direkt"] },
            new() { Kind = ProcessTriggerKind.RueckfrageResolved, Resolution = RueckfrageResolution.Resubmit, PropertyName = "rueckfrage_erledigt", Values = ["neu"] },
            new() { Kind = ProcessTriggerKind.MastrCredentialsProvided, PropertyName = "mastr_benutzer" },
            // Noch nicht konfiguriert: darf nie auslösen.
            new() { Kind = ProcessTriggerKind.InbetriebnahmeReported, PropertyName = "", IsDate = true },
        ],
    };

    private static IReadOnlyList<ProcessTrigger> Evaluate(string body, HubSpotOptions? options = null, ILogger<HubSpotTriggerEvaluator>? logger = null) =>
        new HubSpotTriggerEvaluator(Microsoft.Extensions.Options.Options.Create(options ?? Options),
            logger ?? NullLogger<HubSpotTriggerEvaluator>.Instance).Evaluate(body);

    private static string Event(string property, string? value) =>
        $$"""[{"objectId": 7, "propertyName": "{{property}}", "propertyValue": {{(value is null ? "null" : $"\"{value}\"")}}}]""";

    [Fact]
    public void Stage_rules_only_fire_for_their_values()
    {
        Assert.Equal([new ProcessTrigger(ProcessTriggerKind.Start, "7")], Evaluate(Event("hs_pipeline_stage", "stage-vollmacht")));
        Assert.Equal([new ProcessTrigger(ProcessTriggerKind.Cancelled, "7")], Evaluate(Event("hs_pipeline_stage", "stage-storno")));
        Assert.Empty(Evaluate(Event("hs_pipeline_stage", "andere-stage")));
    }

    [Fact]
    public void Approval_carries_the_submission_kind()
    {
        var trigger = Assert.Single(Evaluate(Event("e8_freigabe", "TRUE")));

        Assert.Equal(ProcessTriggerKind.ReviewApproved, trigger.Kind);
        Assert.Equal(SubmissionKind.Inbetriebnahme, trigger.Submission);
        Assert.Empty(Evaluate(Event("e8_freigabe", "false")));
    }

    [Fact]
    public void Occurred_at_of_the_webhook_event_is_carried_over()
    {
        var body = """[{"objectId": 7, "propertyName": "e8_freigabe", "propertyValue": "true", "occurredAt": 1793793600000}]""";

        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793793600000), Assert.Single(Evaluate(body)).OccurredAt);
        Assert.Null(Assert.Single(Evaluate(Event("e8_freigabe", "true"))).OccurredAt);
        Assert.Null(Assert.Single(Evaluate(
            """[{"objectId": 7, "propertyName": "e8_freigabe", "propertyValue": "true", "occurredAt": 9223372036854775807}]""")).OccurredAt);
    }

    [Fact]
    public void Timestamp_properties_without_values_fire_on_every_set_but_not_on_clear()
    {
        // Empfehlung aus docs/konzept/prozesssteuerung.md: Freigabe/Erledigung als Datum-/Zeitstempel-Property,
        // dann ist jedes Setzen eine Änderung und löst ohne vorheriges Zurücksetzen aus.
        var options = new HubSpotOptions
        {
            TriggerRules =
            [
                new() { Kind = ProcessTriggerKind.Start, PropertyName = "hs_pipeline_stage", Values = ["stage-vollmacht"] },
                new() { Kind = ProcessTriggerKind.ReviewApproved, Submission = SubmissionKind.Netzanmeldung, PropertyName = "na_freigegeben_am" },
                new() { Kind = ProcessTriggerKind.RueckfrageResolved, Resolution = RueckfrageResolution.AnsweredDirectly, PropertyName = "rf_beantwortet_am" },
                new() { Kind = ProcessTriggerKind.RueckfrageResolved, Resolution = RueckfrageResolution.Resubmit, PropertyName = "rf_neu_einreichen_am" },
            ],
        };
        Assert.Empty(HubSpotOptionsValidator.Errors(options));
        Assert.Empty(HubSpotOptionsValidator.Warnings(options));

        var approval = Assert.Single(Evaluate(Event("na_freigegeben_am", "1793793600000"), options));
        Assert.Equal((ProcessTriggerKind.ReviewApproved, SubmissionKind.Netzanmeldung), (approval.Kind, approval.Submission));
        Assert.Equal(RueckfrageResolution.AnsweredDirectly,
            Assert.Single(Evaluate(Event("rf_beantwortet_am", "1793793600000"), options)).Resolution);
        Assert.Equal(RueckfrageResolution.Resubmit,
            Assert.Single(Evaluate(Event("rf_neu_einreichen_am", "2026-11-04T10:00:00Z"), options)).Resolution);

        Assert.Empty(Evaluate(Event("na_freigegeben_am", ""), options));
        Assert.Empty(Evaluate(Event("na_freigegeben_am", null), options));
    }

    [Fact]
    public void Two_rules_on_one_property_map_the_resolution()
    {
        Assert.Equal(RueckfrageResolution.AnsweredDirectly, Assert.Single(Evaluate(Event("rueckfrage_erledigt", "direkt"))).Resolution);
        Assert.Equal(RueckfrageResolution.Resubmit, Assert.Single(Evaluate(Event("rueckfrage_erledigt", "neu"))).Resolution);
    }

    [Theory]
    [InlineData("1794787200000")] // 2026-11-16T00:00:00Z
    [InlineData("2026-11-16")]
    public void Date_rules_parse_epoch_millis_and_iso_dates(string value)
    {
        var trigger = Assert.Single(Evaluate(Event("zusage_datum", value)));

        Assert.Equal(ProcessTriggerKind.EinspeisezusageReceived, trigger.Kind);
        Assert.Equal(new DateOnly(2026, 11, 16), trigger.Date);
    }

    [Fact]
    public void Invalid_date_is_ignored_and_logged_without_the_value()
    {
        var logger = new CollectingLogger();

        Assert.Empty(Evaluate(Event("zusage_datum", "16.11.2026 Frau Geheim"), logger: logger));

        var message = Assert.Single(logger.Messages);
        Assert.Contains("zusage_datum", message);
        Assert.DoesNotContain("Geheim", message);
    }

    [Fact]
    public void Empty_values_and_unconfigured_rules_never_fire()
    {
        // Jeder nicht-leere Wert löst aus, ein geleertes Feld nicht.
        Assert.Single(Evaluate(Event("mastr_benutzer", "irgendwer")));
        Assert.Empty(Evaluate(Event("mastr_benutzer", "")));
        Assert.Empty(Evaluate(Event("mastr_benutzer", null)));
        Assert.Empty(Evaluate(Event("", "2026-11-16")));
    }

    [Fact]
    public void Start_rule_without_values_stays_inactive()
    {
        var options = new HubSpotOptions { TriggerRules = [new() { Kind = ProcessTriggerKind.Start, PropertyName = "hs_pipeline_stage" }] };

        Assert.Empty(Evaluate(Event("hs_pipeline_stage", "beliebige-stage"), options));
        Assert.Contains(HubSpotOptionsValidator.Warnings(options), w => w.Contains("keine Values"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("16.11.2026")]
    [InlineData("2026-13-01")]
    [InlineData("-1x")]
    [InlineData("99999999999999999")]
    public void HubSpotDate_rejects_invalid_values(string value) => Assert.False(HubSpotDate.TryParse(value, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"), out _));

    [Fact]
    public void Validation_requires_a_start_rule_and_kind_specific_fields()
    {
        var errors = HubSpotOptionsValidator.Errors(new HubSpotOptions
        {
            TriggerRules =
            [
                new() { Kind = ProcessTriggerKind.Start, PropertyName = "" },
                new() { Kind = ProcessTriggerKind.ReviewApproved, PropertyName = "freigabe" },
                new() { Kind = ProcessTriggerKind.RueckfrageResolved, PropertyName = "erledigt" },
            ],
        });

        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.Contains("Kind=Start"));
        Assert.Contains(errors, e => e.Contains("Submission"));
        Assert.Contains(errors, e => e.Contains("Resolution"));
        Assert.Empty(HubSpotOptionsValidator.Errors(Options));
    }

    [Fact]
    public void Rules_bind_from_configuration_and_are_validated_on_resolve()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HubSpot:TriggerRules:0:Kind"] = "Start",
            ["HubSpot:TriggerRules:0:PropertyName"] = "hs_pipeline_stage",
            ["HubSpot:TriggerRules:0:Values:0"] = "stage-vollmacht",
            ["HubSpot:TriggerRules:1:Kind"] = "ReviewApproved",
            ["HubSpot:TriggerRules:1:Submission"] = "Mastr",
            ["HubSpot:TriggerRules:1:PropertyName"] = "mastr_freigabe",
            ["HubSpot:TriggerRules:2:Kind"] = "ZaehlerwechselCompleted",
            ["HubSpot:TriggerRules:2:PropertyName"] = "zaehlerwechsel_datum",
            ["HubSpot:TriggerRules:2:IsDate"] = "true",
        }).Build();
        using var provider = new ServiceCollection().AddLogging().AddHubSpot(configuration).BuildServiceProvider();

        var rules = provider.GetRequiredService<IOptions<HubSpotOptions>>().Value.TriggerRules;

        Assert.Equal(3, rules.Count);
        Assert.Equal(SubmissionKind.Mastr, rules[1].Submission);
        Assert.True(rules[2].IsDate);
    }

    [Fact]
    public void Missing_start_rule_fails_on_resolve()
    {
        using var provider = new ServiceCollection().AddLogging()
            .AddHubSpot(new ConfigurationBuilder().Build()).BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<HubSpotOptions>>().Value);
        Assert.Contains("Kind=Start", error.Message);
    }

    private sealed class CollectingLogger : ILogger<HubSpotTriggerEvaluator>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
