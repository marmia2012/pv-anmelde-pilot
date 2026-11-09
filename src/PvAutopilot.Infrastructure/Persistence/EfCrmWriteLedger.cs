using Microsoft.EntityFrameworkCore;
using PvAutopilot.Core.Notifications;

namespace PvAutopilot.Infrastructure.Persistence;

/// <summary>
/// <see cref="ICrmWriteLedger"/> auf der Tabelle <c>crm_writes</c>.
/// </summary>
/// <remarks>
/// Anders als <see cref="EfSubmissionLedger"/> schreibt <see cref="RecordAsync"/> direkt per SQL statt über
/// <c>SaveChanges</c>: Writes passieren mitten in einem Schritt, und <c>SaveChanges</c> würde dabei die halb fertigen
/// Änderungen am Vorgang im selben DbContext mitspeichern. <c>ON CONFLICT DO NOTHING</c> macht das Eintragen
/// atomar, auch bei parallelen Läufen (Postgres und SQLite).
/// </remarks>
public sealed class EfCrmWriteLedger(AutopilotDbContext db, TimeProvider time) : ICrmWriteLedger
{
    public const string Table = "crm_writes";

    public Task<CrmWriteRecord?> FindAsync(string key, CancellationToken ct) =>
        db.CrmWrites.AsNoTracking().SingleOrDefaultAsync(w => w.Key == key, ct);

    public async Task<bool> RecordAsync(string key, string externalId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO crm_writes ("Key", "ExternalId", "CreatedAt") VALUES ({key}, {externalId}, {now}) ON CONFLICT ("Key") DO NOTHING""",
            ct);
        return inserted == 1;
    }
}
