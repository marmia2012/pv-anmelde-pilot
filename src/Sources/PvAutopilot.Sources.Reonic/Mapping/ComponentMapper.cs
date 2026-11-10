using PvAutopilot.Core.DataCard;
using PvAutopilot.Sources.Reonic.Api;

namespace PvAutopilot.Sources.Reonic.Mapping;

/// <summary>Eine Position der Hauptvariante samt Katalogstand der angebotenen Komponentenversion.</summary>
public sealed record PlannedComponent(ReonicLineItem LineItem, ReonicComponent Component);

public sealed record ComponentData(
    IReadOnlyList<ModulPosition> Module,
    IReadOnlyList<WechselrichterPosition> Wechselrichter,
    Batteriespeicher? Speicher,
    Wallbox? Wallbox,
    IReadOnlyList<DocumentRef> Datenblaetter,
    IReadOnlyList<string> Problems)
{
    /// <summary>Dieselben Probleme wie <see cref="Problems"/> (gleiche Reihenfolge und Texte), mit Kategorie zum Filtern.</summary>
    public IReadOnlyList<ComponentProblem> ProblemDetails { get; init; } = [];
}

/// <summary>Kategorie eines Komponentenproblems; erlaubt Verbrauchern zu filtern, ohne auf Texte zu prüfen.</summary>
public enum ComponentProblemKind
{
    /// <summary>Technischer Katalogwert fehlt (Wp, AC-Leistung, Kapazität, Ladeleistung).</summary>
    CatalogValue,

    /// <summary>Mengeneinheit ist nicht „Stück“.</summary>
    QuantityUnit,

    /// <summary>Kein Datenblatt im Katalog (nur für die Netzanmeldung relevant).</summary>
    MissingDatasheet,

    /// <summary>Konstellation, die die Netzanmeldung noch nicht automatisch abbildet (z. B. mehrere Speicher-/Wallbox-Typen).</summary>
    NotYetSupportedForNetzanmeldung,
}

public sealed record ComponentProblem(ComponentProblemKind Kind, string Text);

/// <summary>
/// Übersetzt die Positionen der Hauptvariante in Module, Wechselrichter, Speicher und Wallbox (Vorschlag 4.1).
/// Technische Werte aus dem Katalog: <c>wattPeak</c> (Wp), <c>nominalOutputPower</c> (W), <c>capacity</c> (Wh),
/// <c>maxChargingPower</c> (W).
/// </summary>
public static class ComponentMapper
{
    public const string DocumentSource = "reonic-catalog";

    /// <summary>
    /// Komponententypen, die für die Netzanmeldung zählen. Alles andere (Optimierer, Unterkonstruktion, Kabel,
    /// Montage …) wird weder aus dem Katalog geladen noch geprüft.
    /// </summary>
    public static readonly IReadOnlySet<string> RelevantTypes = new HashSet<string>
    {
        "module", "inverter", "microinverter", "allInOneEnergyStorageSystem", "batteryStorage", "evCharger",
    };

