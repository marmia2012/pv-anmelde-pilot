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

/// <summary>
/// Einziger Weg nach draußen: wendet den <see cref="SubmissionMode"/> an und verhindert Doppelversand.
/// </summary>
public sealed class SubmissionGate(
    ISubmissionLedger ledger,
    IOptions<SubmissionOptions> options,
    ILogger<SubmissionGate> logger)
{
    public async Task<SubmissionOutcome> SubmitAsync(
        ApplicationPackage package,
        ISubmissionChannel channel,
        bool reviewApproved,
        CancellationToken ct)
    {
        var key = package.IdempotencyKey;
        var existing = await ledger.FindAsync(key, ct);
        switch (existing?.Status)
        {
            case SubmissionStatus.Sent:
                return SubmissionOutcome.AlreadySent;
            case SubmissionStatus.Sending:
                return SubmissionOutcome.Uncertain;
        }

        var mode = options.Value.Mode;
        if (mode == SubmissionMode.DryRun)
        {
            await ledger.RecordDryRunAsync(package, channel.Name, ct);
            logger.LogInformation("DryRun: {SubmissionKey} über {Channel} nicht versendet", key, channel.Name);
            return SubmissionOutcome.DryRun;
        }

        if (mode == SubmissionMode.Review && !reviewApproved)
            return SubmissionOutcome.AwaitingReview;

        if (!await ledger.TryReserveAsync(package, channel.Name, ct))
            throw new SubmissionConflictException(key);

        SubmissionReceipt receipt;
        try
        {
            receipt = await channel.SendAsync(package, ct);
        }
        catch (SubmissionNotSentException)
        {
            await ledger.MarkFailedAsync(key, CancellationToken.None);
            throw;
        }
        // Alle anderen Fehler lassen den Status auf Sending → nächster Lauf meldet Uncertain.

        await ledger.MarkSentAsync(key, receipt.ExternalReference, CancellationToken.None);
        logger.LogInformation("{SubmissionKey} über {Channel} versendet", key, channel.Name);
        return SubmissionOutcome.Sent;
    }
}
