using System.Text.Json;
using System.Text.Json.Serialization;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Host.Endpoints;

/// <summary>
/// Ereignis per Admin-Endpunkt, solange Postfach und HubSpot-Properties fehlen (und für Demos auf Staging).
/// Läuft über denselben <see cref="ProcessTriggerHandler"/> wie der Webhook.
/// </summary>
public sealed record ProcessEventRequest(
    ProcessTriggerKind? Kind,
    DateOnly? Date,
    SubmissionKind? Submission,
    RueckfrageResolution? Resolution,
    string? Reference);

public static class AdminProcessEvents
{
    private const int MaxReferenceLength = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    /// <summary>202 übernommen · 409 zum aktuellen Stand unpassend · 404 Vorgang unbekannt · 400 ungültige Anfrage.</summary>
    public static async Task<IResult> PostAsync(
        Guid id, Stream body, IProcessStore store, ProcessTriggerHandler handler, TimeProvider time, CancellationToken ct)
    {
        ProcessEventRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<ProcessEventRequest>(body, Json, ct);
        }
        catch (JsonException)
        {
            // Keine Details zurückgeben: die Meldung könnte Teile des Bodys enthalten.
            return Results.BadRequest(new { error = "Ungültiges JSON oder unbekannter Enum-Wert." });
        }

        if (Validate(request) is { } error)
            return Results.BadRequest(new { error });

        var process = await store.GetAsync(id, ct);
        if (process is null)
            return Results.NotFound();

        var trigger = new ProcessTrigger(request!.Kind!.Value, process.CrmProjectId)
        {
            Date = request.Date,
            Submission = request.Submission,
            Resolution = request.Resolution,
            Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
            // Ein Mensch meldet das Ereignis jetzt; es ist damit nie älter als der aktuelle Zustand.
            OccurredAt = time.GetUtcNow(),
        };

        return await handler.HandleAsync(trigger, ct) switch
        {
            TriggerResult.Applied => Results.Accepted(),
            TriggerResult.Ignored => Results.Conflict(new { error = $"{trigger.Kind} passt nicht zum Zustand {process.State}." }),
            _ => Results.NotFound(),
        };
    }

    private static string? Validate(ProcessEventRequest? request) => request switch
    {
        null or { Kind: null } => "kind fehlt.",
        { Kind: ProcessTriggerKind.Start } => "Start ist hier nicht erlaubt; Vorgänge entstehen über den HubSpot-Webhook.",
        { Kind: ProcessTriggerKind.ReviewApproved, Submission: null } => "ReviewApproved braucht submission.",
        { Kind: ProcessTriggerKind.RueckfrageResolved, Resolution: null } => "RueckfrageResolved braucht resolution.",
        { Reference.Length: > MaxReferenceLength } => $"reference darf höchstens {MaxReferenceLength} Zeichen haben.",
        _ => null,
    };
}
