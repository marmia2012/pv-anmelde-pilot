using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PvAutopilot.Sources.Reonic;
using PvAutopilot.Sources.Reonic.Api;

namespace PvAutopilot.Tests.Sources.Reonic;

public class ReonicFileDownloaderTests
{
    private const string Url = "https://files.example/signed/geheim-token-4711.pdf?sig=abc";

    private readonly StubHandler _handler = new();

    private ReonicFileDownloader Downloader() => new(new HttpClient(_handler));

    [Fact]
    public async Task Https_url_is_downloaded_with_a_plain_get_and_no_auth_header()
    {
        _handler.Respond = _ => Bytes([1, 2, 3]);

        var content = await Downloader().DownloadAsync(Url, default);

        Assert.Equal([1, 2, 3], content);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(Url, request.RequestUri!.ToString());
        Assert.False(request.Headers.Contains("X-Authorization"));
        Assert.False(request.Headers.Contains("Reonic-Cache-Control"));
        Assert.Null(request.Headers.Authorization);
    }

    [Theory]
    [InlineData("http://files.example/a.pdf")]
    [InlineData("ftp://files.example/a.pdf")]
    [InlineData("/relative/a.pdf")]
    [InlineData("a.pdf")]
    [InlineData("")]
    [InlineData("file:///c:/a.pdf")]
    public async Task Everything_but_absolute_https_gives_null_without_a_request(string url)
    {
        Assert.Null(await Downloader().DownloadAsync(url, default));

        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task Missing_or_expired_signed_url_gives_null(HttpStatusCode status)
    {
        _handler.Respond = _ => new HttpResponseMessage(status);

        Assert.Null(await Downloader().DownloadAsync(Url, default));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Other_errors_throw_without_the_url_in_the_message(HttpStatusCode status)
    {
        _handler.Respond = _ => new HttpResponseMessage(status);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Downloader().DownloadAsync(Url, default));

        Assert.Equal(status, ex.StatusCode);
        Assert.Contains(((int)status).ToString(), ex.Message);
        Assert.DoesNotContain("geheim-token", ex.Message);
        Assert.DoesNotContain("files.example", ex.Message);
        Assert.DoesNotContain("sig=", ex.Message);
    }

    [Fact]
    public async Task Declared_content_length_over_the_limit_gives_null_without_reading_the_body()
    {
        var body = new TrackingStream(new MemoryStream([1, 2, 3]));
        _handler.Respond = _ =>
        {
            var content = new StreamContent(body);
            content.Headers.ContentLength = ReonicFileDownloader.MaxBytes + 1;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };

        Assert.Null(await Downloader().DownloadAsync(Url, default));
        Assert.False(body.WasRead);
    }

    [Fact]
    public async Task Streamed_body_over_the_limit_without_length_gives_null()
    {
        _handler.Respond = _ => Unsized(new byte[ReonicFileDownloader.MaxBytes + 1]);

        Assert.Null(await Downloader().DownloadAsync(Url, default));
    }

    [Fact]
    public async Task Streamed_body_exactly_at_the_limit_is_returned()
    {
        var data = new byte[ReonicFileDownloader.MaxBytes];
        data[0] = 7;
        data[^1] = 9;
        _handler.Respond = _ => Unsized(data);

        var content = await Downloader().DownloadAsync(Url, default);

        Assert.Equal(ReonicFileDownloader.MaxBytes, content!.LongLength);
        Assert.Equal((7, 9), (content[0], content[^1]));
    }

    [Fact]
    public void Limit_is_20_megabytes() => Assert.Equal(20L * 1024 * 1024, ReonicFileDownloader.MaxBytes);

    [Fact]
    public async Task Registered_download_client_carries_no_api_key_while_the_reonic_client_does()
    {
        var apiHandler = new StubHandler { Respond = _ => Bytes([]) };
        var fileHandler = new StubHandler { Respond = _ => Bytes([5]) };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Reonic:ApiKey"] = "rnc_v3_test", ["Reonic:BaseUrl"] = "https://reonic.example/rest/v3/",
        }).Build();
        var services = new ServiceCollection();
        services.AddReonic(configuration);
        services.AddHttpClient<ReonicApiClient>().ConfigurePrimaryHttpMessageHandler(() => apiHandler);
        services.AddHttpClient<ReonicFileDownloader>(ReonicFileDownloader.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => fileHandler);
        await using var provider = services.BuildServiceProvider();

        // Kontrolle: der Reonic-Client schickt den Schlüssel.
        apiHandler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        await provider.GetRequiredService<ReonicApiClient>().GetProjectAsync("p1", default);
        Assert.Equal("rnc_v3_test", Assert.Single(Assert.Single(apiHandler.Requests).Headers.GetValues("X-Authorization")));

        var content = await provider.GetRequiredService<ReonicFileDownloader>().DownloadAsync(Url, default);

        Assert.Equal([5], content);
        var request = Assert.Single(fileHandler.Requests);
        Assert.Equal(Url, request.RequestUri!.ToString());
        Assert.False(request.Headers.Contains("X-Authorization"));
        Assert.False(request.Headers.Contains("Reonic-Cache-Control"));
        Assert.Single(apiHandler.Requests);
    }

    private static HttpResponseMessage Bytes(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };

    /// <summary>Antwort ohne Content-Length (nicht suchbarer Stream).</summary>
    private static HttpResponseMessage Unsized(byte[] data)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ForwardOnlyStream(data)) };
        Assert.Null(response.Content.Headers.ContentLength);
        return response;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(Respond(request));
        }
    }

    private sealed class ForwardOnlyStream(byte[] data) : Stream
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

    private sealed class TrackingStream(Stream inner) : Stream
    {
        public bool WasRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            WasRead = true;
            return inner.Read(buffer, offset, count);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
