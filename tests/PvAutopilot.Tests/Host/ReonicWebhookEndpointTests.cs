using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Orders;
using PvAutopilot.Host.Endpoints;
using PvAutopilot.Sources.Reonic;
using PvAutopilot.Sources.Reonic.Webhooks;
using PvAutopilot.Tests.Sources.HubSpot;
using PvAutopilot.Tests.Sources.Reonic;

namespace PvAutopilot.Tests.Host;

internal sealed class RecordingOrderScheduler : IOrderTransferScheduler
{
    public List<(string Project, string? Signature)> Enqueued { get; } = [];
    public void Enqueue(string planningProjectId, string? signatureRequestId) => Enqueued.Add((planningProjectId, signatureRequestId));
}

/// <summary>Verarbeitung von <c>POST /webhooks/reonic</c> ohne HTTP-Pipeline (Rohbody und Header rein, Ergebnis raus).</summary>
public class ReonicWebhookEndpointTests
{
    private const string Secret = "test-webhook-secret";
    private const string SignedBody = """{"version":1,"type":"residentialOffer_signed","occurredAt":"2026-11-10T08:00:00Z","data":{"projectId":"proj-1","signatureRequestId":"sig-1","signedVariantId":"v1"}}""";

    private readonly FixedTime _time = new(TestData.Now);
    private readonly RecordingOrderScheduler _scheduler = new();
    private readonly CapturingLogger<ReonicWebhookValidator> _log = new();

    private string Now => TestData.Now.ToUnixTimeSeconds().ToString();

    private int? Post(string body, string? signature = "valid", string? timestamp = null, string secret = Secret, string? eventType = null,
        string eventId = "evt-1")
    {
        timestamp ??= Now;
        var headers = new HeaderDictionary { [ReonicWebhookValidator.EventIdHeader] = eventId };
        if (signature == "valid")
            signature = ReonicWebhookValidatorTests.Sign(body, timestamp);
        if (signature is not null)
            headers[ReonicWebhookValidator.SignatureHeader] = signature;
        headers[ReonicWebhookValidator.TimestampHeader] = timestamp;
        if (eventType is not null)
            headers[ReonicWebhookValidator.EventHeader] = eventType;

        var validator = new ReonicWebhookValidator(Options.Create(new ReonicOptions { WebhookSecret = secret }), _time);
        var result = ReonicWebhookEndpoints.Handle(Encoding.UTF8.GetBytes(body), headers, validator, _scheduler, _log);
        return ((IStatusCodeHttpResult)result).StatusCode;
    }

    [Fact]
    public void Valid_signed_event_returns_200_and_enqueues_exactly_once_with_project_and_signature()
    {
        Assert.Equal(200, Post(SignedBody));

        Assert.Equal([("proj-1", (string?)"sig-1")], _scheduler.Enqueued);
    }

