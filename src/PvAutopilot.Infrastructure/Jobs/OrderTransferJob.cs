using System.Security.Cryptography;
using System.Text;
using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Orders;

namespace PvAutopilot.Infrastructure.Jobs;

/// <summary>
/// Hangfire-Job: überträgt den unterschriebenen Auftrag eines Reonic-Projekts ins CRM (<see cref="OrderTransferService"/>).
/// Je Reonic-Projekt höchstens ein Lauf gleichzeitig (<see cref="IProcessRunLock"/> mit <see cref="LockId"/>); belegt →
/// in <see cref="RescheduleWhenBusy"/> neu eingeplant. Scheitert der letzte Versuch technisch, geht ein Alarm an den
/// Betreiber und der Job endet ohne Fehler (Muster <see cref="ProcessJob"/>).
/// </summary>
public sealed class OrderTransferJob(
    OrderTransferService service,
    IProcessRunLock runLock,
    IBackgroundJobClient jobs,
    IOperatorAlert alert,
    ILogger<OrderTransferJob> logger)
{
    public const int MaxRetries = 4;

    public static readonly TimeSpan LockWait = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan RescheduleWhenBusy = TimeSpan.FromMinutes(1);

    [AutomaticRetry(Attempts = MaxRetries, DelaysInSeconds = new[] { 30, 120, 600, 1800 })]
    public Task RunAsync(string planningProjectId, string? signatureRequestId, PerformContext? context, CancellationToken ct) =>
        RunCoreAsync(planningProjectId, signatureRequestId, context?.GetJobParameter<int>("RetryCount") ?? 0, ct);

    /// <param name="retryCount">Bisherige Retries dieses Jobs (Hangfire-Jobparameter <c>RetryCount</c>).</param>
    internal async Task RunCoreAsync(string planningProjectId, string? signatureRequestId, int retryCount, CancellationToken ct)
    {
        await using var held = await runLock.TryAcquireAsync(LockId(planningProjectId), LockWait, ct);
        if (held is null)
        {
            logger.LogInformation("Auftragsübernahme für Reonic-Projekt {ReonicProjectId} läuft bereits; Lauf in {Delay} neu eingeplant",
                planningProjectId, RescheduleWhenBusy);
            jobs.Schedule<OrderTransferJob>(j => j.RunAsync(planningProjectId, signatureRequestId, null, CancellationToken.None), RescheduleWhenBusy);
            return;
        }

        try
        {
            await service.TransferAsync(planningProjectId, signatureRequestId, ct);
        }
        // Nur ein Shutdown ist kein Fehler; ein HttpClient-Timeout (OperationCanceledException) zählt als technischer Fehler.
        catch (Exception ex) when (!ct.IsCancellationRequested && retryCount >= MaxRetries)
        {
            logger.LogError("Auftragsübernahme für Reonic-Projekt {ReonicProjectId} (Signatur {SignatureRequestId}) nach {RetryCount} Retries gescheitert: {ExceptionType}",
                planningProjectId, signatureRequestId, retryCount, ex.GetType().Name);
            try
            {
                await alert.SendAsync($"Auftragsübernahme für Reonic-Projekt {planningProjectId} gescheitert",
                    $"Reonic-Projekt {planningProjectId}, Signatur {signatureRequestId ?? "–"}: technischer Fehler {Describe(ex)} nach allen Retries. "
                    + "Nach der Behebung über POST /admin/reonic/projects/{projectId}/order-sync nachholen.",
                    null, CancellationToken.None);
            }
            catch (Exception alertError)
            {
                logger.LogError("Alarm zur Auftragsübernahme für Reonic-Projekt {ReonicProjectId} nicht gesendet: {ExceptionType}",
                    planningProjectId, alertError.GetType().Name);
            }
        }
    }

    /// <summary>
    /// Fehlerbeschreibung für den Alarm. <see cref="HttpRequestException"/>: Typ, Statuscode und Message – die Messages der
    /// Api-Clients und des Downloaders enthalten nur Pfad und Status, nie Body oder signierte URL. Alles andere: nur der Typ.
    /// </summary>
    public static string Describe(Exception ex) => ex switch
    {
        HttpRequestException http => http.StatusCode is { } status
            ? $"{nameof(HttpRequestException)} (HTTP {(int)status}: {http.Message})"
            : $"{nameof(HttpRequestException)} ({http.Message})",
        _ => ex.GetType().Name,
    };

    /// <summary>Stabile Guid aus <c>order:&lt;projekt&gt;</c> für das Lock (eigener Schlüsselraum neben den Vorgangs-IDs).</summary>
    public static Guid LockId(string planningProjectId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"order:{planningProjectId}")).AsSpan(0, 16));
}

public sealed class HangfireOrderTransferScheduler(IBackgroundJobClient jobs) : IOrderTransferScheduler
{
    public void Enqueue(string planningProjectId, string? signatureRequestId) =>
        jobs.Enqueue<OrderTransferJob>(j => j.RunAsync(planningProjectId, signatureRequestId, null, CancellationToken.None));
}
