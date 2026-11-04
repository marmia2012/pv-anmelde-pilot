using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PvAutopilot.Netzbetreiber.NetzeDuisburg.Channels;
using PvAutopilot.Core.Netzbetreiber;

namespace PvAutopilot.Netzbetreiber.NetzeDuisburg;

public static class DependencyInjection
{
    public static IServiceCollection AddNetzeDuisburg(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NetzeDuisburgOptions>().Bind(configuration.GetSection(NetzeDuisburgOptions.SectionName));
        services.AddScoped<NetzeDuisburgMailChannel>();
        services.AddScoped<NetzeDuisburgPortalChannel>();
        services.AddScoped<INetzbetreiberAdapter, NetzeDuisburgAdapter>();
        return services;
    }
}
