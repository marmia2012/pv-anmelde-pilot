using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PvAutopilot.Infrastructure.Persistence;

namespace PvAutopilot.Infrastructure.Jobs;

/// <summary>Verhindert parallele Läufe der State-Machine für denselben Vorgang (auch über mehrere Instanzen).</summary>
public interface IProcessRunLock
{
    /// <summary>
    /// Wartet bis zu <paramref name="wait"/> auf das Lock. <c>null</c>, wenn es in der Zeit nicht frei wurde.
    /// Freigabe per <see cref="IAsyncDisposable.DisposeAsync"/>.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(Guid processId, TimeSpan wait, CancellationToken ct);
}

/// <summary>
/// Postgres-Advisory-Lock auf Sitzungsebene über eine eigene Verbindung. Bricht der Prozess ab,
/// endet die Verbindung und Postgres gibt das Lock frei – es bleibt kein verwaistes Lock zurück.
/// </summary>
public sealed class PostgresProcessRunLock(AutopilotDbContext db, ILogger<PostgresProcessRunLock> logger) : IProcessRunLock
{
    // Erster Schlüssel trennt unsere Locks von anderen Advisory-Locks in derselben Datenbank ("PVAP").
    private const int LockNamespace = 0x50564150;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public async Task<IAsyncDisposable?> TryAcquireAsync(Guid processId, TimeSpan wait, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        try
        {
            await connection.OpenAsync(ct);
            var key = LockKey(processId);
            var deadline = DateTimeOffset.UtcNow + wait;
            while (true)
            {
                await using (var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@ns, @key)", connection))
                {
                    command.Parameters.AddWithValue("ns", LockNamespace);
                    command.Parameters.AddWithValue("key", key);
                    if ((bool)(await command.ExecuteScalarAsync(ct))!)
                        return new Held(connection, key, logger);
                }

                if (DateTimeOffset.UtcNow >= deadline)
                    break;
                await Task.Delay(PollInterval, ct);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    /// <summary>Faltet die Guid auf 32 Bit. Kollisionen serialisieren nur unnötig, sind aber harmlos.</summary>
    public static int LockKey(Guid processId)
    {
        Span<byte> bytes = stackalloc byte[16];
        processId.TryWriteBytes(bytes);
        var key = 0;
        for (var i = 0; i < 16; i += 4)
            key ^= BitConverter.ToInt32(bytes[i..(i + 4)]);
        return key;
    }

    /// <summary>
    /// Die Freigabe wirft nie: Sie läuft im <c>await using</c> des Jobs und würde sonst das Blockieren nach dem
    /// letzten Versuch überdecken. Ist die Verbindung abgerissen, hat Postgres das Lock mit der Sitzung schon freigegeben.
    /// </summary>
    internal sealed class Held(NpgsqlConnection connection, int key, ILogger logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            var unlocked = false;
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@ns, @key)", connection);
                command.Parameters.AddWithValue("ns", LockNamespace);
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteScalarAsync();
                unlocked = true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Prozess-Lock {LockKey} nicht freigegeben; Postgres gibt es mit der Sitzung frei", key);
            }

            try
            {
                // Ohne Unlock die Sitzung nicht im Pool weiterleben lassen, sonst behielte sie das Lock.
                if (!unlocked)
                    NpgsqlConnection.ClearPool(connection);
                // Die Verbindung geht zurück in den Pool; das explizite Unlock oben verhindert, dass das Lock dort weiterlebt.
                await connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Verbindung des Prozess-Locks {LockKey} nicht sauber geschlossen", key);
            }
        }
    }
}
