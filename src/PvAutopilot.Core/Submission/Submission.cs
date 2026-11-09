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

    /// <summary>
    /// Nur für Demos auf Staging: nach einem DryRun weiterlaufen, als wäre versendet worden,
    /// damit sich die Folgeschritte simulieren lassen. In Produktion immer <c>false</c>.
    /// </summary>
    public bool ContinueAfterDryRun { get; set; }
}

public enum SubmissionKind { Netzanmeldung, Inbetriebnahme, Zaehlerwechsel, Mastr }

/// <summary>Identität einer Einreichung. Ein Vorgang reicht jede Art je Revision genau einmal ein.</summary>
public sealed record SubmissionKey(Guid ProcessId, SubmissionKind Kind, int Revision = 0)
{
    /// <summary>Revision 0 ohne Suffix, damit bestehende Ledger-Einträge gültig bleiben.</summary>
    public string Value => Revision == 0 ? $"{ProcessId:N}:{Kind}" : $"{ProcessId:N}:{Kind}:r{Revision}";

    public override string ToString() => Value;
}

public sealed record ApplicationPackage(
    Guid ProcessId,
    SubmissionKind Kind,
    string NetzbetreiberCode,
    IReadOnlyList<GeneratedDocument> Documents,
    IReadOnlyList<DocumentRef> Attachments,
    int Revision = 0)
{
    public SubmissionKey Key => new(ProcessId, Kind, Revision);

    public string IdempotencyKey => Key.Value;
}

/// <param name="ArchiveKey">
/// Optional: stabiler Ablage-Schlüssel ohne personenbezogene Daten. Gesetzt legt das Archiv je Schlüssel genau eine Datei
/// ab, auch wenn sich der Inhalt ändert (z. B. Datenkarte je Revision); leer = Name + Inhalts-Hash.
/// </param>
public sealed record GeneratedDocument(string FileName, string ContentType, byte[] Content, string? ArchiveKey = null);

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
