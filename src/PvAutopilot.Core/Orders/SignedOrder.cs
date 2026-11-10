using PvAutopilot.Core.DataCard;

namespace PvAutopilot.Core.Orders;

/// <summary>
/// Unterschriebener Auftrag aus der Planungssoftware (Instanz reinsolar: Reonic): die Positionen der unterschriebenen
/// Variante, die Zusammenfassung der Komponenten und das unterschriebene PDF (docs/konzept/reonic-hubspot-auftrag.md).
/// </summary>
public sealed record SignedOrder
{
    /// <summary>Projekt-ID in der Planungssoftware (Reonic-Projekt).</summary>
    public required string PlanningProjectId { get; init; }

    /// <summary>ID der Signaturanfrage; <c>null</c>, wenn keine passende gefunden wurde.</summary>
    public string? SignatureRequestId { get; init; }

    /// <summary>
    /// Ob die Signaturanfrage gefunden wurde und unterschrieben ist (<c>status = signed</c>). Sonst steht der Grund in
    /// <see cref="Problems"/> und es wird nichts übertragen.
    /// </summary>
    public bool IsSigned { get; init; }

    /// <summary>CRM-Objekt laut Quelle (Reonic: <c>integrations.hubspotDealId</c>); <c>null</c>, wenn nicht verknüpft.</summary>
    public string? CrmProjectId { get; init; }

    public DateTimeOffset? SignedAt { get; init; }

    /// <summary>ID der unterschriebenen Variante, nur wenn sie geladen werden konnte; sonst <c>null</c> (Grund in <see cref="Problems"/>).</summary>
    public string? VariantId { get; init; }

    /// <summary>Alle Positionen der Variante in ihrer Reihenfolge (alle Systeme, dann die gewählten Zusatzpakete).</summary>
    public IReadOnlyList<OrderPosition> Positions { get; init; } = [];

    public OrderSummary Summary { get; init; } = OrderSummary.Empty;

    /// <summary>Unterschriebenes Auftrags-PDF (<see cref="DocumentKind.Auftrag"/>); <c>null</c>, wenn die Quelle keins liefert.</summary>
    public DocumentRef? SignedPdf { get; init; }

    /// <summary>Fachliche Lücken ohne Werte (Produktnamen erlaubt, keine Preise); landen in einer HubSpot-Aufgabe und im Log.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];
}

/// <summary>
/// Eine Position des Auftrags. <see cref="UnitPriceGross"/> ist der Bruttopreis je Stück; Einkaufspreise kennt das Modell
/// bewusst nicht. <see cref="Description"/> ist der Langtext der Position.
/// </summary>
public sealed record OrderPosition(
    string ComponentType,
    string Brand,
    string Name,
    string? ArticleNumber,
    string? Description,
    decimal Quantity,
    decimal? UnitPriceGross,
    decimal? VatRate)
{
    /// <summary>„{Hersteller} {Name}“, ohne doppelte Leerzeichen bei fehlendem Hersteller.</summary>
    public string DisplayName => $"{Brand} {Name}".Trim();
}

/// <summary>Zusammengefasste Komponenten des Auftrags (gleiche Regeln wie für die NAB-Datenkarte).</summary>
public sealed record OrderSummary(
    IReadOnlyList<ModulPosition> Module,
    IReadOnlyList<WechselrichterPosition> Wechselrichter,
    Batteriespeicher? Speicher,
    Wallbox? Wallbox)
{
    public static readonly OrderSummary Empty = new([], [], null, null);

    /// <summary>Summe Wp × Anzahl in kWp.</summary>
    public decimal LeistungKwp => Module.Sum(m => m.LeistungWp * m.Anzahl) / 1000m;

    /// <summary>Summe AC-Nennleistung × Anzahl in kW.</summary>
    public decimal WechselrichterLeistungKw => Wechselrichter.Sum(w => w.LeistungW * w.Anzahl) / 1000m;
}
