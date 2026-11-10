using PvAutopilot.Core.Logging;

namespace PvAutopilot.Sources.Reonic.Api;

// Nur die Felder, die der Adapter braucht. Quelle: OpenAPI-Spezifikation v3 (https://api.reonic.com/rest/v3/openapi).
// Einkaufspreise (unitPurchasePrice, totalPurchasePrice, purchase*) werden bewusst nicht gelesen.

/// <summary><c>GET /residentialProjects/{projectId}</c> (<c>getResidentialProjectById</c>).</summary>
public sealed record ReonicProject(
    string Id,
    string? MeterNumber,
    string? PrimaryOfferVariantId,
    ReonicProjectIntegrations? Integrations = null);

/// <summary>Verknüpfungen des Projekts mit anderen Systemen; <c>hubspotDealId</c> ist der HubSpot-Deal.</summary>
public sealed record ReonicProjectIntegrations(string? HubspotDealId);

/// <summary><c>GET /residentialProjects/{projectId}/signatureRequests</c>.</summary>
public sealed record ReonicSignatureRequestsResponse(IReadOnlyList<ReonicSignatureRequest>? Data);

/// <summary>
/// Signaturanfrage eines Angebots. <see cref="Status"/>: <c>awaitingSignature</c>, <c>signed</c>, <c>expired</c>,
/// <c>withdrawn</c>, <c>revokedByCustomer</c>. <see cref="SignedPdfUrl"/> ist eine signierte URL auf das Kunden-PDF.
/// </summary>
public sealed record ReonicSignatureRequest(
    string Id,
    string? Status,
    string? SignedVariantId,
    [property: PersonalData] string? SignedPdfUrl,
    DateTimeOffset? SignedAt)
{
    public const string StatusSigned = "signed";

    public bool IsSigned => string.Equals(Status, StatusSigned, StringComparison.Ordinal);
}

/// <summary><c>GET /residentialProjects/{projectId}/variants/{variantId}</c> (<c>getResidentialProjectVariantById</c>).</summary>
public sealed record ReonicVariant(
    string Id,
    ReonicVariantSystems Systems,
    IReadOnlyList<ReonicOptionalBundle>? OptionalBundles)
{
    /// <summary>Alle Positionen in Reihenfolge der Variante: alle Systeme, dann die gewählten Zusatzpakete.</summary>
    public IEnumerable<ReonicLineItem> SelectedLineItems =>
        Systems.AllLineItems.Concat((OptionalBundles ?? []).Where(b => b.Selected).SelectMany(b => b.LineItems ?? []));
}

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

/// <summary>
/// Eine Position der Variante. <see cref="UnitSalesPrice"/> ist der Verkaufspreis je Stück, <see cref="Description"/>
/// der Langtext. Einkaufspreise stehen bewusst nicht im DTO.
/// </summary>
public sealed record ReonicLineItem(
    string ComponentId,
    string ComponentVersionId,
    string ComponentType,
    string Name,
    string Brand,
    decimal Quantity,
    string? ArticleNumber = null,
    string? Description = null,
    bool VisibleToCustomer = true,
    ReonicPrice? UnitSalesPrice = null);

/// <summary>Verkaufspreis je Stück: netto, brutto, Steuerbetrag, Steuersatz.</summary>
public sealed record ReonicPrice(decimal? Net, decimal? Gross, decimal? Vat, decimal? VatRate);

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
