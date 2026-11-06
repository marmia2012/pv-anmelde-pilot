using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PvAutopilot.Core.Submission;
using PvAutopilot.Infrastructure.Persistence;
using PvAutopilot.Tests.Core;

namespace PvAutopilot.Tests.Infrastructure;

/// <summary>
/// <see cref="EfSubmissionLedger"/> gegen In-Memory-SQLite (kein Postgres nötig). Nur die Tabelle <c>submissions</c>
/// wird angelegt; die Spalte <c>Version</c> erzeugt SQLite nicht selbst, Konflikte simuliert ein Interceptor.
/// </summary>
public sealed class EfSubmissionLedgerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ConflictOnce _conflict = new();
    private readonly SubmissionKey _key = new(Guid.Parse("019a4f3c-1d2e-7abc-8def-0123456789ab"), SubmissionKind.Netzanmeldung);

    public EfSubmissionLedgerTests()
    {
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE submissions (
                "IdempotencyKey" TEXT NOT NULL PRIMARY KEY, "ProcessId" TEXT NOT NULL, "Kind" TEXT NOT NULL,
                "Channel" TEXT NOT NULL, "Status" TEXT NOT NULL, "ExternalReference" TEXT NULL,
                "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NOT NULL, "Version" INTEGER NOT NULL DEFAULT 0);
            """;
        command.ExecuteNonQuery();
    }

    private AutopilotDbContext Db() => new(new DbContextOptionsBuilder<AutopilotDbContext>()
        .UseSqlite(_connection).AddInterceptors(_conflict).Options);

    private static EfSubmissionLedger Ledger(AutopilotDbContext db) => new(db, new FixedTime(TestData.Now));

    private SubmissionRecord Stored()
    {
        using var db = Db();
        return db.Submissions.AsNoTracking().Single(s => s.IdempotencyKey == _key.Value);
    }

    [Fact]
    public async Task Dry_run_on_failed_entry_does_nothing()
    {
        using (var db = Db())
        {
            Assert.True(await Ledger(db).TryReserveAsync(_key, "mail", default));
            await Ledger(db).MarkFailedAsync(_key.Value, default);
        }

        using (var db = Db())
            await Ledger(db).RecordDryRunAsync(_key, "mail", default);

        Assert.Equal(SubmissionStatus.Failed, Stored().Status);
    }

    [Fact]
    public async Task Dry_run_on_sent_entry_still_throws()
    {
        using var db = Db();
        await Ledger(db).TryReserveAsync(_key, "mail", default);
        await Ledger(db).MarkSentAsync(_key.Value, "NB-1", default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Ledger(db).RecordDryRunAsync(_key, "mail", default));
    }

    [Fact]
    public async Task Mark_sent_wins_over_a_concurrent_change()
    {
        using var db = Db();
        var ledger = Ledger(db);
        await ledger.TryReserveAsync(_key, "mail", default);
        // Paralleler Schreiber (z. B. manuelles Klären) hat den Eintrag inzwischen auf Failed gesetzt.
        await using (var command = _connection.CreateCommand())
        {
            command.CommandText = """UPDATE submissions SET "Status" = 'Failed', "Version" = "Version" + 1""";
            await command.ExecuteNonQueryAsync();
        }
        _conflict.Armed = true;

        await ledger.MarkSentAsync(_key.Value, "NB-4711", default);

        var stored = Stored();
        Assert.Equal((SubmissionStatus.Sent, "NB-4711"), (stored.Status, stored.ExternalReference));
        Assert.Equal(SubmissionStatus.Sent, (await ledger.FindAsync(_key.Value, default))!.Status);
        Assert.False(_conflict.Armed);
    }

    [Fact]
    public async Task Mark_sent_keeps_existing_reference_when_none_is_given()
    {
        using var db = Db();
        var ledger = Ledger(db);
        await ledger.TryReserveAsync(_key, "mail", default);
        await ledger.ResolveSendingAsync(_key.Value, SubmissionStatus.Sent, "NB-1", default);
        _conflict.Armed = true;

        await ledger.MarkSentAsync(_key.Value, null, default);

        Assert.Equal("NB-1", Stored().ExternalReference);
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>Lässt das nächste SaveChanges mit einem Konkurrenzkonflikt scheitern, wie Postgres über <c>xmin</c>.</summary>
    private sealed class ConflictOnce : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!Armed)
                return ValueTask.FromResult(result);
            Armed = false;
            throw new DbUpdateConcurrencyException("Simulierter Konflikt");
        }
    }
}
