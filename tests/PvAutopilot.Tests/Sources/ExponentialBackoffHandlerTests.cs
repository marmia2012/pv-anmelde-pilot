using System.Net;
using PvAutopilot.Sources.Reonic;

namespace PvAutopilot.Tests.Sources;

public class ExponentialBackoffHandlerTests
{
    [Fact]
    public async Task Retries_rate_limit_with_2_4_8_16_seconds_then_gives_up()
    {
        var waits = new List<TimeSpan>();
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var client = new HttpClient(new ExponentialBackoffHandler((d, _) => { waits.Add(d); return Task.CompletedTask; })
        {
            InnerHandler = inner,
        });

        var response = await client.GetAsync("https://example.org/");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal([2, 4, 8, 16], waits.Select(w => (int)w.TotalSeconds));
        Assert.Equal(5, inner.Calls);
    }

    [Fact]
    public async Task Does_not_retry_client_errors()
    {
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(new ExponentialBackoffHandler((_, _) => Task.CompletedTask) { InnerHandler = inner });

        await client.GetAsync("https://example.org/");

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Longer_retry_after_wins()
    {
        var waits = new List<TimeSpan>();
        var calls = 0;
        var inner = new StubHandler(_ =>
        {
            if (calls++ > 0)
                return new HttpResponseMessage(HttpStatusCode.OK);
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return r;
        });
        using var client = new HttpClient(new ExponentialBackoffHandler((d, _) => { waits.Add(d); return Task.CompletedTask; })
        {
            InnerHandler = inner,
        });

        await client.GetAsync("https://example.org/");

        Assert.Equal([TimeSpan.FromSeconds(30)], waits);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }
}
