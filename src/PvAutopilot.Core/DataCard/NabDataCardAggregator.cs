using Microsoft.Extensions.Options;
using PvAutopilot.Core.Sources;

namespace PvAutopilot.Core.DataCard;

public sealed record DataCardAggregation(NabDataCard? Card, IReadOnlyList<string> MissingFields)
{
    public bool IsComplete => Card is not null && MissingFields.Count == 0;
}

/// <summary>Baut die NAB-Datenkarte aus den konfigurierten Datenquellen zusammen.</summary>
public sealed class NabDataCardAggregator(
    ICrmProjectSource crm,
    IPlanningDataSource planning,
    IProductDocumentSource productDocuments,
    IOptions<ErrichterOptions> errichterOptions)
{
    public async Task<DataCardAggregation> AggregateAsync(string crmProjectId, CancellationToken ct)
    {
        var project = await crm.GetProjectAsync(crmProjectId, ct);
        var missing = new List<string>(project.Problems);

        if (project.PlanningProjectId is null)
            return new DataCardAggregation(null, [.. missing, "Reonic-Projekt-ID"]);

        var plan = await planning.GetPlanningDataAsync(project.PlanningProjectId, ct);
        missing.AddRange(plan.Problems);

        if (project.BestandsanlageVorhanden is { } bestand && plan.Installationsart is { } art
            && bestand != (art == Installationsart.Erweiterung))
            missing.Add("Widerspruch: Neuerrichtung/Erweiterung (Reonic) passt nicht zu „Bestandsanlage vorhanden“ (HubSpot)");

        if (project.Anlagenbetreiber is null) missing.Add("Anlagenbetreiber");
        if (project.Anlagenadresse is null) missing.Add("Anlagenadresse");
        // Errichter: Override am Vorgang vor dem Errichter der Instanz.
        var errichter = project.Errichter ?? errichterOptions.Value.ToErrichter();
        if (errichter is null) missing.Add("Anlagenerrichter");
        if (plan.Module.Count == 0) missing.Add("Module");
        if (plan.Wechselrichter.Count == 0) missing.Add("Wechselrichter");
        if (plan.Hauptzaehlernummer is null) missing.Add("Hauptzählernummer");

        if (project.Anlagenbetreiber is null || project.Anlagenadresse is null || errichter is null)
            return new DataCardAggregation(null, missing);

        var products = plan.Module.Select(m => (m.Hersteller, m.Modell))
            .Concat(plan.Wechselrichter.Select(w => (w.Hersteller, w.Modell)))
            .Concat(plan.Speicher is { } s ? [(s.Hersteller, s.Modell)] : [])
            .Concat(plan.Wallbox is { } w ? [(w.Hersteller, w.Modell)] : [])
            .Distinct();

        var documents = new List<DocumentRef>(project.Dokumente);
        documents.AddRange(plan.Dokumente);
        foreach (var (hersteller, modell) in products)
            documents.AddRange(await productDocuments.GetProductDocumentsAsync(hersteller, modell, ct));

        var card = new NabDataCard
        {
            Anlagenbetreiber = project.Anlagenbetreiber,
            Anlagenadresse = project.Anlagenadresse,
            Errichter = errichter,
            Einspeiseart = project.Einspeiseart ?? Einspeiseart.Teileinspeisung,
            NhSicherungAmpere = project.NhSicherungAmpere ?? 63,
            Installationstermin = project.Installationstermin,
            Installationsart = plan.Installationsart ?? Installationsart.Neuerrichtung,
            Hauptzaehlernummer = plan.Hauptzaehlernummer,
            Kaskadenschaltung = plan.Kaskadenschaltung,
            Zaehler = plan.Zaehler,
            Module = plan.Module,
            Wechselrichter = plan.Wechselrichter,
            Speicher = plan.Speicher,
            Wallbox = plan.Wallbox,
            Messkonzept = plan.Messkonzept,
            MesskonzeptBemerkung = plan.MesskonzeptBemerkung,
            Dokumente = documents,
        };

        return new DataCardAggregation(card, missing);
    }
}