    public static ComponentData Map(IReadOnlyList<PlannedComponent> components)
    {
        var problems = new List<ComponentProblem>();
        var module = new List<ModulPosition>();
        var wechselrichter = new List<WechselrichterPosition>();
        var speicher = new List<Batteriespeicher>();
        var wallboxen = new List<Wallbox>();
        var datenblaetter = new List<DocumentRef>();

        foreach (var (item, component) in components)
        {
            if (!RelevantTypes.Contains(item.ComponentType))
                continue;

            var label = $"{item.ComponentType} „{item.Brand} {item.Name}“";
            if (component.QuantityUnit is { } unit && unit != "item")
                problems.Add(new(ComponentProblemKind.QuantityUnit, $"Reonic-Planung: {label} hat Mengeneinheit „{unit}“ statt Stück"));

            var attributes = component.Attributes;
            switch (item.ComponentType)
            {
                case "module":
                    if (attributes?.WattPeak is { } wp and > 0)
                        module.Add(new ModulPosition(item.Brand, item.Name, wp, item.Quantity));
                    else
                        problems.Add(new(ComponentProblemKind.CatalogValue, $"Reonic-Katalog: {label} ohne Leistung (Wp)"));
                    break;

                case "inverter" or "microinverter":
                    AddInverter(item, attributes, label, wechselrichter, problems);
                    break;

                case "allInOneEnergyStorageSystem":
                    AddInverter(item, attributes, label, wechselrichter, problems);
                    AddStorage(item, attributes, label, speicher, problems);
                    break;

                case "batteryStorage":
                    AddStorage(item, attributes, label, speicher, problems);
                    break;

                case "evCharger":
                    if (attributes?.MaxChargingPower is { } w and > 0)
                        wallboxen.Add(new Wallbox(item.Brand, item.Name, w / 1000m, item.Quantity));
                    else
                        problems.Add(new(ComponentProblemKind.CatalogValue, $"Reonic-Katalog: {label} ohne Ladeleistung"));
                    break;
            }

            if (string.IsNullOrWhiteSpace(component.DatasheetUrl))
                problems.Add(new(ComponentProblemKind.MissingDatasheet, $"Reonic-Katalog: {label} ohne Datenblatt"));
            else if (!datenblaetter.Any(d => d.Reference == component.DatasheetUrl))
                datenblaetter.Add(new DocumentRef(DocumentKind.Datenblatt, DocumentSource, component.DatasheetUrl,
                    SafeFileName($"Datenblatt {item.Brand} {item.Name}") + ".pdf"));
        }

        var wallboxTypen = Merge(wallboxen, w => (w.Hersteller, w.Modell, w.LeistungKw), (a, b) => a with { Anzahl = a.Anzahl + b.Anzahl });
        if (speicher.Count > 1)
            problems.Add(new(ComponentProblemKind.NotYetSupportedForNetzanmeldung, "Reonic-Planung: mehrere verschiedene Speicher – wird noch nicht automatisch angemeldet"));
        if (wallboxTypen.Count > 1)
            problems.Add(new(ComponentProblemKind.NotYetSupportedForNetzanmeldung, "Reonic-Planung: mehrere verschiedene Wallboxen – wird noch nicht automatisch angemeldet"));

        return new ComponentData(
            Merge(module, m => (m.Hersteller, m.Modell, m.LeistungWp), (a, b) => a with { Anzahl = a.Anzahl + b.Anzahl }),
            Merge(wechselrichter, w => (w.Hersteller, w.Modell, w.LeistungW), (a, b) => a with { Anzahl = a.Anzahl + b.Anzahl }),
            speicher.FirstOrDefault(),
            wallboxTypen.FirstOrDefault(),
            datenblaetter,
            problems.Select(p => p.Text).ToList())
        {
            ProblemDetails = problems,
        };
    }

    /// <summary>Katalognamen wie „HVS 7.7 / 3 Module“ enthalten Zeichen, die in Dateinamen nicht erlaubt sind.</summary>
    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
    }

    private static void AddInverter(
        ReonicLineItem item, ReonicComponentAttributes? attributes, string label,
        List<WechselrichterPosition> target, List<ComponentProblem> problems)
    {
        if (attributes?.NominalOutputPower is { } w and > 0)
            target.Add(new WechselrichterPosition(item.Brand, item.Name, null, w, item.Quantity, null, attributes.NumberOfPhases));
        else
            problems.Add(new(ComponentProblemKind.CatalogValue, $"Reonic-Katalog: {label} ohne AC-Nennleistung"));
    }

    private static void AddStorage(
        ReonicLineItem item, ReonicComponentAttributes? attributes, string label,
        List<Batteriespeicher> target, List<ComponentProblem> problems)
    {
        if (attributes?.Capacity is not { } wh || wh <= 0)
        {
            problems.Add(new(ComponentProblemKind.CatalogValue, $"Reonic-Katalog: {label} ohne Kapazität"));
            return;
        }
        // Modulare Speicher: Kapazität je Stück × Anzahl. Kopplung AC/DC kommt aus dem Datenblatt (Konzept 4.7).
        var kwh = wh * item.Quantity / 1000m;
        var existing = target.FindIndex(s => s.Hersteller == item.Brand && s.Modell == item.Name);
        if (existing >= 0)
            target[existing] = target[existing] with { KapazitaetKwh = target[existing].KapazitaetKwh + kwh };
        else
            target.Add(new Batteriespeicher(item.Brand, item.Name, kwh, null));
    }

    /// <summary>Gleiche Komponente in mehreren Positionen (z. B. Dachflächen, Zusatzpaket) zusammenfassen.</summary>
    private static List<T> Merge<T, TKey>(List<T> items, Func<T, TKey> key, Func<T, T, T> combine) where TKey : notnull =>
        items.GroupBy(key).Select(g => g.Aggregate(combine)).ToList();
}
