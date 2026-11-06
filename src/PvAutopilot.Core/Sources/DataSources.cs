using PvAutopilot.Core.DataCard;

namespace PvAutopilot.Core.Sources;

/// <summary>CRM-Datenquelle (Instanz reinsolar: HubSpot). Liefert Kunden-, Betreiber- und Errichterdaten.</summary>
public interface ICrmProjectSource
{
    Task<CrmProjectData> GetProjectAsync(string crmProjectId, CancellationToken ct);
}

/// <summary>Planungs-Datenquelle (Instanz reinsolar: Reonic). Liefert Komponenten, Zähler und Fotos.</summary>
public interface IPlanningDataSource
{
    Task<PlanningData> GetPlanningDataAsync(string planningProjectId, CancellationToken ct);
}

/// <summary>Produktunterlagen je Komponente (Instanz reinsolar: SharePoint): Datenblatt, Einheiten- und NA-Schutz-Zertifikat.</summary>
public interface IProductDocumentSource
{
    Task<IReadOnlyList<DocumentRef>> GetProductDocumentsAsync(string hersteller, string modell, CancellationToken ct);
}

/// <summary>Was das CRM liefert. Felder sind nullable; fehlende Pflichtdaten meldet der Aggregator.</summary>
public sealed record CrmProjectData
{
    public string? PlanningProjectId { get; init; }
    public Person? Anlagenbetreiber { get; init; }

    /// <summary>
    /// Anschrift des Anlagenbetreibers. Instanz reinsolar: immer die Kundenadresse, auch bei abweichendem Betreiber
    /// (Entscheidung 06.11.). Kein Pflichtfeld: <c>null</c> = der Aggregator nimmt <see cref="Anlagenadresse"/>.
    /// </summary>
    public PostalAddress? Betreiberadresse { get; init; }

    public PostalAddress? Anlagenadresse { get; init; }

    /// <summary>Abweichender Errichter am Vorgang; <c>null</c> = Errichter der Instanz (<see cref="ErrichterOptions"/>).</summary>
    public Anlagenerrichter? Errichter { get; init; }
    public Einspeiseart? Einspeiseart { get; init; }
    public int? NhSicherungAmpere { get; init; }

    /// <summary>Nur zur Gegenprüfung; führend ist <see cref="PlanningData.Installationsart"/>.</summary>
    public bool? BestandsanlageVorhanden { get; init; }
    public DateOnly? Installationstermin { get; init; }
    public IReadOnlyList<DocumentRef> Dokumente { get; init; } = [];

    /// <summary>
    /// Fachliche Lücken, die nur die Quelle erkennen kann (z. B. Vorgang nicht gefunden, Adresse nicht zerlegbar,
    /// unbekannter Auswahlwert). Ohne personenbezogene Daten – sie landen in HubSpot-Tasks und Logs.
    /// </summary>
    public IReadOnlyList<string> Problems { get; init; } = [];
}

/// <summary>Was die Planungssoftware liefert.</summary>
public sealed record PlanningData
{
    public Installationsart? Installationsart { get; init; }
    public string? Hauptzaehlernummer { get; init; }
    public bool Kaskadenschaltung { get; init; }
    public IReadOnlyList<Zaehler> Zaehler { get; init; } = [];
    public IReadOnlyList<ModulPosition> Module { get; init; } = [];
    public IReadOnlyList<WechselrichterPosition> Wechselrichter { get; init; } = [];
    public Batteriespeicher? Speicher { get; init; }
    public Wallbox? Wallbox { get; init; }
    public string? Messkonzept { get; init; }
    public string? MesskonzeptBemerkung { get; init; }
    public IReadOnlyList<DocumentRef> Dokumente { get; init; } = [];

    /// <summary>
    /// Fachliche Lücken, die nur die Quelle erkennen kann (z. B. bedingte Pflichtfelder der Checkliste).
    /// Ohne personenbezogene Daten – sie landen in HubSpot-Tasks und Logs.
    /// </summary>
    public IReadOnlyList<string> Problems { get; init; } = [];
}
