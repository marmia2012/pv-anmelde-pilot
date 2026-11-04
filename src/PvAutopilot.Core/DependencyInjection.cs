using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Processes.Steps;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddAutopilotCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AuditLog>();
        services.AddScoped<NabDataCardAggregator>();
        services.AddScoped<SubmissionGate>();
        services.AddScoped<ProcessEngine>();
        services.AddScoped<ProcessTriggerHandler>();
        services.AddScoped<ProcessFailureHandler>();

        services.AddScoped<IProcessStep, AggregateDataCardStep>();
        services.AddScoped<IProcessStep, ResolveNetzbetreiberStep>();
        services.AddScoped<IProcessStep, SubmitNetzanmeldungStep>();
        // TODO: Einspeisezusage (Mail-Eingang), Inbetriebnahme (E.8), Zählerwechsel, MaStR.

        return services;
    }
}
