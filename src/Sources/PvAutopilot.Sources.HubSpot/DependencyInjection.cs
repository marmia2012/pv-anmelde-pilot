using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Http;
using PvAutopilot.Core.Mastr;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Sources;
using PvAutopilot.Sources.HubSpot.Api;
using PvAutopilot.Sources.HubSpot.Documents;

namespace PvAutopilot.Sources.HubSpot;

public static class DependencyInjection
{
    public static IServiceCollection AddHubSpot(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HubSpotOptions>().Bind(configuration.GetSection(HubSpotOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<HubSpotOptions>, HubSpotOptionsValidator>();
        services.AddHostedService<HubSpotTriggerRulesStartupCheck>();
        services.AddSingleton<HubSpotWebhookValidator>();
        services.AddSingleton<HubSpotTriggerEvaluator>();

        services.TryAddTransient<ExponentialBackoffHandler>(_ => new ExponentialBackoffHandler());

        // Ein typisierter Client für Lesen und Schreiben
        services.AddHttpClient<HubSpotApiClient>(Configure).AddHttpMessageHandler<ExponentialBackoffHandler>();

        // Lesen
        services.AddScoped<VollmachtFinder>();
        services.AddScoped<ICrmProjectSource, HubSpotCrmProjectSource>();
        services.AddScoped<HubSpotStructureReader>();
        services.AddScoped<IMastrCredentialStore, HubSpotMastrCredentialStore>();

        // Schreiben, nur bei HubSpot:WriteEnabled (sonst Debug-Log). Idempotenz über ICrmWriteLedger (Infrastructure).
        services.AddScoped<HubSpotWriteOnce>();
        services.AddScoped<IHumanTaskSink, HubSpotTaskSink>();
        services.AddScoped<IAuditSink, HubSpotAuditSink>();
        services.AddScoped<IDocumentArchive, HubSpotDocumentArchive>();
        services.AddScoped<ICrmStatusSink, HubSpotStatusSink>();
        return services;
    }

    private static void Configure(IServiceProvider sp, HttpClient http)
    {
        var options = sp.GetRequiredService<IOptions<HubSpotOptions>>().Value;
        http.BaseAddress = options.BaseUrl;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        // Das Timeout umfasst die Wartezeiten des Backoff-Handlers.
        http.Timeout = ExponentialBackoffHandler.MaxTotalDelay + TimeSpan.FromMinutes(1);
    }
}
