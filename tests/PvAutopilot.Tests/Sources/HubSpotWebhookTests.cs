using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PvAutopilot.Sources.HubSpot;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Tests.Sources;

public class HubSpotWebhookTests
{
    private const string Secret = "test-secret";
    private const string Uri = "https://autopilot.example.org/webhooks/hubspot";

    private static readonly HubSpotOptions Options = new()
    {
        ClientSecret = Secret,
        TriggerRules =
        [
            new() { Kind = ProcessTriggerKind.Start, PropertyName = "hs_pipeline_stage", Values = ["stage-vollmacht"] },
            new()
            {
                Kind = ProcessTriggerKind.ReviewApproved, Submission = SubmissionKind.Netzanmeldung,
                PropertyName = "anmeldung_freigegeben", Values = ["true"],
            },
        ],
    };

    [Fact]
    public void Valid_signature_is_accepted()
    {
        var (validator, timestamp) = Validator();
        const string body = "[]";

        Assert.True(validator.IsValid("POST", Uri, body, Sign("POST", body, timestamp), timestamp));
    }

    [Fact]
    public void Tampered_body_is_rejected()
    {
        var (validator, timestamp) = Validator();

        Assert.False(validator.IsValid("POST", Uri, "[{}]", Sign("POST", "[]", timestamp), timestamp));
    }

    [Fact]
    public void Old_timestamp_is_rejected()
    {
        var time = new FixedTime(TestData.Now);
        var validator = new HubSpotWebhookValidator(Microsoft.Extensions.Options.Options.Create(Options), time);
        var timestamp = TestData.Now.AddMinutes(-6).ToUnixTimeMilliseconds().ToString();

        Assert.False(validator.IsValid("POST", Uri, "[]", Sign("POST", "[]", timestamp), timestamp));
    }

    [Fact]
    public void Evaluator_maps_stage_change_and_approval()
    {
        var evaluator = new HubSpotTriggerEvaluator(Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<HubSpotTriggerEvaluator>.Instance);
        const string body = """
            [
              {"objectId": 11, "propertyName": "hs_pipeline_stage", "propertyValue": "stage-vollmacht"},
              {"objectId": 11, "propertyName": "hs_pipeline_stage", "propertyValue": "stage-vollmacht"},
              {"objectId": 12, "propertyName": "hs_pipeline_stage", "propertyValue": "andere-stage"},
              {"objectId": 13, "propertyName": "anmeldung_freigegeben", "propertyValue": "true"}
            ]
            """;

        var triggers = evaluator.Evaluate(body);

        Assert.Equal(
            [
                new ProcessTrigger(ProcessTriggerKind.Start, "11"),
                new ProcessTrigger(ProcessTriggerKind.ReviewApproved, "13") { Submission = SubmissionKind.Netzanmeldung },
            ],
            triggers);
    }

    private static (HubSpotWebhookValidator, string) Validator() =>
        (new HubSpotWebhookValidator(Microsoft.Extensions.Options.Options.Create(Options), new FixedTime(TestData.Now)),
         TestData.Now.ToUnixTimeMilliseconds().ToString());

    private static string Sign(string method, string body, string timestamp) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(method + Uri + body + timestamp)));
}
