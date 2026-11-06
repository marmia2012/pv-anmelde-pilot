using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PvAutopilot.Core.Netzbetreiber;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;
using PvAutopilot.Infrastructure.Jobs;
using PvAutopilot.Infrastructure.Notifications;
using PvAutopilot.Infrastructure.Persistence;

namespace PvAutopilot.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Autopilot";

    public static IServiceCollection AddAutopilotInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} fehlt.");

        services.AddDbContext<AutopilotDbContext>(o => o.UseNpgsql(connectionString));
        services.AddOptions<SubmissionOptions>()
            .Bind(configuration.GetSection(SubmissionOptions.SectionName))
            .Validate(o => !o.ContinueAfterDryRun || o.Mode == SubmissionMode.DryRun,
                "Submission:ContinueAfterDryRun ist nur mit Submission:Mode=DryRun erlaubt (Demo auf Staging).")
            // Auch per Umgebungsvariable nicht: Produktion darf nie einen Versand simulieren.
            .Validate<IHostEnvironment>((o, env) => !o.ContinueAfterDryRun || !env.IsProduction(),
                "Submission:ContinueAfterDryRun ist in der Umgebung Production nicht erlaubt.")
            .ValidateOnStart();
        services.AddOptions<ProcessWatchdogOptions>()
            .Bind(configuration.GetSection(ProcessWatchdogOptions.SectionName))
            .Validate(o => RecurringJobs.IsValidCron(o.Schedule), "Watchdog:Schedule ist kein gültiger Cron-Ausdruck.")
            .Validate(o => o.StalledAfter > TimeSpan.Zero, "Watchdog:StalledAfter muss größer als 0 sein.")
            .Validate(o => o.MaxWait.Values.All(v => v > TimeSpan.Zero), "Watchdog:MaxWait: alle Fristen müssen größer als 0 sein.")
            .ValidateOnStart();

        services.AddScoped<IProcessStore, EfProcessStore>();
        services.AddScoped<ISubmissionLedger, EfSubmissionLedger>();
        services.AddScoped<ICrmWriteLedger, EfCrmWriteLedger>();
        services.AddScoped<INetzbetreiberResolver, PlzNetzbetreiberResolver>();
        services.AddScoped<NetzbetreiberPlzImporter>();
        services.AddScoped<IProcessScheduler, HangfireProcessScheduler>();
        services.AddScoped<ProcessJob>();
        services.AddScoped<IProcessRunLock, PostgresProcessRunLock>();
        services.AddScoped<ProcessWatchdogJob>();
        // Registriert normalerweise der Kern; TryAdd, damit es keine Doppelregistrierung gibt.
        services.TryAddScoped<ProcessWatchdog>();
        services.AddSingleton<IAuditSink, SerilogAuditSink>();
        services.AddSingleton<IOperatorAlert, LoggingOperatorAlert>();

        return services;
    }
}
