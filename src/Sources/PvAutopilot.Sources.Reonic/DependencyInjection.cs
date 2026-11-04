using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Sources;
using PvAutopilot.Sources.Reonic.Api;

namespace PvAutopilot.Sources.Reonic;

public static class DependencyInjection
{
    public static IServiceCollection AddReonic(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ReonicOptions>()
            .Bind(configuration.GetSection(ReonicOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Reonic:ApiKey fehlt (Umgebungsvariable Reonic__ApiKey).")
            .Validate(o => o.BaseUrl.AbsoluteUri.EndsWith('/'), "Reonic:BaseUrl muss mit / enden, sonst fällt /v3 aus relativen Pfaden.")
            .ValidateOnStart();

        services.AddTransient<ExponentialBackoffHandler>(_ => new ExponentialBackoffHandler());
        services.AddTransient<CacheBypassHandler>();
        services.AddHttpClient<ReonicApiClient>((sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<ReonicOptions>>().Value;
                http.BaseAddress = options.BaseUrl;
                http.DefaultRequestHeaders.Add("X-Authorization", options.ApiKey);
                // Das Timeout umfasst die Wartezeiten des Backoff-Handlers.
                http.Timeout = ExponentialBackoffHandler.MaxTotalDelay + TimeSpan.FromMinutes(1);
            })
            .AddHttpMessageHandler<CacheBypassHandler>()
            .AddHttpMessageHandler<ExponentialBackoffHandler>();
        services.AddScoped<IPlanningDataSource, ReonicPlanningDataSource>();
        return services;
    }
}

/// <summary>
/// Setzt <c>Reonic-Cache-Control: no-cache</c> für veränderliche Daten (Projekt, Variante, Checkliste).
/// Komponentenversionen sind unveränderlich und dürfen aus dem Reonic-Cache kommen; das schont das kleinere
/// „uncached“-Rate-Limit.
/// </summary>
public sealed class CacheBypassHandler(IOptions<ReonicOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (options.Value.BypassCache && !path.Contains("/components/", StringComparison.Ordinal))
            request.Headers.TryAddWithoutValidation("Reonic-Cache-Control", "no-cache");
        return base.SendAsync(request, ct);
    }
}
