using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PvAutopilot.Core.Mastr;
using PvAutopilot.Core.Processes;

namespace PvAutopilot.Registries.Mastr;

public sealed class MastrOptions
{
    public const string SectionName = "Mastr";

    /// <summary>Default ist das Testsystem; Produktion nur explizit per Konfiguration.</summary>
    public Uri PortalUrl { get; set; } = new("https://test.marktstammdatenregister.de/");

    public bool Headless { get; set; } = true;
}

/// <summary>Registrierung im MaStR-Webportal per Playwright mit dem Kundenlogin.</summary>
public sealed class MastrPortalRegistrar : IMastrRegistrar
{
    public Task<MastrRegistration> RegisterAsync(AnmeldeProcess process, MastrCredentials credentials, CancellationToken ct) =>
        // TODO: Klickstrecke (Anlage + Speicher) mit MastrSelectors; bei Fehlern AutomationException mit Screenshot.
        throw new NotImplementedException("MaStR-Registrierung ist noch nicht umgesetzt.");
}

/// <summary>Alle Selektoren des MaStR-Webportals an einer Stelle.</summary>
internal static class MastrSelectors
{
    // TODO: beim Bau der Klickstrecke füllen.
}

public static class DependencyInjection
{
    public static IServiceCollection AddMastr(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MastrOptions>().Bind(configuration.GetSection(MastrOptions.SectionName));
        services.AddScoped<IMastrRegistrar, MastrPortalRegistrar>();
        return services;
    }
}
