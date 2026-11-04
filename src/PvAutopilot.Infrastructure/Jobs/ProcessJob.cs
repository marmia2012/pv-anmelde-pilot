using Hangfire;
using Hangfire.Server;
using PvAutopilot.Core.Processes;

namespace PvAutopilot.Infrastructure.Jobs;

/// <summary>Hangfire-Job: ein Lauf der State-Machine für einen Vorgang.</summary>
public sealed class ProcessJob(ProcessEngine engine, ProcessFailureHandler failureHandler)
{
    public const int MaxRetries = 4;

    [AutomaticRetry(Attempts = MaxRetries, DelaysInSeconds = new[] { 30, 120, 600, 1800 })]
    public async Task RunAsync(Guid processId, PerformContext? context, CancellationToken ct)
    {
        try
        {
            await engine.RunAsync(processId, ct);
        }
        // Nur ein Shutdown (ct abgebrochen) ist kein Fehler. Ein HttpClient-Timeout wirft ebenfalls
        // OperationCanceledException und muss beim letzten Versuch blockieren.
        catch (Exception ex) when (!ct.IsCancellationRequested && IsLastAttempt(context))
        {
            // Letzter Versuch: blockieren und alarmieren statt den Job endgültig fehlschlagen zu lassen.
            await failureHandler.BlockAsync(processId, ex, CancellationToken.None);
        }
    }

    private static bool IsLastAttempt(PerformContext? context) =>
        context?.GetJobParameter<int>("RetryCount") >= MaxRetries;
}

public sealed class HangfireProcessScheduler(IBackgroundJobClient jobs) : IProcessScheduler
{
    public void Enqueue(Guid processId) => jobs.Enqueue<ProcessJob>(j => j.RunAsync(processId, null, CancellationToken.None));
}
