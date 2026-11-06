using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PvAutopilot.Sources.HubSpot.Api;

namespace PvAutopilot.Sources.HubSpot;

/// <summary>
/// Liest die Struktur des konfigurierten Objekttyps (Pipelines mit Stages, Properties mit Optionen), um Stage-IDs
/// und interne Property-Namen für <c>instance.json</c> zu ermitteln. Nur Metadaten, keine Datensätze.
/// </summary>
public sealed class HubSpotStructureReader(HubSpotApiClient api, IOptions<HubSpotOptions> options, ILogger<HubSpotStructureReader> logger)
{
    public async Task<HubSpotStructure> ReadAsync(CancellationToken ct)
    {
        var type = options.Value.ObjectTypeId;
        var pipelines = await api.GetPipelinesAsync(type, ct);
        var properties = await api.GetPropertiesAsync(type, ct);

        logger.LogInformation("HubSpot-Struktur für Objekttyp {ObjectTypeId} gelesen: {PipelineCount} Pipelines, {PropertyCount} Properties",
            type, pipelines.Count, properties.Count);

        return new HubSpotStructure(
            type,
            pipelines
                .OrderBy(p => p.DisplayOrder ?? int.MaxValue)
                .Select(p => new HubSpotStructure.Pipeline(
                    p.Id,
                    p.Label,
                    (p.Stages ?? [])
                        .OrderBy(s => s.DisplayOrder ?? int.MaxValue)
                        .Select(s => new HubSpotStructure.Stage(s.Id, s.Label))
                        .ToList()))
                .ToList(),
            properties
                .OrderBy(p => p.GroupName, StringComparer.Ordinal)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => new HubSpotStructure.Property(
                    p.Name,
                    p.Label,
                    p.Type,
                    p.FieldType,
                    p.GroupName,
                    (p.Options ?? []).Select(x => new HubSpotStructure.Option(x.Value, x.Label)).ToList()))
                .ToList());
    }
}

/// <summary>Antwort von <c>GET /admin/hubspot/structure</c>.</summary>
public sealed record HubSpotStructure(
    string ObjectTypeId,
    IReadOnlyList<HubSpotStructure.Pipeline> Pipelines,
    IReadOnlyList<HubSpotStructure.Property> Properties)
{
    public sealed record Pipeline(string Id, string? Label, IReadOnlyList<Stage> Stages);

    public sealed record Stage(string Id, string? Label);

    public sealed record Property(
        string Name,
        string? Label,
        string? Type,
        string? FieldType,
        string? GroupName,
        IReadOnlyList<Option> Options);

    public sealed record Option(string? Value, string? Label);
}
