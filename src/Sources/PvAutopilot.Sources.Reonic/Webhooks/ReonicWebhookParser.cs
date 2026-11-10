using System.Text.Json;

namespace PvAutopilot.Sources.Reonic.Webhooks;

/// <summary>Ein Reonic-Webhook-Ereignis, nur die IDs (der Body selbst wird nie geloggt).</summary>
public sealed record ReonicWebhookEvent(string? Type, string? ProjectId, string? SignatureRequestId);

/// <summary>
/// Liest den Webhook-Body <c>{ version, type, occurredAt, data: { projectId, signatureRequestId, … } }</c>.
/// Fehlt <c>type</c> im Body, gilt der Header <c>X-Reonic-Event</c>.
/// </summary>
public static class ReonicWebhookParser
{
    public const string OfferSigned = "residentialOffer_signed";
    public const string SignatureWithdrawn = "residentialOffer_signatureWithdrawn";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <exception cref="JsonException">Body ist kein gültiges JSON-Objekt.</exception>
    public static ReonicWebhookEvent Parse(byte[] body, string? headerType)
    {
        var payload = JsonSerializer.Deserialize<Payload>(body, Json)
            ?? throw new JsonException("Reonic-Webhook: leerer Body.");
        var type = string.IsNullOrWhiteSpace(payload.Type) ? headerType : payload.Type;
        return new ReonicWebhookEvent(Clean(type), Clean(payload.Data?.ProjectId), Clean(payload.Data?.SignatureRequestId));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record Payload(string? Type, PayloadData? Data);

    private sealed record PayloadData(string? ProjectId, string? SignatureRequestId);
}
