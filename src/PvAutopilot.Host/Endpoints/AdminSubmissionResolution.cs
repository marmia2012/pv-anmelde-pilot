using System.Text.Json;
using System.Text.Json.Serialization;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;
using PvAutopilot.Infrastructure.Jobs;

namespace PvAutopilot.Host.Endpoints;

public enum SubmissionResolutionOutcome
{
    /// <summary>Die Einreichung ist beim Empfänger angekommen.</summary>
    Sent,

    /// <summary>Nachweislich nicht angekommen; der nächste Lauf versendet erneut.</summary>
    NotSent,
}

public sealed record SubmissionResolutionRequest(SubmissionResolutionOutcome? Outcome, string? Reference);

/// <summary>
/// Ausweg bei unklarem Versand: Nach manueller Prüfung beim Netzbetreiber bzw. im MaStR meldet ein Mensch,
/// ob die Einreichung angekommen ist. Nur für Ledger-Einträge auf <see cref="SubmissionStatus.Sending"/>.
/// </summary>
public static class AdminSubmissionResolution
{
    private const int MaxReferenceLength = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    /// <summary>
    /// 200 geklärt · 409 Vorgang nicht blockiert, gerade in Bearbeitung oder Eintrag steht nicht auf Sending ·
    /// 404 Vorgang oder Eintrag unbekannt · 400 ungültige Anfrage.
    /// </summary>
    /// <remarks>
    /// Nur für blockierte Vorgänge und unter dem Prozess-Lock: Steht ein Eintrag auf Sending, während ein Lauf
    /// gerade versendet, würde „NotSent“ einen zweiten Versand auslösen.
    /// </remarks>
    public static async Task<IResult> PostAsync(
        Guid id, string kind, Stream body, IProcessStore store, ISubmissionLedger ledger,
        ProcessTriggerHandler handler, AuditLog audit, IProcessRunLock runLock, CancellationToken ct)
    {
        // Nur Namen, keine Zahlen oder Kombinationen wie Enum.TryParse sie zuließe.
        var match = Enum.GetValues<SubmissionKind>().Where(k => string.Equals(k.ToString(), kind, StringComparison.OrdinalIgnoreCase)).ToList();
        if (match is not [var submissionKind])
            return Results.BadRequest(new { error = "Unbekannte Einreichungsart." });

        SubmissionResolutionRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<SubmissionResolutionRequest>(body, Json, ct);
        }
        catch (JsonException)
        {
            // Keine Details zurückgeben: die Meldung könnte Teile des Bodys enthalten.
            return Results.BadRequest(new { error = "Ungültiges JSON oder unbekannter Enum-Wert." });
        }

        if (Validate(request) is { } error)
            return Results.BadRequest(new { error });

        // Nicht warten: Läuft gerade ein Lauf, ist der Vorgang ohnehin nicht (mehr) sicher blockiert.
        await using var held = await runLock.TryAcquireAsync(id, TimeSpan.Zero, ct);
        if (held is null)
            return Results.Conflict(new { error = "Der Vorgang wird gerade bearbeitet. Bitte in einer Minute erneut versuchen." });

        var process = await store.GetAsync(id, ct);
        if (process is null)
            return Results.NotFound();
        if (process.State != ProcessState.Blocked)
        {
            return Results.Conflict(new
            {
                error = $"Vorgang steht in {process.State}, nicht blockiert. Ein Versand lässt sich nur bei einem "
                    + "blockierten Vorgang klären; sonst könnte ein laufender Versand doppelt ausgelöst werden.",
            });
        }

        var key = process.CurrentSubmissionKey(submissionKind);
        var record = await ledger.FindAsync(key.Value, ct);
        if (record is null)
            return Results.NotFound(new { error = $"Keine Einreichung {key.Value} im Ledger." });

        var outcome = request!.Outcome == SubmissionResolutionOutcome.Sent ? SubmissionStatus.Sent : SubmissionStatus.Failed;
        var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();
        if (record.Status != SubmissionStatus.Sending
            || !await ledger.ResolveSendingAsync(key.Value, outcome, reference, ct))
            return Results.Conflict(new { error = $"{key.Value} ist nicht im Versand (Status {record.Status}); nichts zu klären." });

        var label = submissionKind.Label();
        await audit.RecordAsync(process, new AuditEvent("submission-resolved", outcome == SubmissionStatus.Sent
            ? $"{label}: Versand manuell als angekommen bestätigt"
            : $"{label}: Versand manuell als nicht angekommen gemeldet, wird erneut versendet"), ct);

        // Der eingeplante Lauf wartet auf das Lock, bis diese Anfrage es freigibt.
        var resumed = await handler.ResumeAsync(process.Id, ct) == ResumeResult.Resumed;
        return Results.Ok(new { submission = key.Value, status = outcome.ToString(), resumed });
    }

    private static string? Validate(SubmissionResolutionRequest? request) => request switch
    {
        null or { Outcome: null } => "outcome fehlt (Sent oder NotSent).",
        { Outcome: SubmissionResolutionOutcome.NotSent, Reference: not null } => "reference nur bei outcome Sent.",
        { Reference.Length: > MaxReferenceLength } => $"reference darf höchstens {MaxReferenceLength} Zeichen haben.",
        _ => null,
    };
}