    [Fact]
    public void Wrong_signature_returns_401_without_enqueue()
    {
        Assert.Equal(401, Post(SignedBody, signature: "sha256=" + new string('0', 64)));

        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public void Missing_signature_returns_401_without_enqueue()
    {
        Assert.Equal(401, Post(SignedBody, signature: null));

        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public void Signature_made_with_another_secret_returns_401_without_enqueue()
    {
        var signature = ReonicWebhookValidatorTests.Sign(SignedBody, Now, "anderes-secret");

        Assert.Equal(401, Post(SignedBody, signature));
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public void Empty_secret_returns_401_even_with_a_matching_empty_secret_signature()
    {
        var signature = ReonicWebhookValidatorTests.Sign(SignedBody, Now, "");

        Assert.Equal(401, Post(SignedBody, signature, secret: ""));
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public void Old_timestamp_returns_401_without_enqueue()
    {
        var old = TestData.Now.AddMinutes(-6).ToUnixTimeSeconds().ToString();

        Assert.Equal(401, Post(SignedBody, timestamp: old));
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public void Manipulated_body_returns_401_without_enqueue()
    {
        var signature = ReonicWebhookValidatorTests.Sign(SignedBody, Now);

        Assert.Equal(401, Post(SignedBody.Replace("proj-1", "proj-2"), signature));
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public void Withdrawn_signature_returns_200_without_enqueue()
    {
        Assert.Equal(200, Post("""{"type":"residentialOffer_signatureWithdrawn","data":{"projectId":"proj-1","signatureRequestId":"sig-1"}}"""));

        Assert.Empty(_scheduler.Enqueued);
    }

    [Theory]
    [InlineData("""{"type":"test","data":{}}""")]
    [InlineData("""{"type":"residentialProject_created","data":{"projectId":"proj-1"}}""")]
    [InlineData("""{"type":"residentialOffer_viewed","data":{"projectId":"proj-1","signatureRequestId":"sig-1"}}""")]
    [InlineData("""{"data":{"projectId":"proj-1","signatureRequestId":"sig-1"}}""")]
    public void Other_event_types_return_200_without_enqueue(string body)
    {
        Assert.Equal(200, Post(body));

        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public void Event_type_falls_back_to_the_header_when_the_body_has_none()
    {
        Assert.Equal(200, Post("""{"data":{"projectId":"proj-1","signatureRequestId":"sig-1"}}""", eventType: "residentialOffer_signed"));

        Assert.Equal([("proj-1", (string?)"sig-1")], _scheduler.Enqueued);
    }

    [Theory]
    [InlineData("kein json")]
    [InlineData("{ \"type\": ")]
    [InlineData("")]
    public void Invalid_json_with_a_valid_signature_returns_400_without_enqueue(string body)
    {
        Assert.Equal(400, Post(body));

        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public void Invalid_json_with_an_invalid_signature_is_unauthorized_not_bad_request() =>
        Assert.Equal(401, Post("kein json", signature: "sha256=00"));

    [Theory]
    [InlineData("""{"type":"residentialOffer_signed","data":{"signatureRequestId":"sig-1"}}""")]
    [InlineData("""{"type":"residentialOffer_signed","data":{"projectId":"proj-1"}}""")]
    [InlineData("""{"type":"residentialOffer_signed","data":{"projectId":" ","signatureRequestId":"sig-1"}}""")]
    [InlineData("""{"type":"residentialOffer_signed","data":{}}""")]
    [InlineData("""{"type":"residentialOffer_signed"}""")]
    public void Signed_event_without_ids_returns_400_without_enqueue(string body)
    {
        Assert.Equal(400, Post(body));

        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public void Log_names_event_and_ids_but_never_body_or_secret()
    {
        Post("""{"type":"residentialOffer_signed","customerName":"Geheimname Erika","data":{"projectId":"proj-1","signatureRequestId":"sig-1"}}""");
        Post("""{"type":"test","customerName":"Geheimname Erika"}""");
        Post("kein Geheimname json");
        Post(SignedBody, signature: "sha256=" + new string('0', 64));

        Assert.NotEmpty(_log.Messages);
        Assert.DoesNotContain("Geheimname", _log.All);
        Assert.DoesNotContain(Secret, _log.All);
        Assert.Contains("evt-1", _log.All);
        Assert.Contains("proj-1", _log.All);
        Assert.Contains("sig-1", _log.All);
    }
}

/// <summary>Der Endpunkt über einen echten lokalen Kestrel (127.0.0.1, freier Port): Rohbody, Header, Statuscodes, Startwarnung.</summary>
public class ReonicWebhookHttpTests
{
    private const string Secret = "test-webhook-secret";
    private const string SignedBody = """{"version":1,"type":"residentialOffer_signed","data":{"projectId":"proj-1","signatureRequestId":"sig-1"}}""";

    private static readonly FixedTime Time = new(TestData.Now);

    private sealed record Running(WebApplication App, HttpClient Client, RecordingOrderScheduler Scheduler, List<string> Log) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }

    private static async Task<Running> StartAsync(string secret)
    {
        var scheduler = new RecordingOrderScheduler();
        var log = new List<string>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new ListLoggerProvider(log));
        builder.Services.AddSingleton(new ReonicWebhookValidator(Options.Create(new ReonicOptions { WebhookSecret = secret }), Time));
        builder.Services.AddSingleton<IOrderTransferScheduler>(scheduler);

        var app = builder.Build();
        app.MapReonicWebhooks();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new Running(app, new HttpClient { BaseAddress = new Uri(address) }, scheduler, log);
    }

    private static async Task<HttpStatusCode> PostAsync(Running running, string body, string? signature = null, string secret = Secret)
    {
        var timestamp = TestData.Now.ToUnixTimeSeconds().ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/reonic")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(ReonicWebhookValidator.TimestampHeader, timestamp);
        request.Headers.Add(ReonicWebhookValidator.SignatureHeader, signature ?? ReonicWebhookValidatorTests.Sign(body, timestamp, secret));
        request.Headers.Add(ReonicWebhookValidator.EventIdHeader, "evt-9");
        using var response = await running.Client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Signed_event_over_http_is_accepted_and_enqueued_once()
    {
        await using var running = await StartAsync(Secret);

        Assert.Equal(HttpStatusCode.OK, await PostAsync(running, SignedBody));

        Assert.Equal([("proj-1", (string?)"sig-1")], running.Scheduler.Enqueued);
    }

    [Fact]
    public async Task Body_with_non_ascii_characters_is_verified_over_the_raw_bytes()
    {
        await using var running = await StartAsync(Secret);
        var body = SignedBody.Replace("\"version\":1", "\"version\":1,\"note\":\"Müller – Straße €\"");

        Assert.Equal(HttpStatusCode.OK, await PostAsync(running, body));
        Assert.Single(running.Scheduler.Enqueued);
    }

    [Fact]
    public async Task Wrong_signature_over_http_is_401_without_enqueue()
    {
        await using var running = await StartAsync(Secret);

        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(running, SignedBody, secret: "falsch"));
        Assert.Empty(running.Scheduler.Enqueued);
    }

    [Fact]
    public async Task Without_secret_everything_is_401_and_start_logs_one_warning()
    {
        await using var running = await StartAsync("");

        // Die Startwarnung steht schon vor der ersten Anfrage im Log, genau einmal.
        Assert.Single(running.Log, l => l.StartsWith("Warning|" + ReonicWebhookEndpoints.LoggerName, StringComparison.Ordinal)
            && l.Contains("Reonic__WebhookSecret", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(running, SignedBody, secret: ""));

        Assert.Empty(running.Scheduler.Enqueued);
        Assert.Single(running.Log, l => l.Contains("Reonic__WebhookSecret", StringComparison.Ordinal));
        Assert.DoesNotContain(running.Log, l => l.Contains(Secret));
    }

    [Fact]
    public async Task With_secret_the_start_logs_no_disabled_warning()
    {
        await using var running = await StartAsync(Secret);

        Assert.DoesNotContain(running.Log, l => l.Contains("Reonic__WebhookSecret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_json_over_http_is_400()
    {
        await using var running = await StartAsync(Secret);

        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(running, "kein json"));
        Assert.Empty(running.Scheduler.Enqueued);
    }

    [Fact]
    public async Task Chunked_body_over_256_kb_over_http_is_413_without_enqueue()
    {
        await using var running = await StartAsync(Secret);
        var timestamp = TestData.Now.ToUnixTimeSeconds().ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/reonic")
        {
            Content = new StreamContent(new UnsizedStream(new byte[ReonicWebhookEndpoints.MaxBodyBytes + 1])),
        };
        request.Headers.TransferEncodingChunked = true;
        request.Headers.Add(ReonicWebhookValidator.TimestampHeader, timestamp);
        request.Headers.Add(ReonicWebhookValidator.SignatureHeader, "sha256=" + new string('0', 64));

        using var response = await running.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(running.Scheduler.Enqueued);
    }

    private sealed class UnsizedStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ListLoggerProvider(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(categoryName, sink);
        public void Dispose() { }

        private sealed class ListLogger(string category, List<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (sink)
                    sink.Add($"{logLevel}|{category}|{formatter(state, exception)}");
            }
        }
    }
}

/// <summary><c>ReceiveAsync</c>: Vorabprüfung der Header ohne Body-Lesen und Größenlimit (256 KB).</summary>
public class ReonicWebhookReceiveTests
{
    private const string Secret = "test-webhook-secret";
    private const int Limit = 256 * 1024;

    private readonly FixedTime _time = new(TestData.Now);
    private readonly RecordingOrderScheduler _scheduler = new();

    private string Now => TestData.Now.ToUnixTimeSeconds().ToString();

    private static string BodyOfSize(int bytes)
    {
const string prefix = "{\"type\":\"residentialOffer_signed\",\"pad\":\"";        const string tail = "\",\"data\":{\"projectId\":\"proj-1\",\"signatureRequestId\":\"sig-1\"}}";
        var body = prefix + new string('x', bytes - prefix.Length - tail.Length) + tail;
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(body));
        return body;
    }

    private async Task<(int? Status, CountingStream Stream)> ReceiveAsync(Stream body, string secret = Secret, bool signature = true,
        bool timestamp = true, long? contentLength = null, string? signedText = null)
    {
        var context = new DefaultHttpContext();
        var counting = new CountingStream(body);
        context.Request.Body = counting;
        context.Request.ContentLength = contentLength;
        if (timestamp)
            context.Request.Headers[ReonicWebhookValidator.TimestampHeader] = Now;
        if (signature)
            context.Request.Headers[ReonicWebhookValidator.SignatureHeader] = signedText is null
                ? "sha256=" + new string('0', 64)
                : ReonicWebhookValidatorTests.Sign(signedText, Now);

        var validator = new ReonicWebhookValidator(Options.Create(new ReonicOptions { WebhookSecret = secret }), _time);
        var result = await ReonicWebhookEndpoints.ReceiveAsync(context.Request, validator, _scheduler,
            new CapturingLogger<ReonicWebhookValidator>(), default);
        return (((IStatusCodeHttpResult)result).StatusCode, counting);
    }

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Without_secret_it_is_401_and_the_body_is_not_read()
    {
        var (status, stream) = await ReceiveAsync(Bytes("{}"), secret: "");

        Assert.Equal(401, status);
        Assert.Equal(0, stream.Reads);
    }

    [Fact]
    public async Task Without_signature_header_it_is_401_and_the_body_is_not_read()
    {
        var (status, stream) = await ReceiveAsync(Bytes("{}"), signature: false);

        Assert.Equal(401, status);
        Assert.Equal(0, stream.Reads);
    }

    [Fact]
    public async Task Without_timestamp_header_it_is_401_and_the_body_is_not_read()
    {
        var (status, stream) = await ReceiveAsync(Bytes("{}"), timestamp: false);

        Assert.Equal(401, status);
        Assert.Equal(0, stream.Reads);
    }

    [Fact]
    public async Task Without_headers_an_oversized_declared_body_is_still_401_not_413()
    {
        var (status, stream) = await ReceiveAsync(Bytes("{}"), signature: false, contentLength: Limit + 1);

        Assert.Equal(401, status);
        Assert.Equal(0, stream.Reads);
    }

    [Fact]
    public async Task Declared_content_length_over_the_limit_is_413_without_reading()
    {
        var (status, stream) = await ReceiveAsync(Bytes("{}"), contentLength: Limit + 1);

        Assert.Equal(413, status);
        Assert.Equal(0, stream.Reads);
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public async Task Streamed_body_over_the_limit_without_length_is_413()
    {
        var (status, _) = await ReceiveAsync(Bytes(BodyOfSize(Limit + 1)), contentLength: null);

        Assert.Equal(413, status);
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public async Task Streamed_body_over_the_limit_is_rejected_even_with_a_valid_signature()
    {
        var body = BodyOfSize(Limit + 1);

        var (status, _) = await ReceiveAsync(Bytes(body), signedText: body);

        Assert.Equal(413, status);
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public async Task Body_of_exactly_the_limit_with_a_valid_signature_is_accepted_and_enqueued()
    {
        var body = BodyOfSize(Limit);

        var (status, _) = await ReceiveAsync(Bytes(body), contentLength: Limit, signedText: body);

        Assert.Equal(200, status);
        Assert.Equal([("proj-1", (string?)"sig-1")], _scheduler.Enqueued);
    }

    [Fact]
    public async Task Body_of_exactly_the_limit_without_declared_length_is_accepted()
    {
        var body = BodyOfSize(Limit);

        var (status, _) = await ReceiveAsync(Bytes(body), signedText: body);

        Assert.Equal(200, status);
        Assert.Single(_scheduler.Enqueued);
    }

    [Fact]
    public async Task Small_body_with_a_wrong_signature_is_401_after_reading()
    {
        var (status, stream) = await ReceiveAsync(Bytes("{}"));

        Assert.Equal(401, status);
        Assert.True(stream.Reads > 0);
    }

    [Fact]
    public void Limit_is_256_kilobytes() => Assert.Equal(Limit, ReonicWebhookEndpoints.MaxBodyBytes);

    private sealed class CountingStream(Stream inner) : Stream
    {
        public int Reads { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Reads++;
            return inner.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reads++;
            return inner.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
