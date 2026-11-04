using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Sources;

namespace PvAutopilot.Sources.SharePoint;

public sealed class SharePointOptions
{
    public const string SectionName = "SharePoint";

    public string SiteId { get; set; } = string.Empty;
    public string DriveId { get; set; } = string.Empty;

    /// <summary>Wurzelordner der Produktunterlagen, darunter <c>{Hersteller}/{Modell}/</c>.</summary>
    public string ProductRootPath { get; set; } = string.Empty;
}

/// <summary>Datenblätter und Zertifikate je Produkt aus SharePoint (Microsoft Graph).</summary>
public sealed class SharePointProductDocumentSource(HttpClient http) : IProductDocumentSource
{
    public Task<IReadOnlyList<DocumentRef>> GetProductDocumentsAsync(string hersteller, string modell, CancellationToken ct)
    {
        _ = http;
        // TODO: Ordnerstruktur je Produkt mit reinsolar abstimmen; ggf. zuerst Datenblatt-URL aus dem Reonic-Katalog.
        throw new NotImplementedException("SharePoint-Ordnerstruktur ist noch nicht festgelegt.");
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddSharePoint(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SharePointOptions>().Bind(configuration.GetSection(SharePointOptions.SectionName));
        services.AddHttpClient<IProductDocumentSource, SharePointProductDocumentSource>(http =>
            http.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/"));
        return services;
    }
}
