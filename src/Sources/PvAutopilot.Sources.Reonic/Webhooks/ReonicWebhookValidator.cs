using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace PvAutopilot.Sources.Reonic.Webhooks;

/// <summary>
/// Prüft die Reonic-Webhook-Signatur: <c>X-Reonic-Signature</c> = <c>sha256=&lt;hex&gt;</c>, HMAC-SHA256 mit
/// <see cref="ReonicOptions.WebhookSecret"/> über <c>${X-Reonic-Timestamp}.${rawBody}</c>. Der Zeitstempel (Unix-Sekunden)
/// darf höchstens <see cref="MaxSkew"/> von der aktuellen Zeit abweichen. Ohne Secret ist keine Signatur gültig.
/// </summary>
public sealed class ReonicWebhookValidator(IOptions<ReonicOptions> options, TimeProvider time)
{
    public const string SignatureHeader = "X-Reonic-Signature";
    public const string TimestampHeader = "X-Reonic-Timestamp";
    public const string EventHeader = "X-Reonic-Event";
    public const string EventIdHeader = "X-Reonic-Event-Id";

    public static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(5);

    private const string Prefix = "sha256=";

    public bool IsEnabled => !string.IsNullOrEmpty(options.Value.WebhookSecret);

    public bool IsValid(byte[] rawBody, string? signature, string? timestamp)
    {
        var secret = options.Value.WebhookSecret;
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp))
            return false;
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            return false;

        DateTimeOffset sentAt;
        try
        {
            sentAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        if ((time.GetUtcNow() - sentAt).Duration() > MaxSkew)
            return false;

        if (!signature.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        byte[] provided;
        try
        {
            provided = Convert.FromHexString(signature.AsSpan(Prefix.Length).Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        // Über die Rohbytes signieren: ein Umweg über string würde ungültiges UTF-8 verändern.
        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var source = new byte[prefix.Length + rawBody.Length];
        prefix.CopyTo(source, 0);
        rawBody.CopyTo(source, prefix.Length);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), source);
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }
}
