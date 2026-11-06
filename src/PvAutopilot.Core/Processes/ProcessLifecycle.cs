using Microsoft.Extensions.Logging;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core.Processes;

/// <summary>Ereignisse von außen (CRM, Postfach, Admin). Übersicht: docs/konzept/prozesssteuerung.md.</summary>
public enum ProcessTriggerKind
{
    /// <summary>Projekt hat die Trigger-Stage erreicht (Vollmacht zurück).</summary>
    Start,

    /// <summary>Einreichung im CRM freigegeben (Modus Review); <see cref="ProcessTrigger.Submission"/> nennt die Stufe.</summary>
    ReviewApproved,

    EinspeisezusageReceived,
    RueckfrageReceived,

    /// <summary>Rückfrage erledigt; <see cref="ProcessTrigger.Resolution"/> sagt wie.</summary>
    RueckfrageResolved,

    /// <summary>Anlage ist in Betrieb; <see cref="ProcessTrigger.Date"/> = Inbetriebnahmedatum.</summary>
    InbetriebnahmeReported,

    ZaehlerwechselCompleted,

    /// <summary>Kundenlogin für das MaStR liegt vor; löst nur einen neuen Lauf aus.</summary>
    MastrCredentialsProvided,

    Cancelled,
}

/// <summary>Ein Ereignis zu einem CRM-Projekt. Die optionalen Felder gelten je nach <see cref="Kind"/>.</summary>
public sealed record ProcessTrigger(ProcessTriggerKind Kind, string CrmProjectId)
{
    /// <summary>Bei <see cref="ProcessTriggerKind.ReviewApproved"/>: welche Stufe.</summary>
    public SubmissionKind? Submission { get; init; }

    /// <summary>Datum der Zusage, der Inbetriebnahme oder des Zählerwechsels. Fehlt es, gilt der heutige Tag.</summary>
    public DateOnly? Date { get; init; }

    public RueckfrageResolution? Resolution { get; init; }

    /// <summary>Vorgangsnummer des Netzbetreibers (keine personenbezogenen Daten).</summary>
    public string? Reference { get; init; }

    /// <summary>
    /// Wann das Ereignis an der Quelle eingetreten ist (HubSpot: <c>occurredAt</c>, Admin: Zeitpunkt der Anfrage).
    /// Freigabe und Rückfrage nutzen ihn, um verspätet oder erneut zugestellte alte Ereignisse zu erkennen.
    /// <c>null</c> = unbekannt, das Ereignis gilt dann als aktuell.
    /// </summary>
    public DateTimeOffset? OccurredAt { get; init; }
}

public enum TriggerResult
{
    Applied,

    /// <summary>Zum aktuellen Stand unpassend (z. B. Freigabe ohne wartende Einreichung) – nichts geändert.</summary>
    Ignored,

    /// <summary>Zum CRM-Projekt gibt es keinen Vorgang.</summary>
    NotFound,
}

/// <summary>
/// Nimmt Ereignisse entgegen, hält sie als Fakt am Vorgang fest und stößt die State-Machine an.
/// Den Zustand ändern nur die Schritte.
/// </summary>
public sealed class ProcessTriggerHandler(
    IProcessStore store,
    IProcessScheduler scheduler,
    ICrmStatusSink crmStatus,
    TimeProvider time,
    ILogger<ProcessTriggerHandler> logger)
{
    public async Task<TriggerResult> HandleAsync(ProcessTrigger trigger, CancellationToken ct)
    {
        if (trigger.Kind == ProcessTriggerKind.Start)
        {
            var created = await store.GetOrCreateAsync(trigger.CrmProjectId, ct);
            if (!created.State.IsTerminal())
                scheduler.Enqueue(created.Id);
            return TriggerResult.Applied;
        }

        var process = await store.FindByCrmProjectAsync(trigger.CrmProjectId, ct);
        if (process is null)
        {
            logger.LogWarning("{TriggerKind} für Projekt {CrmProjectId} ignoriert: kein Vorgang", trigger.Kind, trigger.CrmProjectId);
            return TriggerResult.NotFound;
        }

        if (process.State.IsTerminal() || !Apply(process, trigger))
        {
            logger.LogInformation("{TriggerKind} für Vorgang {ProcessId} in {State} ignoriert", trigger.Kind, process.Id, process.State);
            return TriggerResult.Ignored;
        }

        await store.SaveAsync(process, ct);
        // Blockierte Vorgänge laufen erst nach einem Resume weiter (der Fakt bleibt erhalten) – außer beim Storno.
        if (process.State != ProcessState.Blocked || trigger.Kind == ProcessTriggerKind.Cancelled)
            scheduler.Enqueue(process.Id);
        return TriggerResult.Applied;
    }

    private bool Apply(AnmeldeProcess process, ProcessTrigger trigger)
    {
        var now = time.GetUtcNow();
        var date = trigger.Date ?? ProcessCalendar.Today(time);

        switch (trigger.Kind)
        {
            case ProcessTriggerKind.ReviewApproved:
                return trigger.Submission is { } kind && process.ApproveReview(kind, now, trigger.OccurredAt);
            case ProcessTriggerKind.EinspeisezusageReceived:
                process.RecordEinspeisezusage(date, trigger.Reference, now);
                return true;
            case ProcessTriggerKind.RueckfrageReceived:
                return process.RecordRueckfrage(now, trigger.OccurredAt);
            case ProcessTriggerKind.RueckfrageResolved:
                return trigger.Resolution is { } resolution && process.ResolveRueckfrage(resolution, now);
            case ProcessTriggerKind.InbetriebnahmeReported:
                process.RecordInbetriebnahme(date, now);
                return true;
            case ProcessTriggerKind.ZaehlerwechselCompleted:
                process.RecordZaehlerwechsel(date, now);
                return true;
            case ProcessTriggerKind.MastrCredentialsProvided:
                return true;
            case ProcessTriggerKind.Cancelled:
                process.RequestCancellation(now);
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(trigger), trigger.Kind, null);
        }
    }

    public async Task<ResumeResult> ResumeAsync(Guid processId, CancellationToken ct)
    {
        var process = await store.GetAsync(processId, ct);
        if (process is null)
            return ResumeResult.NotFound;
        if (process.State != ProcessState.Blocked)
            return ResumeResult.NotBlocked;

        process.Resume(time.GetUtcNow());
        await store.SaveAsync(process, ct);
        // Sonst bliebe „blockiert“ im CRM stehen, falls der nächste Lauf nur wartet.
        await crmStatus.PublishAsync(process, ct);
        scheduler.Enqueue(process.Id);
        return ResumeResult.Resumed;
    }
}

