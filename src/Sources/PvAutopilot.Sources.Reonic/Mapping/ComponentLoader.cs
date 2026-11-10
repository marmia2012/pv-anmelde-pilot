using PvAutopilot.Sources.Reonic.Api;

namespace PvAutopilot.Sources.Reonic.Mapping;

/// <summary>
/// Lädt zu Positionen der relevanten Typen (<see cref="ComponentMapper.RelevantTypes"/>) die angebotene
/// Komponentenversion aus dem Katalog. Genutzt von Netzanmeldung (Hauptvariante) und Auftragsübernahme (unterschriebene Variante).
/// </summary>
public static class ComponentLoader
{
    /// <param name="problems">Fehlende Katalog-Komponenten werden hier gemeldet (fachlich, nur Produktnamen).</param>
    public static async Task<List<PlannedComponent>> LoadAsync(
        ReonicApiClient api, IEnumerable<ReonicLineItem> lineItems, List<string> problems, CancellationToken ct)
    {
        // Nur relevante Typen aus dem Katalog laden: spart Calls und verhindert, dass eine gelöschte
        // Zubehör-Komponente den Vorgang blockiert.
        var relevant = lineItems.Where(i => ComponentMapper.RelevantTypes.Contains(i.ComponentType)).ToList();

        // Jede Komponentenversion nur einmal laden (Rate-Limit).
        var versions = new Dictionary<(string, string), ReonicComponent?>();
        var result = new List<PlannedComponent>();
        foreach (var item in relevant)
        {
            var versionKey = (item.ComponentId, item.ComponentVersionId);
            if (!versions.TryGetValue(versionKey, out var component))
            {
                component = await api.GetComponentVersionAsync(item.ComponentId, item.ComponentVersionId, ct);
                versions[versionKey] = component;
            }

            if (component is null)
                problems.Add($"Reonic-Katalog: Komponente „{item.Brand} {item.Name}“ nicht gefunden");
            else
                result.Add(new PlannedComponent(item, component));
        }
        return result;
    }
}
