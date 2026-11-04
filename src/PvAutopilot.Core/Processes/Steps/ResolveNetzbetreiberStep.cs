using PvAutopilot.Core.Netzbetreiber;

namespace PvAutopilot.Core.Processes.Steps;

public sealed class ResolveNetzbetreiberStep(
    INetzbetreiberResolver resolver,
    IEnumerable<INetzbetreiberAdapter> adapters,
    TimeProvider time) : IProcessStep
{
    public bool CanHandle(ProcessState state) => state == ProcessState.DataCardAggregated;

    public async Task<StepResult> ExecuteAsync(AnmeldeProcess process, CancellationToken ct)
    {
        var address = process.DataCard!.Anlagenadresse;
        var code = await resolver.ResolveAsync(address, ct);
        if (code is null)
        {
            return StepResult.HumanAction(
                $"netzbetreiber-unknown:{process.Id:N}",
                "Anmeldung: Netzbetreiber nicht ermittelbar",
                $"Für PLZ {address.Plz} {address.Ort} ist kein eindeutiger Netzbetreiber hinterlegt. Bitte PLZ-Tabelle ergänzen.");
        }

        if (!adapters.Any(a => a.Code == code))
        {
            return StepResult.HumanAction(
                $"netzbetreiber-unsupported:{process.Id:N}",
                "Anmeldung: Netzbetreiber noch nicht automatisiert",
                $"Netzbetreiber {code} wird noch nicht automatisch angemeldet. Bitte manuell anmelden.");
        }

        process.AssignNetzbetreiber(code, time.GetUtcNow());
        process.TransitionTo(ProcessState.NetzbetreiberResolved, time.GetUtcNow());
        return StepResult.Continue;
    }
}
