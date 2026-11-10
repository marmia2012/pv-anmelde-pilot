using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Orders;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Processes.Steps;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddAutopilotCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        // Ohne Konfiguration kein Instanz-Errichter: der Aggregator meldet dann „Anlagenerrichter“ als fehlend.
        services.AddOptions<ErrichterOptions>().Bind(configuration.GetSection(ErrichterOptions.SectionName));
        services.AddScoped<AuditLog>();
        services.AddScoped<NabDataCardAggregator>();
        services.AddScoped<SubmissionGate>();
        services.AddScoped<ProcessEngine>();
        services.AddScoped<ProcessTriggerHandler>();
        services.AddScoped<ProcessFailureHandler>();
        services.AddScoped<ProcessWatchdog>();

        // Auftragsübernahme Planungssoftware → CRM, eigener Ablauf außerhalb der State-Machine.
        services.AddScoped<OrderTransferService>();

        // Je Zustand genau ein Schritt; ProcessEngine prüft das beim Erzeugen.
        services.AddScoped<IProcessStep, AggregateDataCardStep>();
        services.AddScoped<IProcessStep, ResolveNetzbetreiberStep>();
        foreach (var stage in new[] { SubmissionStages.Netzanmeldung, SubmissionStages.Inbetriebnahme, SubmissionStages.Zaehlerwechsel })
            services.AddScoped<IProcessStep>(sp => ActivatorUtilities.CreateInstance<NetzbetreiberSubmissionStep>(sp, stage));
        services.AddScoped<IProcessStep, AwaitNetzbetreiberResponseStep>();
        services.AddScoped<IProcessStep, AwaitInbetriebnahmeStep>();
        services.AddScoped<IProcessStep, AwaitZaehlerwechselStep>();
        services.AddScoped<IProcessStep, MastrRegistrationStep>();
        services.AddScoped<IProcessStep, CompleteStep>();

        return services;
    }
}
