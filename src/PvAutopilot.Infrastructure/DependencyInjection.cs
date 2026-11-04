using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddOptions<SubmissionOptions>().Bind(configuration.GetSection(SubmissionOptions.SectionName));

        services.AddScoped<IProcessStore, EfProcessStore>();
        services.AddScoped<ISubmissionLedger, EfSubmissionLedger>();
        services.AddScoped<INetzbetreiberResolver, PlzNetzbetreiberResolver>();
        services.AddScoped<NetzbetreiberPlzImporter>();
        services.AddScoped<IProcessScheduler, HangfireProcessScheduler>();
        services.AddScoped<ProcessJob>();
        services.AddSingleton<IAuditSink, SerilogAuditSink>();
        services.AddSingleton<IOperatorAlert, LoggingOperatorAlert>();

        return services;
    }
}
