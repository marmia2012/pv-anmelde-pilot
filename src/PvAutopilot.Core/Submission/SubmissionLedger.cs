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

    Task RecordDryRunAsync(ApplicationPackage package, string channel, CancellationToken ct);

    /// <summary>Setzt atomar den Status <see cref="SubmissionStatus.Sending"/>. <c>false</c>, wenn ein anderer Lauf schneller war.</summary>
    Task<bool> TryReserveAsync(ApplicationPackage package, string channel, CancellationToken ct);

    Task MarkSentAsync(string idempotencyKey, string? externalReference, CancellationToken ct);

    Task MarkFailedAsync(string idempotencyKey, CancellationToken ct);
}
