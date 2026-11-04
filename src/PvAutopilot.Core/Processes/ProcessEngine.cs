using Microsoft.Extensions.Logging;
using PvAutopilot.Core.Notifications;

namespace PvAutopilot.Core.Processes;

public interface IProcessStore
{
    Task<AnmeldeProcess?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Liefert den Vorgang zum CRM-Projekt oder legt ihn an (idempotent, auch bei parallelen Webhooks).</summary>
    Task<AnmeldeProcess> GetOrCreateAsync(string crmProjectId, CancellationToken ct);

    Task<AnmeldeProcess?> FindByCrmProjectAsync(string crmProjectId, CancellationToken ct);

    Task SaveAsync(AnmeldeProcess process, CancellationToken ct);
}

/// <summary>Stellt einen Lauf der State-Machine in die Job-Queue (Hangfire).</summary>
public interface IProcessScheduler
{
    void Enqueue(Guid processId);
}

/// <summary>Ein Schritt der State-Machine. Schritte müssen idempotent sein und dürfen mehrfach laufen.</summary>
public interface IProcessStep
{
    bool CanHandle(ProcessState state);

    /// <summary>Führt den Schritt aus und ändert dabei den Zustand über <see cref="AnmeldeProcess.TransitionTo"/>.</summary>
    Task<StepResult> ExecuteAsync(AnmeldeProcess process, CancellationToken ct);
}

public abstract record StepResult
{
    public static readonly StepResult Continue = new ContinueResult();

    public static StepResult Wait(string reason) => new WaitResult(reason);

    public static StepResult HumanAction(string taskKey, string title, string body) =>
        new HumanActionResult(taskKey, title, body);

    /// <summary>Zustand wurde geändert, nächster Schritt kann sofort laufen.</summary>
    public sealed record ContinueResult : StepResult;

    /// <summary>Wartet auf ein externes Ereignis (Mail, Webhook, Freigabe).</summary>
    public sealed record WaitResult(string Reason) : StepResult;

    /// <summary>Fachliches Problem: Aufgabe im CRM anlegen, Vorgang bleibt stehen.</summary>
    public sealed record HumanActionResult(string TaskKey, string Title, string Body) : StepResult;
}

/// <summary>Führt die Schritte eines Vorgangs aus, bis er auf ein externes Ereignis wartet.</summary>
public sealed class ProcessEngine(
    IProcessStore store,
    IEnumerable<IProcessStep> steps,
    IHumanTaskSink tasks,
    AuditLog audit,
    ILogger<ProcessEngine> logger)
{
    private const int MaxStepsPerRun = 20;

    public async Task RunAsync(Guid processId, CancellationToken ct)
    {
        var process = await store.GetAsync(processId, ct)
            ?? throw new InvalidOperationException($"Vorgang {processId} nicht gefunden.");

        for (var i = 0; i < MaxStepsPerRun; i++)
        {
            if (process.State is ProcessState.Completed or ProcessState.Blocked)
                return;

            var step = steps.FirstOrDefault(s => s.CanHandle(process.State));
            if (step is null)
            {
                logger.LogInformation("Vorgang {ProcessId} wartet in {State} (kein Schritt zuständig)", process.Id, process.State);
                return;
            }

            var before = process.State;
            var result = await step.ExecuteAsync(process, ct);
            await store.SaveAsync(process, ct);

            if (process.State != before)
                await audit.RecordAsync(process, new AuditEvent("state-changed", $"{before} → {process.State}"), ct);

            switch (result)
            {
                case StepResult.WaitResult wait:
                    logger.LogInformation("Vorgang {ProcessId} wartet in {State}: {Reason}", process.Id, process.State, wait.Reason);
                    return;
                case StepResult.HumanActionResult action:
                    await tasks.CreateTaskAsync(process.CrmProjectId, action.TaskKey, action.Title, action.Body, ct);
                    await audit.RecordAsync(process, new AuditEvent("human-action", action.Title), ct);
                    return;
                case StepResult.ContinueResult when process.State == before:
                    throw new InvalidOperationException($"Schritt {step.GetType().Name} meldet Continue ohne Zustandswechsel.");
            }
        }

        throw new InvalidOperationException($"Vorgang {processId}: mehr als {MaxStepsPerRun} Schritte in einem Lauf.");
    }
}
