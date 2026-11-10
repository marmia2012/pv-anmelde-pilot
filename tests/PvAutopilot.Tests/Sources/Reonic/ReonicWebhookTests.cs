using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PvAutopilot.Sources.Reonic;
using PvAutopilot.Sources.Reonic.Webhooks;

namespace PvAutopilot.Tests.Sources.Reonic;

public class ReonicWebhookValidatorTests
{
    private const string Secret = "test-webhook-secret";
    private const string Body = """{"version":1,"type":"residentialOffer_signed","data":{"projectId":"p1","signatureRequestId":"s1"}}""";

    private readonly FixedTime _time = new(TestData.Now);

    private static long Unix(DateTimeOffset at) => at.ToUnixTimeSeconds();

    private ReonicWebhookValidator Validator(string secret = Secret) =>
        new(Options.Create(new ReonicOptions { WebhookSecret = secret }), _time);

    internal static string Sign(string body, string timestamp, string secret = Secret) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));

    private string Now => Unix(TestData.Now).ToString();

    private bool IsValid(string body, string? signature, string? timestamp) =>
        Validator().IsValid(Encoding.UTF8.GetBytes(body), signature, timestamp);

    [Fact]
    public void Valid_signature_over_timestamp_dot_body_is_accepted() =>
        Assert.True(IsValid(Body, Sign(Body, Now), Now));

    [Fact]
    public void Uppercase_hex_is_accepted() =>
        Assert.True(IsValid(Body, "sha256=" + Sign(Body, Now)["sha256=".Length..].ToUpperInvariant(), Now));

    [Fact]
    public void Wrong_hex_is_rejected()
    {
        var signature = Sign(Body, Now);
        var flipped = signature[..^1] + (signature[^1] == '0' ? '1' : '0');

        Assert.False(IsValid(Body, flipped, Now));
    }

    [Fact]
    public void Signature_that_is_not_hex_is_rejected() =>
        Assert.False(IsValid(Body, "sha256=zzzz", Now));

    [Fact]
    public void Truncated_signature_is_rejected() =>
        Assert.False(IsValid(Body, Sign(Body, Now)[..^2], Now));

    [Fact]
    public void Wrong_secret_is_rejected() =>
        Assert.False(IsValid(Body, Sign(Body, Now, "anderes-secret"), Now));

    [Fact]
    public void Missing_prefix_is_rejected() =>
        Assert.False(IsValid(Body, Sign(Body, Now)["sha256=".Length..], Now));

    [Fact]
    public void Other_algorithm_prefix_is_rejected() =>
        Assert.False(IsValid(Body, "sha1=" + Sign(Body, Now)["sha256=".Length..], Now));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_signature_header_is_rejected(string? signature) =>
        Assert.False(IsValid(Body, signature, Now));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("99999999999999999999")]
    public void Missing_or_malformed_timestamp_is_rejected(string? timestamp) =>
        Assert.False(IsValid(Body, Sign(Body, timestamp ?? ""), timestamp));

    [Fact]
    public void Timestamp_header_not_matching_the_signed_one_is_rejected() =>
        Assert.False(IsValid(Body, Sign(Body, Now), (Unix(TestData.Now) + 1).ToString()));

    [Fact]
    public void Tampered_body_is_rejected() =>
        Assert.False(IsValid(Body.Replace("p1", "p2"), Sign(Body, Now), Now));

    [Fact]
    public void Appended_whitespace_in_the_body_is_rejected() =>
        Assert.False(IsValid(Body + " ", Sign(Body, Now), Now));

    [Theory]
    [InlineData(-299, true)]
    [InlineData(299, true)]
    [InlineData(-301, false)]
    [InlineData(301, false)]
    [InlineData(-3600, false)]
    [InlineData(3600, false)]
    public void Timestamp_must_be_within_five_minutes_of_now(int offsetSeconds, bool expected)
    {
        var timestamp = (Unix(TestData.Now) + offsetSeconds).ToString();

        Assert.Equal(expected, IsValid(Body, Sign(Body, timestamp), timestamp));
    }

    [Fact]
    public void Window_follows_the_time_provider()
    {
        var timestamp = Now;
        var signature = Sign(Body, timestamp);
        Assert.True(IsValid(Body, signature, timestamp));

        _time.Now = TestData.Now.AddMinutes(6);

        Assert.False(IsValid(Body, signature, timestamp));
    }

    [Fact]
    public void Empty_secret_rejects_even_a_signature_made_with_an_empty_secret()
    {
        var validator = Validator("");

        Assert.False(validator.IsEnabled);
        Assert.False(validator.IsValid(Encoding.UTF8.GetBytes(Body), Sign(Body, Now, ""), Now));
    }

    [Fact]
    public void Enabled_follows_the_secret() => Assert.True(Validator().IsEnabled);

    [Fact]
    public void Signature_covers_the_raw_bytes()
    {
        var raw = new byte[] { 0x7B, 0xFF, 0xFE, 0x7D };
        var prefix = Encoding.UTF8.GetBytes(Now + ".");
        var signature = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), prefix.Concat(raw).ToArray()));

        Assert.True(Validator().IsValid(raw, signature, Now));
    }
}

