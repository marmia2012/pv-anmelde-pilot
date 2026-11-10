using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Sources;
using PvAutopilot.Sources.Reonic.Api;
using PvAutopilot.Sources.Reonic.Checklist;
using PvAutopilot.Sources.Reonic.Mapping;

namespace PvAutopilot.Sources.Reonic;

/// <summary>
/// Liest die Planungsdaten eines Projekts aus Reonic (Konzept: docs/konzept/reonic-checkliste.md, Abschnitt 6):
/// <list type="number">
/// <item>Projekt → Hauptzählernummer (<c>meterNumber</c>) und Hauptvariante (<c>primaryOfferVariantId</c>)</item>
/// <item>Hauptvariante → Positionen; je Position die angebotene Komponentenversion aus dem Katalog</item>
/// <item>Checkliste „Netzanmeldung“ → Vor-Ort-Angaben, Fotos, Schaltplan</item>
/// </list>
/// Fehlende Daten werden als <see cref="PlanningData.Problems"/> gemeldet (fachlich, kein Retry).
/// </summary>
public sealed class ReonicPlanningDataSource(
    ReonicApiClient api,
    IOptions<ReonicOptions> options,
    ILogger<ReonicPlanningDataSource> logger) : IPlanningDataSource
{
    public async Task<PlanningData> GetPlanningDataAsync(string planningProjectId, CancellationToken ct)
    {
        var project = await api.GetProjectAsync(planningProjectId, ct);
        if (project is null)
            return new PlanningData { Problems = ["Reonic: Projekt nicht gefunden (Reonic-Projekt-ID am HubSpot-Projekt prüfen)"] };

        var problems = new List<string>();

        var components = await LoadPrimaryVariantAsync(project, problems, ct);
        var componentData = ComponentMapper.Map(components);
        problems.AddRange(componentData.Problems);

        var stage = options.Value.ChecklistStage;
        var checklist = await api.GetChecklistAsync(project.Id, stage, ct);
        ChecklistData? checklistData = null;
        if (checklist is null)
            problems.Add($"Reonic: keine Checkliste in Phase „{stage}“");
        else
        {
            checklistData = ChecklistMapper.Map(ChecklistValues.Parse(checklist, NetzanmeldungChecklist.TitleFallback));
            problems.AddRange(checklistData.Problems);
        }

        logger.LogInformation(
            "Reonic-Projekt {ReonicProjectId} gelesen: {ComponentCount} Positionen, {ProblemCount} Lücken",
            project.Id, components.Count, problems.Count);

        return new PlanningData
        {
            Installationsart = checklistData?.Installationsart,
            Hauptzaehlernummer = string.IsNullOrWhiteSpace(project.MeterNumber) ? null : project.MeterNumber.Trim(),
            Kaskadenschaltung = checklistData?.Kaskadenschaltung ?? false,
            Zaehler = checklistData?.Zaehler ?? [],
            Module = componentData.Module,
            Wechselrichter = componentData.Wechselrichter,
            Speicher = componentData.Speicher,
            Wallbox = componentData.Wallbox,
            Messkonzept = checklistData?.Messkonzept,
            MesskonzeptBemerkung = checklistData?.MesskonzeptBemerkung,
            Dokumente = [.. checklistData?.Dokumente ?? [], .. componentData.Datenblaetter],
            Problems = problems,
        };
    }

    private async Task<List<PlannedComponent>> LoadPrimaryVariantAsync(
        ReonicProject project, List<string> problems, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(project.PrimaryOfferVariantId))
        {
            problems.Add("Reonic: keine Hauptvariante markiert");
            return [];
        }

        var variant = await api.GetVariantAsync(project.Id, project.PrimaryOfferVariantId, ct);
        if (variant is null)
        {
            problems.Add("Reonic: Hauptvariante nicht gefunden");
            return [];
        }

        return await ComponentLoader.LoadAsync(api, variant.SelectedLineItems, problems, ct);
    }
}
