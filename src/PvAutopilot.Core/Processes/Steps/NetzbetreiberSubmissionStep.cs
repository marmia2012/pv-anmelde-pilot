using Microsoft.Extensions.Options;
using PvAutopilot.Core.Netzbetreiber;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core.Processes.Steps;

/// <summary>
/// Eine Einreichungsstufe beim Netzbetreiber (Netzanmeldung, E.8, Zählerwechsel): Unterlagen erzeugen,
/// je nach Modus ablegen, auf Freigabe warten oder über das <see cref="SubmissionGate"/> einreichen.
/// Eine Instanz je Stufe, siehe <see cref="DependencyInjection"/>.
/// </summary>
public sealed class NetzbetreiberSubmissionStep(
    SubmissionStage stage,
    IEnumerable<INetzbetreiberAdapter> adapters,
    SubmissionGate gate,
    IDocumentArchive archive,
    IOptions<SubmissionOptions> options,
    TimeProvider time) : SubmissionStepBase(stage, options, time)
{
    public override async Task<StepResult> ExecuteAsync(AnmeldeProcess process, CancellationToken ct)
    {
        var adapter = adapters.Single(a => a.Code == process.NetzbetreiberCode);

        if (!adapter.Einreichungen.Contains(Stage.Kind))
        {
            process.TransitionTo(Stage.Submitted, Time.GetUtcNow(), $"{Label}: beim Netzbetreiber nicht separat nötig");
            return StepResult.Continue;
        }

        var problems = adapter.Validate(Stage.Kind, process.DataCard!);
        if (problems.Count > 0)
        {
            return StepResult.HumanAction(
                $"{Stage.Kind.ToString().ToLowerInvariant()}-invalid:{process.Id:N}:r{process.Revision}",
                $"{Label}: fachliche Prüfung fehlgeschlagen",
                string.Join(Environment.NewLine, problems));
        }

        var revision = process.CurrentSubmissionKey(Stage.Kind).Revision;
        var package = await adapter.PrepareAsync(Stage.Kind, process, ct) with { Revision = revision };
        var result = await gate.SubmitAsync(package, adapter.Channel, process.IsReviewApproved(Stage.Kind), ct);

        return await ApplyAsync(
            process, result, revision,
            token => archive.StoreAsync(process.CrmProjectId, package.Documents, token),
            (submitted, now, _) =>
            {
                if (Stage.Kind == SubmissionKind.Netzanmeldung && submitted.ExternalReference is { } reference)
                    process.RecordNetzbetreiberReferenz(reference, now);
                return Task.CompletedTask;
            },
            $"Unterlagen für {adapter.Code} liegen am Projekt. Nach Prüfung bitte freigeben.",
            adapter.Channel.Name,
            ct);
    }
}
