using PvAutopilot.Core.DataCard;

namespace PvAutopilot.Core.Processes.Steps;

public sealed class AggregateDataCardStep(NabDataCardAggregator aggregator, TimeProvider time) : IProcessStep
{
    public bool CanHandle(ProcessState state) => state == ProcessState.Created;

    public async Task<StepResult> ExecuteAsync(AnmeldeProcess process, CancellationToken ct)
    {
        var result = await aggregator.AggregateAsync(process.CrmProjectId, ct);
        if (!result.IsComplete)
        {
            return StepResult.HumanAction(
                $"datacard-missing:{process.Id:N}",
                "Anmeldung: Daten unvollständig",
                "Bitte ergänzen und den Vorgang erneut anstoßen: " + string.Join(", ", result.MissingFields));
        }

        process.AttachDataCard(result.Card!, time.GetUtcNow());
        // TODO: NAB-Datenkarte zusätzlich im CRM ablegen (Konzept: Prozess-DB + HubSpot).
        process.TransitionTo(ProcessState.DataCardAggregated, time.GetUtcNow());
        return StepResult.Continue;
    }
}