public class ReonicWebhookParserTests
{
    private static ReonicWebhookEvent Parse(string json, string? header = null) =>
        ReonicWebhookParser.Parse(Encoding.UTF8.GetBytes(json), header);

    [Fact]
    public void Signed_event_gives_type_project_and_signature_ids() =>
        Assert.Equal(new ReonicWebhookEvent("residentialOffer_signed", "p1", "s1"), Parse("""
            { "version": 1, "type": "residentialOffer_signed", "occurredAt": "2026-11-10T08:00:00Z",
              "data": { "projectId": "p1", "signatureRequestId": "s1", "signedVariantId": "v1", "signedPaymentOptionId": "x" } }
            """));

    [Fact]
    public void Ids_are_trimmed_and_blank_ids_are_null() =>
        Assert.Equal(new ReonicWebhookEvent("residentialOffer_signed", "p1", null), Parse("""
            { "type": " residentialOffer_signed ", "data": { "projectId": " p1 ", "signatureRequestId": "  " } }
            """));

    [Fact]
    public void Withdrawn_event_keeps_its_type_so_it_is_not_treated_as_signed() =>
        Assert.Equal(new ReonicWebhookEvent("residentialOffer_signatureWithdrawn", "p1", "s1"), Parse("""
            { "type": "residentialOffer_signatureWithdrawn", "data": { "projectId": "p1", "signatureRequestId": "s1" } }
            """));

    [Fact]
    public void Test_event_has_a_type_but_no_order_ids()
    {
        var evt = Parse("""{ "version": 1, "type": "test", "data": {} }""");

        Assert.Equal("test", evt.Type);
        Assert.Null(evt.ProjectId);
        Assert.Null(evt.SignatureRequestId);
    }

    [Fact]
    public void Missing_data_gives_null_ids() =>
        Assert.Equal(new ReonicWebhookEvent("residentialOffer_signed", null, null), Parse("""{ "type": "residentialOffer_signed" }"""));

    [Fact]
    public void Header_type_is_used_when_the_body_has_none() =>
        Assert.Equal("residentialOffer_signed", Parse("""{ "data": { "projectId": "p1", "signatureRequestId": "s1" } }""", "residentialOffer_signed").Type);

    [Fact]
    public void Body_type_wins_over_the_header() =>
        Assert.Equal("test", Parse("""{ "type": "test" }""", "residentialOffer_signed").Type);

    [Theory]
    [InlineData("")]
    [InlineData("kein json")]
    [InlineData("{ \"type\": ")]
    [InlineData("null")]
    public void Invalid_json_throws_json_exception(string body) =>
        Assert.ThrowsAny<JsonException>(() => Parse(body));
}
