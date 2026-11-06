using System.Text.Json;
using System.Text.Json.Serialization;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core.Processes.Steps;

public sealed class AggregateDataCardStep(NabDataCardAggregator aggregator, IDocumentArchive archive, TimeProvider time) : IProcessStep
{
    private static readonly JsonSerializerOptions DataCardJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    public bool CanHandle(ProcessState state) => state == ProcessState.Created;

    public async Task<StepResult> ExecuteAsync(AnmeldeProcess process, CancellationToken ct)
    {
        var result = await aggregator.AggregateAsync(process.CrmProjectId, ct);
        if (!result.IsComplete)
        {
            return StepResult.HumanAction(
                // Revision im Schlüssel: bei einer Neueinreichung nach Rückfrage entsteht ein neuer Task.
                $"datacard-missing:{process.Id:N}:r{process.Revision}",
                "Anmeldung: Daten unvollständig",
                "Bitte ergänzen und den Vorgang erneut anstoßen: " + string.Join(", ", result.MissingFields));
        }

        process.AttachDataCard(result.Card!, time.GetUtcNow());
        // Konzept: Datenkarte in der Prozess-DB und im CRM. Das Archiv ist idempotent (gleicher Inhalt = keine zweite Datei),
        // ein Wiederholungslauf legt also nichts doppelt ab.
        await archive.StoreAsync(process.CrmProjectId, [DataCardDocument(process.Revision, result.Card!)], ct);
        process.TransitionTo(ProcessState.DataCardAggregated, time.GetUtcNow());
        return StepResult.Continue;
    }

    /// <summary>Die Datenkarte als JSON-Dokument, je Revision ein eigener Dateiname.</summary>
    public static GeneratedDocument DataCardDocument(int revision, NabDataCard card) =>
        new($"nab-datenkarte-r{revision}.json", "application/json", JsonSerializer.SerializeToUtf8Bytes(card, DataCardJson));
}
