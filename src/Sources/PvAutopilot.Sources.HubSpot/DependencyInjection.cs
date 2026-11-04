using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Mastr;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Sources;

namespace PvAutopilot.Sources.HubSpot;

public static class DependencyInjection
{
    public static IServiceCollection AddHubSpot(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HubSpotOptions>().Bind(configuration.GetSection(HubSpotOptions.SectionName));
        services.AddSingleton<HubSpotWebhookValidator>();
        services.AddSingleton<HubSpotTriggerEvaluator>();

        services.AddHttpClient<ICrmProjectSource, HubSpotCrmProjectSource>(Configure);
        services.AddHttpClient<IHumanTaskSink, HubSpotTaskSink>(Configure);
        services.AddHttpClient<IAuditSink, HubSpotAuditSink>(Configure);
        services.AddHttpClient<IDocumentArchive, HubSpotDocumentArchive>(Configure);
        services.AddHttpClient<IMastrCredentialStore, HubSpotMastrCredentialStore>(Configure);
        return services;
    }

    private static void Configure(IServiceProvider sp, HttpClient http)
    {
        var options = sp.GetRequiredService<IOptions<HubSpotOptions>>().Value;
        http.BaseAddress = options.BaseUrl;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
    }
}
