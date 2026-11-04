using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Processes;

namespace PvAutopilot.Sources.HubSpot;

/// <summary>Prüft die HubSpot-Webhook-Signatur v3 (HMAC-SHA256 über Methode + URI + Body + Timestamp).</summary>
public sealed class HubSpotWebhookValidator(IOptions<HubSpotOptions> options, TimeProvider time)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    public bool IsValid(string method, string uri, string body, string? signature, string? timestamp)
    {
        var secret = options.Value.ClientSecret;
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature) || !long.TryParse(timestamp, out var ms))
            return false;

        if (time.GetUtcNow() - DateTimeOffset.FromUnixTimeMilliseconds(ms) > MaxAge)
            return false;

        var source = Encoding.UTF8.GetBytes(method + uri + body + timestamp);
        var expected = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), source));
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature));
    }
}

/// <summary>Übersetzt HubSpot-Webhook-Ereignisse in Prozess-Trigger.</summary>
public sealed class HubSpotTriggerEvaluator(IOptions<HubSpotOptions> options)
{
    public IReadOnlyList<ProcessTrigger> Evaluate(string body)
    {
        var o = options.Value;
        var events = JsonSerializer.Deserialize<List<WebhookEvent>>(body) ?? [];
        var triggers = new List<ProcessTrigger>();

        foreach (var e in events)
        {
            if (e.ObjectId is null)
                continue;
            if (!string.IsNullOrEmpty(o.ProjectObjectTypeId) && e.ObjectTypeId is { } type && type != o.ProjectObjectTypeId)
                continue;

            var id = e.ObjectId.Value.ToString();
            if (e.PropertyName == o.TriggerPropertyName && o.TriggerPropertyValues.Contains(e.PropertyValue ?? ""))
                triggers.Add(new ProcessTrigger(ProcessTriggerKind.Start, id));
            else if (!string.IsNullOrEmpty(o.ReviewApprovalPropertyName)
                     && e.PropertyName == o.ReviewApprovalPropertyName
                     && e.PropertyValue == o.ReviewApprovalPropertyValue)
                triggers.Add(new ProcessTrigger(ProcessTriggerKind.ReviewApproved, id));
        }

        // HubSpot liefert Ereignisse mehrfach; die Verarbeitung ist idempotent, doppelte trotzdem filtern.
        return triggers.Distinct().ToList();
    }

    private sealed record WebhookEvent(
        [property: JsonPropertyName("objectId")] long? ObjectId,
        [property: JsonPropertyName("objectTypeId")] string? ObjectTypeId,
        [property: JsonPropertyName("propertyName")] string? PropertyName,
        [property: JsonPropertyName("propertyValue")] string? PropertyValue);
}
