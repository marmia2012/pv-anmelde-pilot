using System.Text.Json.Serialization;
using PvAutopilot.Core.Logging;

namespace PvAutopilot.Sources.HubSpot.Api;

// Nur die Felder, die der Adapter braucht. Quelle: HubSpot CRM API v3/v4, Files API v3.

/// <summary><c>GET crm/v3/objects/{objectType}/{id}</c> bzw. ein Eintrag aus <c>…/batch/read</c>.</summary>
public sealed record HubSpotObject(
    string Id,
    [property: PersonalData] Dictionary<string, string?>? Properties)
{
    /// <summary>Getrimmter Wert der Property; <c>null</c>, wenn nicht vorhanden oder leer.</summary>
    public string? Value(string? propertyName) =>
        propertyName is not null && Properties is not null
        && Properties.TryGetValue(propertyName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
}

/// <summary>Antwort von <c>POST crm/v3/objects/{objectType}/batch/read</c>.</summary>
public sealed record HubSpotBatchResponse(IReadOnlyList<HubSpotObject>? Results);

/// <summary>Antwort von <c>GET crm/v4/objects/{fromType}/{id}/associations/{toType}</c>.</summary>
public sealed record HubSpotAssociationsResponse(IReadOnlyList<HubSpotAssociation>? Results, HubSpotPaging? Paging);

public sealed record HubSpotAssociation(
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long ToObjectId);

public sealed record HubSpotPaging(HubSpotPagingNext? Next);

public sealed record HubSpotPagingNext(string? After);

/// <summary><c>GET files/v3/files/{fileId}</c>. Der Name kann Kundennamen enthalten.</summary>
public sealed record HubSpotFile(
    string Id,
    [property: PersonalData] string? Name,
    string? Extension,
    DateTimeOffset? CreatedAt);

/// <summary>Antwort von <c>GET crm/v3/pipelines/{objectType}</c>.</summary>
public sealed record HubSpotPipelinesResponse(IReadOnlyList<HubSpotPipeline>? Results);

public sealed record HubSpotPipeline(string Id, string? Label, int? DisplayOrder, IReadOnlyList<HubSpotPipelineStage>? Stages);

public sealed record HubSpotPipelineStage(string Id, string? Label, int? DisplayOrder);

/// <summary>Antwort von <c>GET crm/v3/properties/{objectType}</c>.</summary>
public sealed record HubSpotPropertiesResponse(IReadOnlyList<HubSpotProperty>? Results);

public sealed record HubSpotProperty(
    string Name,
    string? Label,
    string? Type,
    string? FieldType,
    string? GroupName,
    IReadOnlyList<HubSpotPropertyOption>? Options);

public sealed record HubSpotPropertyOption(string? Value, string? Label);
