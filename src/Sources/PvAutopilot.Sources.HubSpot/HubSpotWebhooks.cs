using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
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

/// <summary>Übersetzt HubSpot-Webhook-Ereignisse anhand von <see cref="HubSpotOptions.TriggerRules"/> in Prozess-Trigger.</summary>
public sealed class HubSpotTriggerEvaluator(IOptions<HubSpotOptions> options, ILogger<HubSpotTriggerEvaluator> logger)
{
    public IReadOnlyList<ProcessTrigger> Evaluate(string body)
    {
        var o = options.Value;
        var rules = o.TriggerRules.Where(r => r.IsActive).ToList();
        var events = JsonSerializer.Deserialize<List<WebhookEvent>>(body) ?? [];
        var triggers = new List<ProcessTrigger>();

        foreach (var e in events)
        {
            if (e.ObjectId is null || string.IsNullOrWhiteSpace(e.PropertyValue))
                continue;
            if (!string.IsNullOrEmpty(o.ProjectObjectTypeId) && e.ObjectTypeId is { } type && type != o.ProjectObjectTypeId)
                continue;

            var id = e.ObjectId.Value.ToString();
            foreach (var rule in rules.Where(r => Matches(r, e)))
            {
                if (ToTrigger(rule, id, e.PropertyValue, OccurredAt(e)) is { } trigger)
                    triggers.Add(trigger);
            }
        }

        // HubSpot liefert Ereignisse mehrfach; die Verarbeitung ist idempotent, doppelte trotzdem filtern.
        return triggers.Distinct().ToList();
    }

    private static bool Matches(HubSpotTriggerRule rule, WebhookEvent e) =>
        string.Equals(rule.PropertyName, e.PropertyName, StringComparison.Ordinal)
        && (rule.Values.Count == 0 || rule.Values.Contains(e.PropertyValue!.Trim(), StringComparer.OrdinalIgnoreCase));

    /// <summary>Zeitpunkt der Änderung in HubSpot (Epoch-Millisekunden); fehlt er oder ist er ungültig, <c>null</c>.</summary>
    private static DateTimeOffset? OccurredAt(WebhookEvent e)
    {
        if (e.OccurredAt is not { } ms)
            return null;
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private ProcessTrigger? ToTrigger(HubSpotTriggerRule rule, string crmProjectId, string value, DateTimeOffset? occurredAt)
    {
        DateOnly? date = null;
        if (rule.IsDate)
        {
            if (!HubSpotDate.TryParse(value, out var parsed))
            {
                // Wert nicht loggen: Freitext-Properties können personenbezogene Daten enthalten.
                logger.LogWarning("{TriggerKind} für Projekt {CrmProjectId} ignoriert: {PropertyName} ist kein gültiges Datum",
                    rule.Kind, crmProjectId, rule.PropertyName);
                return null;
            }
            date = parsed;
        }

        return new ProcessTrigger(rule.Kind, crmProjectId)
        {
            Submission = rule.Submission,
            Resolution = rule.Resolution,
            Date = date,
            OccurredAt = occurredAt,
        };
    }

    private sealed record WebhookEvent(
        [property: JsonPropertyName("objectId")] long? ObjectId,
        [property: JsonPropertyName("objectTypeId")] string? ObjectTypeId,
        [property: JsonPropertyName("propertyName")] string? PropertyName,
        [property: JsonPropertyName("propertyValue")] string? PropertyValue,
        [property: JsonPropertyName("occurredAt")] long? OccurredAt);
}
