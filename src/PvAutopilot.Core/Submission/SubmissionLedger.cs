namespace PvAutopilot.Core.Submission;

public enum SubmissionStatus
{
    DryRun,

    /// <summary>Versand reserviert/gestartet. Bleibt der Status stehen, ist unklar, ob versendet wurde.</summary>
    Sending,
    Sent,

    /// <summary>Versand nachweislich nicht erfolgt; darf erneut reserviert werden.</summary>
    Failed,
}

public sealed class SubmissionRecord
{
    public required string IdempotencyKey { get; init; }
    public required Guid ProcessId { get; init; }
    public required SubmissionKind Kind { get; init; }
    public required string Channel { get; set; }
    public required SubmissionStatus Status { get; set; }
    public string? ExternalReference { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Protokoll aller Einreichungen; garantiert, dass nichts doppelt versendet wird.</summary>
public interface ISubmissionLedger
{
    Task<SubmissionRecord?> FindAsync(string idempotencyKey, CancellationToken ct);

    /// <summary>Alle Einträge eines Vorgangs (alle Arten und Revisionen), z. B. für das Storno.</summary>
    Task<IReadOnlyList<SubmissionRecord>> ListByProcessAsync(Guid processId, CancellationToken ct);

    Task RecordDryRunAsync(SubmissionKey key, string channel, CancellationToken ct);

    /// <summary>Setzt atomar den Status <see cref="SubmissionStatus.Sending"/>. <c>false</c>, wenn ein anderer Lauf schneller war.</summary>
    Task<bool> TryReserveAsync(SubmissionKey key, string channel, CancellationToken ct);

    Task MarkSentAsync(string idempotencyKey, string? externalReference, CancellationToken ct);

    Task MarkFailedAsync(string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Klärt einen unklaren Versand nach manueller Prüfung: <see cref="SubmissionStatus.Sent"/> (angekommen) oder
    /// <see cref="SubmissionStatus.Failed"/> (nicht angekommen, darf erneut versendet werden).
    /// <c>false</c>, wenn der Eintrag fehlt oder nicht (mehr) auf <see cref="SubmissionStatus.Sending"/> steht.
    /// </summary>
    Task<bool> ResolveSendingAsync(string idempotencyKey, SubmissionStatus outcome, string? externalReference, CancellationToken ct);
}
