using PvAutopilot.Core.DataCard;

namespace PvAutopilot.Core.Submission;

/// <summary>Go-live-Stufe. Default ist immer <see cref="DryRun"/>.</summary>
public enum SubmissionMode
{
    /// <summary>Unterlagen werden erzeugt und abgelegt, aber nichts wird versendet.</summary>
    DryRun,

    /// <summary>Vor jedem Versand ist eine Freigabe in HubSpot nötig.</summary>
    Review,

    /// <summary>Vollautomatischer Versand.</summary>
    Live,
}

public sealed class SubmissionOptions
{
    public const string SectionName = "Submission";

    public SubmissionMode Mode { get; set; } = SubmissionMode.DryRun;
}

public enum SubmissionKind { Netzanmeldung, Inbetriebnahme, Zaehlerwechsel }

public sealed record ApplicationPackage(
    Guid ProcessId,
    SubmissionKind Kind,
    string NetzbetreiberCode,
    IReadOnlyList<GeneratedDocument> Documents,
    IReadOnlyList<DocumentRef> Attachments)
{
    /// <summary>Ein Vorgang reicht jede Antragsart genau einmal ein.</summary>
    public string IdempotencyKey => $"{ProcessId:N}:{Kind}";
}

public sealed record GeneratedDocument(string FileName, string ContentType, byte[] Content);

public sealed record SubmissionReceipt(string? ExternalReference);

/// <summary>Versandweg zu einem Netzbetreiber (Portal, Mail, …).</summary>
public interface ISubmissionChannel
{
    string Name { get; }

    /// <summary>
    /// Versendet das Paket. Wirf <see cref="SubmissionNotSentException"/> nur, wenn sicher ist,
    /// dass nichts beim Netzbetreiber angekommen ist – sonst wird der Vorgang zur manuellen Prüfung blockiert.
    /// </summary>
    Task<SubmissionReceipt> SendAsync(ApplicationPackage package, CancellationToken ct);
}

/// <summary>Der Versand ist nachweislich nicht erfolgt; ein erneuter Versuch ist gefahrlos.</summary>
public sealed class SubmissionNotSentException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Ein paralleler Lauf hat den Versand gerade reserviert; später erneut versuchen.</summary>
public sealed class SubmissionConflictException(string idempotencyKey)
    : Exception($"Versand {idempotencyKey} ist bereits reserviert.");
