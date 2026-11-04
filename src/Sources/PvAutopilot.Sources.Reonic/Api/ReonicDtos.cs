namespace PvAutopilot.Sources.Reonic.Api;

// Nur die Felder, die der Adapter braucht. Quelle: OpenAPI-Spezifikation v3 (https://api.reonic.com/rest/v3/openapi).

/// <summary><c>GET /residentialProjects/{projectId}</c> (<c>getResidentialProjectById</c>).</summary>
public sealed record ReonicProject(string Id, string? MeterNumber, string? PrimaryOfferVariantId);

/// <summary><c>GET /residentialProjects/{projectId}/variants/{variantId}</c> (<c>getResidentialProjectVariantById</c>).</summary>
public sealed record ReonicVariant(
    string Id,
    ReonicVariantSystems Systems,
    IReadOnlyList<ReonicOptionalBundle>? OptionalBundles);

public sealed record ReonicVariantSystems(
    ReonicVariantSystem? Solar,
    ReonicVariantSystem? BatteryStorage,
    ReonicVariantSystem? EvCharger,
    ReonicVariantSystem? Heating,
    ReonicVariantSystem? Additional)
{
    public IEnumerable<ReonicLineItem> AllLineItems =>
        new[] { Solar, BatteryStorage, EvCharger, Heating, Additional }
            .Where(s => s is not null)
            .SelectMany(s => s!.LineItems ?? []);
}

public sealed record ReonicVariantSystem(IReadOnlyList<ReonicLineItem>? LineItems);

public sealed record ReonicOptionalBundle(string Id, bool Selected, IReadOnlyList<ReonicLineItem>? LineItems);

public sealed record ReonicLineItem(
    string ComponentId,
    string ComponentVersionId,
    string ComponentType,
    string Name,
    string Brand,
    decimal Quantity);

/// <summary><c>GET /components/{componentId}/versions/{versionId}</c> (<c>getComponentVersionById</c>).</summary>
public sealed record ReonicComponent(
    string Id,
    string VersionId,
    string Name,
    string Brand,
    string ComponentType,
    string? QuantityUnit,
    string? DatasheetUrl,
    ReonicComponentAttributes? Attributes);

/// <summary>Vereinigung der Attribute aller relevanten Komponententypen. Einheiten: Wp, W, Wh.</summary>
public sealed record ReonicComponentAttributes(
    decimal? WattPeak,
    decimal? NominalOutputPower,
    int? NumberOfPhases,
    decimal? Capacity,
    decimal? MaxChargingPower);

/// <summary><c>GET /checklists/{projectId}?stage=…</c> (<c>getChecklistByProjectId</c>).</summary>
public sealed record ReonicChecklist(string Stage, IReadOnlyList<ReonicChecklistBlock> Blocks);

public sealed record ReonicChecklistBlock(string Id, string Name, IReadOnlyList<ReonicChecklistSegment> Segments);

public sealed record ReonicChecklistSegment(
    string Id,
    string Name,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ReonicChecklistItem> Items);

/// <summary>
/// Ein Checklisten-Element. Die API liefert je nach <see cref="Type"/> unterschiedliche Felder
/// (<c>radio</c>, <c>freetext</c>, <c>image</c>, …); hier flach mit allen optionalen Feldern.
/// </summary>
public sealed record ReonicChecklistItem(
    string Type,
    string? Title,
    string? Description,
    string? Text,
    bool MarkedIrrelevant,
    IReadOnlyList<ReonicChecklistSubItem>? Items);

/// <summary>Auswahlwert (<c>radio</c>/<c>checkbox</c>) oder Foto-Anforderung (<c>image</c>).</summary>
public sealed record ReonicChecklistSubItem(
    string? Text,
    bool Checked,
    string? Title,
    string? Description,
    bool MarkedIrrelevant,
    IReadOnlyList<ReonicAttachment>? Attachments);

public sealed record ReonicAttachment(string? Type, string? Url);
