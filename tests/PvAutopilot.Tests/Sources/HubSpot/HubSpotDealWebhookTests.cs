using Microsoft.Extensions.Logging.Abstractions;
using PvAutopilot.Core.Processes;
using PvAutopilot.Sources.HubSpot;

namespace PvAutopilot.Tests.Sources.HubSpot;

/// <summary>Webhook-Ereignisse am Deal (<c>dealstage</c>) lösen Start und Storno aus.</summary>
public class HubSpotDealWebhookTests
{
    private static HubSpotOptions Options(string objectTypeId = "0-3") => new()
    {
        ObjectTypeId = objectTypeId,
        TriggerRules =
        [
            new() { Kind = ProcessTriggerKind.Start, PropertyName = "dealstage", Values = ["stage-start"] },
            new() { Kind = ProcessTriggerKind.Cancelled, PropertyName = "dealstage", Values = ["stage-storno"] },
        ],
    };

    private static IReadOnlyList<ProcessTrigger> Evaluate(string body, string objectTypeId = "0-3") =>
        new HubSpotTriggerEvaluator(HubSpotRig.Wrap(Options(objectTypeId)), NullLogger<HubSpotTriggerEvaluator>.Instance).Evaluate(body);

    private static string Event(string subscriptionType, string value, string? objectTypeId = null, string property = "dealstage") =>
        $$"""[{"subscriptionType": "{{subscriptionType}}", "objectId": 7, {{(objectTypeId is null ? "" : $"\"objectTypeId\": \"{objectTypeId}\", ")}}"propertyName": "{{property}}", "propertyValue": "{{value}}"}]""";

    [Fact]
    public void Deal_stage_change_to_the_start_stage_starts_the_process()
    {
        Assert.Equal([new ProcessTrigger(ProcessTriggerKind.Start, "7")], Evaluate(Event("deal.propertyChange", "stage-start")));
    }

    [Fact]
    public void Deal_stage_change_to_the_storno_stage_cancels_the_process()
    {
        Assert.Equal([new ProcessTrigger(ProcessTriggerKind.Cancelled, "7")], Evaluate(Event("deal.propertyChange", "stage-storno")));
    }

    [Fact]
    public void Other_deal_stages_trigger_nothing()
    {
        Assert.Empty(Evaluate(Event("deal.propertyChange", "stage-irgendwas")));
    }

    [Fact]
    public void Other_properties_trigger_nothing()
    {
        Assert.Empty(Evaluate(Event("deal.propertyChange", "stage-start", property: "hs_pipeline_stage")));
    }

    [Theory]
    [InlineData("contact.propertyChange")]
    [InlineData("company.propertyChange")]
    [InlineData("ticket.propertyChange")]
    [InlineData("deal.creation")]
    [InlineData("deal.deletion")]
    [InlineData("deal.associationChange")]
    [InlineData("contact.creation")]
    public void Other_subscription_types_are_ignored(string subscriptionType)
    {
        Assert.Empty(Evaluate(Event(subscriptionType, "stage-start")));
    }

    [Fact]
    public void Generic_object_subscription_with_the_configured_object_type_is_accepted()
    {
        Assert.Equal([new ProcessTrigger(ProcessTriggerKind.Start, "7")], Evaluate(Event("object.propertyChange", "stage-start", "0-3")));
    }

    [Fact]
    public void Generic_object_subscription_with_a_wrong_object_type_is_ignored()
    {
        Assert.Empty(Evaluate(Event("object.propertyChange", "stage-start", "0-1")));
        Assert.Empty(Evaluate(Event("object.propertyChange", "stage-start", "0-970")));
    }

    [Fact]
    public void Generic_object_subscription_without_an_object_type_is_ignored()
    {
        Assert.Empty(Evaluate(Event("object.propertyChange", "stage-start")));
    }

    [Fact]
    public void Deal_subscription_for_a_foreign_object_type_is_ignored()
    {
        Assert.Empty(Evaluate(Event("deal.propertyChange", "stage-start", "0-1")));
    }

    [Fact]
    public void Custom_object_type_needs_the_generic_subscription()
    {
        Assert.Equal([new ProcessTrigger(ProcessTriggerKind.Start, "7")], Evaluate(Event("object.propertyChange", "stage-start", "2-77"), "2-77"));
        Assert.Empty(Evaluate(Event("deal.propertyChange", "stage-start"), "2-77"));
        Assert.Empty(Evaluate(Event("object.propertyChange", "stage-start", "0-3"), "2-77"));
    }

    [Fact]
    public void Duplicate_deliveries_trigger_once_and_mixed_batches_are_filtered_per_event()
    {
        const string body = """
            [
              {"subscriptionType": "deal.propertyChange", "objectId": 7, "propertyName": "dealstage", "propertyValue": "stage-start"},
              {"subscriptionType": "deal.propertyChange", "objectId": 7, "propertyName": "dealstage", "propertyValue": "stage-start"},
              {"subscriptionType": "contact.propertyChange", "objectId": 8, "propertyName": "dealstage", "propertyValue": "stage-start"},
              {"subscriptionType": "deal.propertyChange", "objectId": 9, "propertyName": "dealstage", "propertyValue": "stage-storno"}
            ]
            """;

        Assert.Equal(
            [new ProcessTrigger(ProcessTriggerKind.Start, "7"), new ProcessTrigger(ProcessTriggerKind.Cancelled, "9")],
            Evaluate(body));
    }
}
