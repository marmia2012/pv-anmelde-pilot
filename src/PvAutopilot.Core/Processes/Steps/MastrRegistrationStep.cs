using Microsoft.Extensions.Options;
using PvAutopilot.Core.Mastr;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core.Processes.Steps;

/// <summary>
/// Registriert Anlage und Speicher im MaStR mit dem Kundenlogin. Versand über das <see cref="SubmissionGate"/>,
/// damit Modus, Freigabe und Doppelversand-Schutz genauso gelten wie beim Netzbetreiber.
/// Nach der Registrierung wird das Passwort gelöscht.
/// </summary>
public sealed class MastrRegistrationStep(
    IMastrRegistrar registrar,
    IMastrCredentialStore credentials,
    ISubmissionLedger ledger,
    SubmissionGate gate,
    IOptions<SubmissionOptions> options,
    TimeProvider time) : SubmissionStepBase(SubmissionStages.Mastr, options, time)
{
    public const string ChannelName = "mastr-portal";

    public override async Task<StepResult> ExecuteAsync(AnmeldeProcess process, CancellationToken ct)
    {
        var key = process.CurrentSubmissionKey(SubmissionKind.Mastr);
        // Im DryRun wird nichts versendet, der Login wird also weder gelesen noch verlangt –
        // so läuft die Demo auf Staging auch ohne Kundenzugang durch.
        var dryRun = Options.Value.Mode == SubmissionMode.DryRun;
        var login = dryRun ? null : await credentials.GetAsync(process.CrmProjectId, ct);

        // Ist schon versendet (oder unklar), entscheidet das Gate; der Login kann dann bereits gelöscht sein.
        if (login is null && !dryRun
            && (await ledger.FindAsync(key.Value, ct))?.Status is not (SubmissionStatus.Sent or SubmissionStatus.Sending))
        {
            return StepResult.HumanAction(
                $"mastr-login-missing:{process.Id:N}",
                "MaStR-Login fehlt",
                "Für die MaStR-Registrierung fehlt der Zugang des Anlagenbetreibers. Bitte im Webformular hinterlegen; "
                + "danach läuft die Registrierung automatisch weiter. Frist: 1 Monat nach Inbetriebnahme.");
        }

        var result = await gate.ExecuteAsync(
            key,
            ChannelName,
            async token =>
            {
                // Nur erreichbar, wenn wirklich versendet wird; ohne Login ist sicher nichts passiert.
                var registration = await registrar.RegisterAsync(process,
                    login ?? throw new SubmissionNotSentException("MaStR-Login fehlt."), token);
                return new SubmissionReceipt(registration.ToReference());
            },
            process.IsReviewApproved(SubmissionKind.Mastr),
            ct);

        return await ApplyAsync(
            process, result, revision: 0,
            archive: null,
            async (submitted, now, token) =>
            {
                var registration = MastrRegistration.FromReference(submitted.ExternalReference);
                process.RecordMastrRegistration(registration.AnlagenMastrNummer, registration.SpeicherMastrNummer, now);
                // Idempotent; läuft auch im Wiederholungslauf nach AlreadySent, falls der erste Lauf vorher abbrach.
                await credentials.DeleteAsync(process.CrmProjectId, token);
            },
            "Die Daten für die MaStR-Registrierung liegen am Projekt. Nach Prüfung bitte freigeben.",
            ChannelName,
            ct);
    }
}
