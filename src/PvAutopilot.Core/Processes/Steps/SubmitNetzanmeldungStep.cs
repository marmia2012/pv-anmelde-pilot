using PvAutopilot.Core.Netzbetreiber;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core.Processes.Steps;

/// <summary>Erzeugt den Netzanmeldungs-Antrag und reicht ihn je nach Modus ein.</summary>
public sealed class SubmitNetzanmeldungStep(
    IEnumerable<INetzbetreiberAdapter> adapters,
    SubmissionGate gate,
    IDocumentArchive archive,
    TimeProvider time) : IProcessStep
{
    public bool CanHandle(ProcessState state) =>
        state is ProcessState.NetzbetreiberResolved or ProcessState.ApplicationPrepared or ProcessState.AwaitingReview;

    public async Task<StepResult> ExecuteAsync(AnmeldeProcess process, CancellationToken ct)
    {
        var adapter = adapters.Single(a => a.Code == process.NetzbetreiberCode);

        var problems = adapter.Validate(SubmissionKind.Netzanmeldung, process.DataCard!);
        if (problems.Count > 0)
        {
            return StepResult.HumanAction(
                $"netzanmeldung-invalid:{process.Id:N}",
                "Anmeldung: fachliche Prüfung fehlgeschlagen",
                string.Join(Environment.NewLine, problems));
        }

        var package = await adapter.PrepareAsync(SubmissionKind.Netzanmeldung, process, ct);
        var outcome = await gate.SubmitAsync(package, adapter.Channel, process.ReviewApprovedAt is not null, ct);
        var now = time.GetUtcNow();

        switch (outcome)
        {
            case SubmissionOutcome.DryRun:
                await archive.StoreAsync(process.CrmProjectId, package.Documents, ct);
                EnsurePrepared(process, now);
                return StepResult.Wait("DryRun – Antrag erzeugt, nicht versendet");

            case SubmissionOutcome.AwaitingReview:
                if (process.State == ProcessState.AwaitingReview)
                    return StepResult.Wait("Freigabe ausstehend");
                await archive.StoreAsync(process.CrmProjectId, package.Documents, ct);
                EnsurePrepared(process, now);
                process.TransitionTo(ProcessState.AwaitingReview, now);
                return StepResult.HumanAction(
                    $"review:{process.Id:N}:{SubmissionKind.Netzanmeldung}",
                    "Netzanmeldung prüfen und freigeben",
                    $"Antrag an {adapter.Code} liegt am Projekt. Nach Prüfung bitte freigeben.");

            case SubmissionOutcome.Sent or SubmissionOutcome.AlreadySent:
                EnsurePrepared(process, now);
                process.TransitionTo(ProcessState.ApplicationSubmitted, now);
                return StepResult.Wait("Antrag eingereicht – warte auf Antwort des Netzbetreibers");

            case SubmissionOutcome.Uncertain:
                process.Block("Versandstatus unklar – bitte beim Netzbetreiber prüfen", now);
                return StepResult.HumanAction(
                    $"submission-uncertain:{process.Id:N}",
                    "Netzanmeldung: Versand unklar",
                    "Ein Versand wurde abgebrochen. Bitte prüfen, ob der Antrag beim Netzbetreiber angekommen ist.");

            default:
                throw new InvalidOperationException($"Unbekanntes Ergebnis {outcome}.");
        }
    }

    private static void EnsurePrepared(AnmeldeProcess process, DateTimeOffset now)
    {
        if (process.State == ProcessState.NetzbetreiberResolved)
            process.TransitionTo(ProcessState.ApplicationPrepared, now);
    }
}
