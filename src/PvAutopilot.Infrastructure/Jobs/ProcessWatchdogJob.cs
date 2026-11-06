using Cronos;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Processes;

namespace PvAutopilot.Infrastructure.Jobs;

/// <summary>Wiederkehrender Hangfire-Job: Fristen prüfen und hängengebliebene Vorgänge neu einplanen.</summary>
public sealed class ProcessWatchdogJob(ProcessWatchdog watchdog, ILogger<ProcessWatchdogJob> logger)
{
    public const string RecurringJobId = "process-watchdog";

    // Der nächste reguläre Lauf kommt ohnehin; wenige Retries reichen.
    [AutomaticRetry(Attempts = 2)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task RunAsync(CancellationToken ct)
    {
        var report = await watchdog.RunAsync(ct);
        logger.LogInformation("Watchdog: {Checked} Vorgänge geprüft, {Rescheduled} neu eingeplant, {Overdue} überfällig",
            report.Checked, report.Rescheduled, report.Overdue);
    }
}

public static class RecurringJobs
{
    /// <summary>Registriert bzw. aktualisiert die wiederkehrenden Jobs (Cron aus der Konfiguration, UTC).</summary>
    public static void ScheduleRecurringJobs(this IServiceProvider services)
    {
        var watchdog = services.GetRequiredService<IOptions<ProcessWatchdogOptions>>().Value;
        services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<ProcessWatchdogJob>(
            ProcessWatchdogJob.RecurringJobId,
            j => j.RunAsync(CancellationToken.None),
            watchdog.Schedule,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }

    /// <summary>Hangfire akzeptiert Cron mit 5 Feldern oder mit Sekunden (6 Felder).</summary>
    public static bool IsValidCron(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;
        var format = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 6
            ? CronFormat.IncludeSeconds
            : CronFormat.Standard;
        return CronExpression.TryParse(expression, format, out _);
    }
}
