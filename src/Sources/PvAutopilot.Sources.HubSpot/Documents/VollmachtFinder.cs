using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Sources.HubSpot.Api;

namespace PvAutopilot.Sources.HubSpot.Documents;

/// <summary>
/// Sucht die EVU-Vollmacht unter den Anhängen der Notizen und E-Mails am Deal: PDF, Dateiname enthält
/// <see cref="HubSpotOptions.VollmachtFileNamePattern"/> (Groß-/Kleinschreibung egal), der neueste Anhang gewinnt.
/// Heruntergeladen wird erst beim Versand.
/// </summary>
public sealed class VollmachtFinder(HubSpotApiClient api, IOptions<HubSpotOptions> options, ILogger<VollmachtFinder> logger)
{
    public const string Source = "hubspot";

    private static readonly string[] EngagementTypes = ["notes", "emails"];
    private static readonly string[] EngagementProperties = ["hs_attachment_ids", "hs_timestamp"];

    /// <returns><c>null</c>, wenn kein passender Anhang am Deal hängt.</returns>
    public async Task<DocumentRef?> FindAsync(string dealId, CancellationToken ct)
    {
        var o = options.Value;
        var pattern = o.VollmachtFileNamePattern.Trim();

        var engagements = new List<(DateTimeOffset At, IReadOnlyList<string> FileIds)>();
        foreach (var type in EngagementTypes)
        {
            var ids = await api.GetAssociatedIdsAsync(o.ObjectTypeId, dealId, type, ct);
            if (ids.Count == 0)
                continue;
            foreach (var engagement in await api.BatchReadAsync(type, ids, EngagementProperties, ct))
            {
                var fileIds = AttachmentIds(engagement.Value("hs_attachment_ids"));
                if (fileIds.Count > 0)
                    engagements.Add((Timestamp(engagement.Value("hs_timestamp")) ?? DateTimeOffset.MinValue, fileIds));
            }
        }

        // Neueste Notiz/E-Mail zuerst; der erste Treffer ist damit der neueste. Spart Datei-Abrufe.
        var checkedFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, fileIds) in engagements.OrderByDescending(e => e.At))
        {
            HubSpotFile? best = null;
            foreach (var fileId in fileIds.Where(checkedFiles.Add))
            {
                var file = await api.GetFileAsync(fileId, ct);
                if (file is not null && Matches(file, pattern)
                    && (best is null || (file.CreatedAt ?? DateTimeOffset.MinValue) > (best.CreatedAt ?? DateTimeOffset.MinValue)))
                    best = file;
            }

            if (best is not null)
            {
                // Dateinamen nicht loggen: sie enthalten oft den Kundennamen.
                logger.LogInformation("EVU-Vollmacht für Deal {DealId}: {AttachmentCount} Anhänge geprüft, gefunden: {Found}",
                    dealId, checkedFiles.Count, true);
                return new DocumentRef(DocumentKind.EvuVollmacht, Source, best.Id, FileName(best));
            }
        }

        logger.LogWarning("EVU-Vollmacht für Deal {DealId}: {AttachmentCount} Anhänge geprüft, gefunden: {Found}",
            dealId, checkedFiles.Count, false);
        return null;
    }

    private static bool Matches(HubSpotFile file, string pattern) =>
        IsPdf(file) && file.Name is { } name && name.Contains(pattern, StringComparison.OrdinalIgnoreCase);

    private static bool IsPdf(HubSpotFile file) =>
        string.Equals(file.Extension?.Trim().TrimStart('.'), "pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>HubSpot führt den Namen ohne Endung; die Endung wird nur angehängt, wenn sie fehlt.</summary>
    private static string FileName(HubSpotFile file)
    {
        var name = file.Name!.Trim();
        var extension = "." + file.Extension!.Trim().TrimStart('.');
        return name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? name : name + extension;
    }

    /// <summary><c>hs_attachment_ids</c> ist eine durch Semikolon getrennte Liste von Datei-IDs.</summary>
    private static IReadOnlyList<string> AttachmentIds(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary><c>hs_timestamp</c> kommt als ISO 8601 oder als Epoch-Millisekunden.</summary>
    private static DateTimeOffset? Timestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ms))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;
    }
}