public enum ResumeResult { Resumed, NotFound, NotBlocked }

/// <summary>Setzt einen Vorgang nach dem letzten fehlgeschlagenen Retry auf „blockiert“ und alarmiert.</summary>
public sealed class ProcessFailureHandler(
    IProcessStore store,
    IOperatorAlert alert,
    IHumanTaskSink tasks,
    ICrmStatusSink crmStatus,
    AuditLog audit,
    TimeProvider time,
    ILogger<ProcessFailureHandler> logger)
{
    public async Task BlockAsync(Guid processId, Exception error, CancellationToken ct)
    {
        logger.LogError(error, "Vorgang {ProcessId} nach allen Retries blockiert", processId);

        var process = await store.GetAsync(processId, ct);
        // Mehrere Retry-Ketten (z. B. nach wiederholtem Webhook) sollen nur einmal alarmieren.
        if (process is null || process.State == ProcessState.Blocked || process.State.IsTerminal())
            return;

        // Geht nach HubSpot und in die Alarm-Mail; die volle Exception steht nur im technischen Log (oben).
        var reason = BlockReasons.From(error);
        process.Block(reason, time.GetUtcNow());
        await store.SaveAsync(process, ct);

        await audit.RecordAsync(process, new AuditEvent("blocked", error.GetType().Name), ct);
        await crmStatus.PublishAsync(process, ct);
        // Zeitpunkt der Blockade im Schlüssel: eine erneute Blockade nach Resume bekommt einen neuen Task.
        await tasks.CreateTaskAsync(process.CrmProjectId, $"blocked:{process.Id:N}:{TaskKeys.Stamp(process.StateChangedAt)}",
            "Anmeldung blockiert", "Technischer Fehler – der Betreiber ist informiert.", ct);
        await alert.SendAsync($"Vorgang {process.Id} blockiert", reason,
            (error as AutomationException)?.Screenshot, ct);
    }
}

/// <summary>
/// Blockadegrund für HubSpot, Task und Alarm-Mail. Fremde Exception-Meldungen können personenbezogene Daten
/// enthalten (z. B. Validierungsfehler mit Namen oder Adresse); deshalb nur Meldungen aus einer Allowlist,
/// sonst nur der Typname. Die volle Exception steht im technischen Log.
/// </summary>
public static partial class BlockReasons
{
    public const string Timeout = "Zeitüberschreitung bei einem externen System";

    public static string From(Exception error)
    {
        var type = error.GetType().Name;
        return error switch
        {
            // Eigene Exceptions: die Meldungen schreiben wir selbst und ohne personenbezogene Daten.
            AutomationException or SubmissionConflictException or InvalidProcessTransitionException => $"{type}: {error.Message}",
            HttpRequestException http when OwnHttpStatusMessage().IsMatch(http.Message) => $"{type}: {http.Message}",
            HttpRequestException { StatusCode: { } status } => $"{type}: HTTP {(int)status}",
            TimeoutException or OperationCanceledException => $"{type}: {Timeout}",
            _ => type,
        };
    }

    /// <summary>Format unserer API-Clients, z. B. <c>Reonic /projects/123: HTTP 503</c> oder <c>HTTP 503</c>.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"^(?:[A-Za-z][\w ./-]{0,100}: )?HTTP \d{3}$")]
    private static partial System.Text.RegularExpressions.Regex OwnHttpStatusMessage();
}
