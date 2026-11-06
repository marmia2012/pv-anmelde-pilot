using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core.Processes.Steps;

public sealed class AggregateDataCardStep(NabDataCardAggregator aggregator, IDocumentArchive archive, TimeProvider time) : IProcessStep
{
    /// <summary>
    /// JSON der abgelegten Datenkarte. <see cref="DocumentRef.Reference"/> (signierte URLs, Datei-IDs) wird weggelassen:
    /// eine Datei im CRM soll keine wiederverwendbaren Zugriffslinks enthalten; Art, Quelle und Dateiname reichen.
    /// Die Prozess-DB speichert die Datenkarte vollständig (eigene Serialisierung im DbContext).
    /// </summary>
    private static readonly JsonSerializerOptions DataCardJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitDocumentReference } },
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
        // Konzept: Datenkarte in der Prozess-DB und im CRM. Stabiler Ablage-Schlüssel je Vorgang und Revision: ein
        // Wiederholungslauf legt keine zweite Datei ab, auch wenn sich der Inhalt inzwischen geändert hat.
        await archive.StoreAsync(process.CrmProjectId, [DataCardDocument(process.Id, process.Revision, result.Card!)], ct);
        process.TransitionTo(ProcessState.DataCardAggregated, time.GetUtcNow());
        return StepResult.Continue;
    }

    /// <summary>Die Datenkarte als JSON-Dokument, je Revision ein eigener Dateiname und Ablage-Schlüssel.</summary>
    public static GeneratedDocument DataCardDocument(Guid processId, int revision, NabDataCard card) =>
        new($"nab-datenkarte-r{revision}.json", "application/json", JsonSerializer.SerializeToUtf8Bytes(card, DataCardJson),
            ArchiveKey: $"datacard:{processId:N}:r{revision}");

    private static void OmitDocumentReference(JsonTypeInfo info)
    {
        if (info.Type != typeof(DocumentRef))
            return;
        foreach (var property in info.Properties)
        {
            if (property.AttributeProvider is System.Reflection.PropertyInfo { Name: nameof(DocumentRef.Reference) })
                property.ShouldSerialize = static (_, _) => false;
        }
    }
}
