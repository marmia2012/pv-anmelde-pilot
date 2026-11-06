using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PvAutopilot.Core.Processes;

namespace PvAutopilot.Infrastructure.Jobs;

/// <summary>Hangfire-Job: ein Lauf der State-Machine für einen Vorgang.</summary>
/// <remarks>
/// Je Vorgang läuft höchstens ein Lauf gleichzeitig (<see cref="IProcessRunLock"/>). Hangfires
/// <c>[DisableConcurrentExecution("process:{0}", …)]</c> könnte das seit 1.8 auch, wirft aber nach dem Timeout
/// eine Exception: Das verbraucht Retries und lässt den Job nach dem letzten Versuch fehlschlagen, ohne dass
/// <see cref="ProcessFailureHandler"/> greift. Deshalb ein eigenes Lock mit Neueinplanung statt Fehler.
/// </remarks>
public sealed class ProcessJob(
    ProcessEngine engine,
    IServiceScopeFactory scopes,
    IProcessRunLock runLock,
    IBackgroundJobClient jobs,
    ILogger<ProcessJob> logger)
{
    public const int MaxRetries = 4;

    /// <summary>So lange wartet ein Lauf auf einen parallelen Lauf desselben Vorgangs.</summary>
    public static readonly TimeSpan LockWait = TimeSpan.FromSeconds(30);

    /// <summary>Ist der Vorgang danach noch belegt, wird der Lauf neu eingeplant statt verworfen.</summary>
    public static readonly TimeSpan RescheduleWhenBusy = TimeSpan.FromMinutes(1);

    [AutomaticRetry(Attempts = MaxRetries, DelaysInSeconds = new[] { 30, 120, 600, 1800 })]
    public Task RunAsync(Guid processId, PerformContext? context, CancellationToken ct) =>
        RunCoreAsync(processId, context?.GetJobParameter<int>("RetryCount") ?? 0, ct);

    /// <param name="retryCount">Bisherige Retries dieses Jobs (Hangfire-Jobparameter <c>RetryCount</c>).</param>
    internal async Task RunCoreAsync(Guid processId, int retryCount, CancellationToken ct)
    {
        // Datenbankfehler beim Lock: normaler Hangfire-Retry (blockieren ginge ohne Datenbank ohnehin nicht).
        // Die Freigabe wirft nie (siehe PostgresProcessRunLock), damit sie das Blockieren unten nicht überdeckt.
        await using var held = await runLock.TryAcquireAsync(processId, LockWait, ct);
        if (held is null)
        {
            // Nicht still enden: Ein Ereignis kann einen Fakt gespeichert haben, den der laufende Lauf
            // nicht mehr sieht (er hat den Vorgang schon geladen). Der neue Lauf holt das nach.
            logger.LogInformation("Vorgang {ProcessId} läuft bereits; Lauf in {Delay} neu eingeplant", processId, RescheduleWhenBusy);
            jobs.Schedule<ProcessJob>(j => j.RunAsync(processId, null, CancellationToken.None), RescheduleWhenBusy);
            return;
        }

        try
        {
            await engine.RunAsync(processId, ct);
        }
        // Nur ein Shutdown (ct abgebrochen) ist kein Fehler. Ein HttpClient-Timeout wirft ebenfalls
        // OperationCanceledException und muss beim letzten Versuch blockieren.
        catch (Exception ex) when (!ct.IsCancellationRequested && retryCount >= MaxRetries)
        {
            // Letzter Versuch: blockieren und alarmieren statt den Job endgültig fehlschlagen zu lassen.
            // Frischer DI-Scope: Der DbContext des Laufs kann nach dem Fehler unbrauchbar sein (fehlgeschlagenes
            // SaveChanges, halb geänderte Entities) – blockiert wird mit einem neu geladenen Vorgang.
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ProcessFailureHandler>()
                .BlockAsync(processId, ex, CancellationToken.None);
        }
    }
}

public sealed class HangfireProcessScheduler(IBackgroundJobClient jobs) : IProcessScheduler
{
    public void Enqueue(Guid processId) => jobs.Enqueue<ProcessJob>(j => j.RunAsync(processId, null, CancellationToken.None));
}
