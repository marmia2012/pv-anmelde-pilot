using System.Net;

namespace PvAutopilot.Core.Http;

/// <summary>
/// Wiederholt Requests bei Rate-Limit (429) und Serverfehlern mit 2/4/8/16 s Wartezeit.
/// Ein längeres <c>Retry-After</c> des Servers hat Vorrang, höchstens aber <see cref="MaxDelay"/>.
/// Damit warten alle Versuche zusammen höchstens <see cref="MaxTotalDelay"/>.
/// </summary>
public sealed class ExponentialBackoffHandler(Func<TimeSpan, CancellationToken, Task>? delay = null) : DelegatingHandler
{
    public static readonly TimeSpan[] Delays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16)];

    /// <summary>Obergrenze je Wartezeit, auch wenn der Server ein längeres <c>Retry-After</c> verlangt.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    /// <summary>Maximale Gesamtdauer aller Wartezeiten; das HttpClient-Timeout muss darüber liegen.</summary>
    public static readonly TimeSpan MaxTotalDelay = MaxDelay * Delays.Length;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await base.SendAsync(request, ct);
            if (!IsTransient(response.StatusCode) || attempt >= Delays.Length)
                return response;

            var wait = Delays[attempt];
            if (response.Headers.RetryAfter?.Delta is { } retryAfter && retryAfter > wait)
                wait = retryAfter < MaxDelay ? retryAfter : MaxDelay;

            response.Dispose();
            await _delay(wait, ct);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;
}
