using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Sources;
using PvAutopilot.Sources.HubSpot.Api;
using PvAutopilot.Sources.HubSpot.Documents;
using PvAutopilot.Sources.HubSpot.Mapping;

namespace PvAutopilot.Sources.HubSpot;

/// <summary>
/// Liest die CRM-Daten der NAB-Datenkarte vom Deal (Objekttyp <see cref="HubSpotOptions.ObjectTypeId"/>):
/// <list type="number">
/// <item><c>GET crm/v3/objects/{type}/{id}</c> mit allen konfigurierten Properties (<see cref="HubSpotPropertyKeys.DataCardKeys"/>)</item>
/// <item>Mapping über <see cref="DealMapper"/> (Fallbacks Betreiber/Anlagenort, späterer Termin, Einspeiseart)</item>
/// <item>EVU-Vollmacht unter den Anhängen am Deal (<see cref="VollmachtFinder"/>)</item>
/// </list>
/// Fehlende oder ungültige Daten werden als <see cref="CrmProjectData.Problems"/> gemeldet (fachlich, kein Retry);
/// 401/403/5xx werfen <see cref="HttpRequestException"/> (technisch, Retry durch den Job).
/// </summary>
public sealed class HubSpotCrmProjectSource(
    HubSpotApiClient api,
    VollmachtFinder vollmacht,
    IOptions<HubSpotOptions> options,
    ILogger<HubSpotCrmProjectSource> logger) : ICrmProjectSource
{
    public const string DealNotFound = "HubSpot: Deal nicht gefunden (Deal-ID bzw. HubSpot:ObjectTypeId prüfen)";
    /// <summary>Fachliches Problem, wenn keine Vollmacht gefunden wird; das Muster stammt aus der Konfiguration.</summary>
    public static string VollmachtMissing(string pattern) => $"EVU-Vollmacht fehlt (kein PDF-Anhang mit '{pattern}' im Namen am Deal)";

    public async Task<CrmProjectData> GetProjectAsync(string crmProjectId, CancellationToken ct)
    {
        var o = options.Value;
        var properties = HubSpotPropertyKeys.DataCardKeys
            .Select(o.PropertyName)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        logger.LogInformation("HubSpot-Deal {DealId} wird gelesen ({PropertyCount} Properties konfiguriert)", crmProjectId, properties.Count);
        var unconfigured = HubSpotPropertyKeys.DataCardKeys.Where(k => o.PropertyName(k) is null).ToList();
        if (unconfigured.Count > 0)
            logger.LogDebug("HubSpot: nicht konfigurierte Felder werden nicht gelesen: {UnconfiguredKeys}", unconfigured);

        var deal = await api.GetObjectAsync(o.ObjectTypeId, crmProjectId, properties, ct);
        if (deal is null)
        {
            logger.LogWarning("HubSpot-Deal {DealId} nicht gefunden (Objekttyp {ObjectTypeId})", crmProjectId, o.ObjectTypeId);
            return new CrmProjectData { Problems = [DealNotFound] };
        }

        var (data, missingKeys) = DealMapper.Map(deal, o);
        var problems = new List<string>(data.Problems);

        var vollmachtRef = await vollmacht.FindAsync(crmProjectId, ct);
        if (vollmachtRef is null)
            problems.Add(VollmachtMissing(o.VollmachtFileNamePattern.Trim()));

        if (missingKeys.Count > 0)
            // Nur fachliche Schlüssel, nie Werte.
            logger.LogWarning("HubSpot-Deal {DealId}: Felder fehlen oder sind ungültig: {MissingKeys}", crmProjectId, missingKeys.Distinct().ToList());

        logger.LogInformation("HubSpot-Deal {DealId} gelesen: {FieldCount} Felder gefüllt, {ProblemCount} Lücken",
            crmProjectId, DealMapper.CountFilled(deal, o), problems.Count);

        return data with
        {
            Dokumente = vollmachtRef is null ? [] : [vollmachtRef],
            Problems = problems,
        };
    }
}
