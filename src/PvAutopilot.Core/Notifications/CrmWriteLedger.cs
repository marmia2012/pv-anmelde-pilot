namespace PvAutopilot.Core.Notifications;

/// <summary>Ein bereits ausgeführter Schreibvorgang ins CRM (Aufgabe, Notiz, Datei).</summary>
public sealed class CrmWriteRecord
{
    /// <summary>Stabiler Schlüssel des Writes, z. B. der Task-Schlüssel. Enthält keine personenbezogenen Daten.</summary>
    public required string Key { get; init; }

    /// <summary>ID des angelegten Objekts im CRM (z. B. HubSpot-Task-ID, Datei-ID).</summary>
    public required string ExternalId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Protokoll der CRM-Writes, damit ein Retry dieselbe Aufgabe, Notiz oder Datei nicht zweimal anlegt.
/// Ablauf je Write: <see cref="FindAsync"/> → nur ohne Treffer ins CRM schreiben → sofort <see cref="RecordAsync"/>.
/// </summary>
public interface ICrmWriteLedger
{
    Task<CrmWriteRecord?> FindAsync(string key, CancellationToken ct);

    /// <summary>
    /// Hält einen erfolgten Write fest. <c>false</c>, wenn der Schlüssel schon vorhanden ist
    /// (paralleler Lauf); der vorhandene Eintrag bleibt dann unverändert.
    /// </summary>
    Task<bool> RecordAsync(string key, string externalId, CancellationToken ct);
}
