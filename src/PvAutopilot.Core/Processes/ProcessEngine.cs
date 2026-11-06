using Microsoft.Extensions.Logging;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core.Processes;

public interface IProcessStore
{
    Task<AnmeldeProcess?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Liefert den Vorgang zum CRM-Projekt oder legt ihn an (idempotent, auch bei parallelen Webhooks).</summary>
    Task<AnmeldeProcess> GetOrCreateAsync(string crmProjectId, CancellationToken ct);

    Task<AnmeldeProcess?> FindByCrmProjectAsync(string crmProjectId, CancellationToken ct);

    /// <summary>Alle Vorgänge, die nicht abgeschlossen oder storniert sind (für den Watchdog).</summary>
    Task<IReadOnlyList<AnmeldeProcess>> ListOpenAsync(CancellationToken ct);

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
public sealed class ProcessEngine
{
    private const int MaxStepsPerRun = 20;

    private readonly IProcessStore _store;
    private readonly IReadOnlyDictionary<ProcessState, IProcessStep> _steps;
    private readonly ISubmissionLedger _ledger;
    private readonly IHumanTaskSink _tasks;
    private readonly ICrmStatusSink _crmStatus;
    private readonly AuditLog _audit;
    private readonly TimeProvider _time;
    private readonly ILogger<ProcessEngine> _logger;

    public ProcessEngine(
        IProcessStore store,
        IEnumerable<IProcessStep> steps,
        ISubmissionLedger ledger,
        IHumanTaskSink tasks,
        ICrmStatusSink crmStatus,
        AuditLog audit,
        TimeProvider time,
        ILogger<ProcessEngine> logger)
    {
        _store = store;
        _steps = MapSteps(steps);
        _ledger = ledger;
        _tasks = tasks;
        _crmStatus = crmStatus;
        _audit = audit;
        _time = time;
        _logger = logger;
    }

    /// <summary>Ordnet jedem Zustand seinen Schritt zu. Zwei Schritte für denselben Zustand sind ein Konfigurationsfehler.</summary>
    public static IReadOnlyDictionary<ProcessState, IProcessStep> MapSteps(IEnumerable<IProcessStep> steps)
    {
        var list = steps.ToList();
        var map = new Dictionary<ProcessState, IProcessStep>();
        foreach (var state in Enum.GetValues<ProcessState>())
        {
            var responsible = list.Where(s => s.CanHandle(state)).ToList();
            if (responsible.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Für Zustand {state} sind mehrere Schritte zuständig: {string.Join(", ", responsible.Select(s => s.GetType().Name))}.");
            }
            if (responsible.Count == 1)
                map[state] = responsible[0];
        }
        return map;
    }

    public async Task RunAsync(Guid processId, CancellationToken ct)
    {
        var process = await _store.GetAsync(processId, ct)
            ?? throw new InvalidOperationException($"Vorgang {processId} nicht gefunden.");

        for (var i = 0; i < MaxStepsPerRun; i++)
        {
            if (process.CancellationRequestedAt is not null && !process.State.IsTerminal())
            {
                await CancelAsync(process, ct);
                return;
            }

            if (process.State.IsTerminal() || process.State == ProcessState.Blocked)
                return;

            if (!_steps.TryGetValue(process.State, out var step))
            {
                _logger.LogWarning("Vorgang {ProcessId} steht in {State}: kein Schritt zuständig", process.Id, process.State);
                return;
            }

            var before = process.State;
            var result = await step.ExecuteAsync(process, ct);
            await _store.SaveAsync(process, ct);

            if (process.State != before)
                await StateChangedAsync(process, before, ct);

            switch (result)
            {
                case StepResult.WaitResult wait:
                    _logger.LogInformation("Vorgang {ProcessId} wartet in {State}: {Reason}", process.Id, process.State, wait.Reason);
                    return;
                case StepResult.HumanActionResult action:
                    await _tasks.CreateTaskAsync(process.CrmProjectId, action.TaskKey, action.Title, action.Body, ct);
                    await _audit.RecordAsync(process, new AuditEvent("human-action", action.Title), ct);
                    return;
                case StepResult.ContinueResult when process.State == before:
                    throw new InvalidOperationException($"Schritt {step.GetType().Name} meldet Continue ohne Zustandswechsel.");
            }
        }

        throw new InvalidOperationException($"Vorgang {processId}: mehr als {MaxStepsPerRun} Schritte in einem Lauf.");
    }

    /// <summary>Storno gilt aus jedem nicht-terminalen Zustand, auch aus Blocked.</summary>
    private async Task CancelAsync(AnmeldeProcess process, CancellationToken ct)
    {
        var before = process.State;
        // Sending zählt mit: ob der abgebrochene Versand angekommen ist, ist unklar.
        var submitted = (await _ledger.ListByProcessAsync(process.Id, ct))
            .Where(r => r.Kind != SubmissionKind.Mastr && r.Status is SubmissionStatus.Sent or SubmissionStatus.Sending)
            .Select(r => r.Kind)
            .Distinct()
            .ToList();

        process.TransitionTo(ProcessState.Cancelled, _time.GetUtcNow(), "Projekt storniert");
        await _store.SaveAsync(process, ct);
        await StateChangedAsync(process, before, ct);

        if (submitted.Count > 0)
        {
            await _tasks.CreateTaskAsync(process.CrmProjectId, $"withdraw:{process.Id:N}",
                "Antrag beim Netzbetreiber zurückziehen",
                $"Das Projekt wurde storniert. Bereits eingereicht: {string.Join(", ", submitted.Select(k => k.Label()))}. "
                + "Bitte den Antrag beim Netzbetreiber zurückziehen.", ct);
            await _audit.RecordAsync(process, new AuditEvent("human-action", "Antrag beim Netzbetreiber zurückziehen"), ct);
        }
    }

    private async Task StateChangedAsync(AnmeldeProcess process, ProcessState before, CancellationToken ct)
    {
        await _audit.RecordAsync(process, new AuditEvent("state-changed", $"{before} → {process.State}"), ct);
        await _crmStatus.PublishAsync(process, ct);
    }
}

/// <summary>Bausteine für Task-Schlüssel. Der Task-Sink legt je Schlüssel nur einen Task an.</summary>
public static class TaskKeys
{
    /// <summary>
    /// Zeitstempel für den Schlüssel, z. B. der Eintritt in einen Zustand: Wer denselben Zustand später erneut
    /// erreicht (nach Resume oder neuer Revision), bekommt einen neuen Task statt des alten, erledigten.
    /// </summary>
    public static string Stamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
}
