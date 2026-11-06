using Microsoft.Extensions.Options;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core.Processes.Steps;

/// <summary>
/// Gemeinsamer Ablauf einer Einreichungsstufe (Bereit → Vorbereitet → Freigabe → Eingereicht):
/// setzt das Ergebnis des <see cref="SubmissionGate"/> in Zustandswechsel um. Gilt für Netzbetreiber und MaStR.
/// </summary>
public abstract class SubmissionStepBase(SubmissionStage stage, IOptions<SubmissionOptions> options, TimeProvider time)
    : IProcessStep
{
    public SubmissionStage Stage => stage;

    protected IOptions<SubmissionOptions> Options => options;
    protected TimeProvider Time => time;
    protected string Label => stage.Kind.Label();

    public bool CanHandle(ProcessState state) => stage.Handles(state);

    public abstract Task<StepResult> ExecuteAsync(AnmeldeProcess process, CancellationToken ct);

    /// <param name="archive">Legt die erzeugten Unterlagen am CRM-Projekt ab (nur beim ersten Erzeugen bzw. vor der Freigabe).</param>
    /// <param name="onSubmitted">Fakten aus der Einreichung festhalten; läuft bei Sent und AlreadySent, muss idempotent sein.</param>
    /// <param name="reviewBody">Text des Freigabe-Tasks (ohne personenbezogene Daten).</param>
    protected async Task<StepResult> ApplyAsync(
        AnmeldeProcess process,
        SubmissionResult result,
        int revision,
        Func<CancellationToken, Task>? archive,
        Func<SubmissionResult, DateTimeOffset, CancellationToken, Task> onSubmitted,
        string reviewBody,
        string channelName,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        switch (result.Outcome)
        {
            case SubmissionOutcome.DryRun:
                if (process.State == stage.Ready && archive is not null)
                    await archive(ct);
                EnsurePrepared(process, now);
                if (options.Value.ContinueAfterDryRun)
                {
                    process.TransitionTo(stage.Submitted, now, "DryRun – nicht versendet, Ablauf simuliert");
                    return StepResult.Continue;
                }
                return StepResult.Wait($"DryRun – {Label} erzeugt, nicht versendet");

            case SubmissionOutcome.AwaitingReview:
                if (process.State != stage.AwaitingReview)
                {
                    if (archive is not null)
                        await archive(ct);
                    EnsurePrepared(process, now);
                    process.TransitionTo(stage.AwaitingReview, now);
                }
                // Bei jedem Lauf ohne Freigabe: Der Sink legt je Schlüssel nur einen Task an. Der Eintritt in den
                // Zustand steht im Schlüssel, damit nach Block/Resume (die Freigabe verfällt dabei) ein neuer Task
                // entsteht und ein Mensch erneut freigibt.
                return StepResult.HumanAction(
                    $"review:{process.Id:N}:{stage.Kind}:r{revision}:{TaskKeys.Stamp(process.StateChangedAt)}",
                    $"{Label} prüfen und freigeben",
                    reviewBody + " " + ReviewPropertyHint);

            case SubmissionOutcome.Sent or SubmissionOutcome.AlreadySent:
                await onSubmitted(result, now, ct);
                process.TransitionTo(stage.Submitted, now, $"{Label} eingereicht über {channelName}");
                return StepResult.Continue;

            case SubmissionOutcome.Uncertain:
                process.Block($"{Label}: Versandstatus unklar – bitte manuell prüfen", now);
                return StepResult.HumanAction(
                    $"submission-uncertain:{process.Id:N}:{stage.Kind}:r{revision}",
                    $"{Label}: Versand unklar",
                    "Ein Versand wurde abgebrochen. Bitte beim Empfänger prüfen, ob die Einreichung angekommen ist, und das "
                    + $"Ergebnis melden: POST /admin/processes/{process.Id}/submissions/{stage.Kind}/resolve mit "
                    + "{\"outcome\": \"Sent\"} (angekommen, optional \"reference\") oder {\"outcome\": \"NotSent\"} "
                    + "(nicht angekommen, wird erneut versendet). Danach wird der Vorgang fortgesetzt: Bei \"Sent\" läuft er "
                    + "ohne erneuten Versand weiter. Bei \"NotSent\" wird erneut versendet – im Modus Review erst nach einer "
                    + "neuen Freigabe (es entsteht ein neuer Freigabe-Task). "
                    + "Ein erneutes Fortsetzen ohne diese Meldung blockiert sofort wieder.");

            default:
                throw new InvalidOperationException($"Unbekanntes Ergebnis {result.Outcome}.");
        }
    }

    /// <summary>
    /// Wir lesen HubSpot nur; HubSpot meldet eine Property nur bei Änderung. Eine noch gesetzte Freigabe
    /// (z. B. aus einer früheren Revision) muss deshalb erst zurückgesetzt werden.
    /// </summary>
    internal const string ReviewPropertyHint =
        "Zum Freigeben die Freigabe-Property dieser Stufe im Projekt setzen. Ist sie noch von einer früheren Freigabe "
        + "gesetzt, bitte erst zurücksetzen und dann neu setzen – nur eine Änderung löst die Freigabe aus.";

    private void EnsurePrepared(AnmeldeProcess process, DateTimeOffset now)
    {
        if (process.State == stage.Ready)
            process.TransitionTo(stage.Prepared, now);
    }
}
