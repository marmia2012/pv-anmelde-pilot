using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PvAutopilot.Core.Submission;

public enum SubmissionOutcome
{
    DryRun,
    AwaitingReview,
    Sent,
    AlreadySent,

    /// <summary>Ein früherer Versand ist abgebrochen – ob er angekommen ist, muss ein Mensch prüfen.</summary>
    Uncertain,
}

/// <param name="ExternalReference">Referenz des Empfängers (z. B. Vorgangsnummer, MaStR-Nummern), auch bei <see cref="SubmissionOutcome.AlreadySent"/>.</param>
public sealed record SubmissionResult(SubmissionOutcome Outcome, string? ExternalReference = null);

/// <summary>
/// Einziger Weg nach draußen: wendet den <see cref="SubmissionMode"/> an und verhindert Doppelversand.
/// Gilt für alle externen Einreichungen – Netzbetreiber und MaStR.
/// </summary>
public sealed class SubmissionGate(
    ISubmissionLedger ledger,
    IOptions<SubmissionOptions> options,
    ILogger<SubmissionGate> logger)
{
    public Task<SubmissionResult> SubmitAsync(
        ApplicationPackage package,
        ISubmissionChannel channel,
        bool reviewApproved,
        CancellationToken ct) =>
        ExecuteAsync(package.Key, channel.Name, token => channel.SendAsync(package, token), reviewApproved, ct);

    /// <param name="send">
    /// Der eigentliche Versand. Wirf <see cref="SubmissionNotSentException"/> nur, wenn sicher ist,
    /// dass nichts angekommen ist – jeder andere Fehler gilt als unklarer Versand.
    /// </param>
    public async Task<SubmissionResult> ExecuteAsync(
        SubmissionKey key,
        string channelName,
        Func<CancellationToken, Task<SubmissionReceipt>> send,
        bool reviewApproved,
        CancellationToken ct)
    {
        var existing = await ledger.FindAsync(key.Value, ct);
        switch (existing?.Status)
        {
            case SubmissionStatus.Sent:
                return new SubmissionResult(SubmissionOutcome.AlreadySent, existing.ExternalReference);
            case SubmissionStatus.Sending:
                return new SubmissionResult(SubmissionOutcome.Uncertain);
        }

        var mode = options.Value.Mode;
        if (mode == SubmissionMode.DryRun)
        {
            await ledger.RecordDryRunAsync(key, channelName, ct);
            logger.LogInformation("DryRun: {SubmissionKey} über {Channel} nicht versendet", key.Value, channelName);
            return new SubmissionResult(SubmissionOutcome.DryRun);
        }

        if (mode == SubmissionMode.Review && !reviewApproved)
            return new SubmissionResult(SubmissionOutcome.AwaitingReview);

        if (!await ledger.TryReserveAsync(key, channelName, ct))
            throw new SubmissionConflictException(key.Value);

        SubmissionReceipt receipt;
        try
        {
            receipt = await send(ct);
        }
        catch (SubmissionNotSentException)
        {
            await ledger.MarkFailedAsync(key.Value, CancellationToken.None);
            throw;
        }
        // Alle anderen Fehler lassen den Status auf Sending → nächster Lauf meldet Uncertain.

        await ledger.MarkSentAsync(key.Value, receipt.ExternalReference, CancellationToken.None);
        logger.LogInformation("{SubmissionKey} über {Channel} versendet", key.Value, channelName);
        return new SubmissionResult(SubmissionOutcome.Sent, receipt.ExternalReference);
    }
}
