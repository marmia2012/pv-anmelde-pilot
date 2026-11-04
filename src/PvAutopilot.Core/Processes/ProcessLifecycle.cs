using Microsoft.Extensions.Logging;
using PvAutopilot.Core.Notifications;

namespace PvAutopilot.Core.Processes;

public enum ProcessTriggerKind
{
    /// <summary>Projekt hat die Trigger-Stage erreicht (Vollmacht zurück).</summary>
    Start,

    /// <summary>Antrag wurde im CRM zur Einreichung freigegeben (Modus Review).</summary>
    ReviewApproved,
}

public sealed record ProcessTrigger(ProcessTriggerKind Kind, string CrmProjectId);

/// <summary>Nimmt Trigger aus dem CRM entgegen und stößt die State-Machine an.</summary>
public sealed class ProcessTriggerHandler(
    IProcessStore store,
    IProcessScheduler scheduler,
    TimeProvider time,
    ILogger<ProcessTriggerHandler> logger)
{
    public async Task HandleAsync(ProcessTrigger trigger, CancellationToken ct)
    {
        switch (trigger.Kind)
        {
            case ProcessTriggerKind.Start:
                var process = await store.GetOrCreateAsync(trigger.CrmProjectId, ct);
                scheduler.Enqueue(process.Id);
                break;

            case ProcessTriggerKind.ReviewApproved:
                var existing = await store.FindByCrmProjectAsync(trigger.CrmProjectId, ct);
                if (existing?.State != ProcessState.AwaitingReview)
                {
                    logger.LogWarning("Freigabe für Projekt {CrmProjectId} ignoriert: kein Vorgang in AwaitingReview", trigger.CrmProjectId);
                    return;
                }
                existing.ApproveReview(time.GetUtcNow());
                await store.SaveAsync(existing, ct);
                scheduler.Enqueue(existing.Id);
                break;
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
    AuditLog audit,
    TimeProvider time,
    ILogger<ProcessFailureHandler> logger)
{
    public async Task BlockAsync(Guid processId, Exception error, CancellationToken ct)
    {
        logger.LogError(error, "Vorgang {ProcessId} nach allen Retries blockiert", processId);

        var process = await store.GetAsync(processId, ct);
        // Mehrere Retry-Ketten (z. B. nach wiederholtem Webhook) sollen nur einmal alarmieren.
        if (process is null || process.State == ProcessState.Blocked)
            return;

        var reason = $"{error.GetType().Name}: {error.Message}";
        process.Block(reason, time.GetUtcNow());
        await store.SaveAsync(process, ct);

        await audit.RecordAsync(process, new AuditEvent("blocked", error.GetType().Name), ct);
        await tasks.CreateTaskAsync(process.CrmProjectId, $"blocked:{process.Id:N}",
            "Anmeldung blockiert", "Technischer Fehler – der Betreiber ist informiert.", ct);
        await alert.SendAsync($"Vorgang {process.Id} blockiert", reason,
            (error as AutomationException)?.Screenshot, ct);
    }
}
