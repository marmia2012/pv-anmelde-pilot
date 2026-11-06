using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PvAutopilot.Infrastructure.Persistence;

namespace PvAutopilot.Tests.Infrastructure;

/// <summary><see cref="EfCrmWriteLedger"/> gegen In-Memory-SQLite; nur die Tabelle <c>crm_writes</c> wird angelegt.</summary>
public sealed class EfCrmWriteLedgerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FixedTime _time = new(TestData.Now);

    public EfCrmWriteLedgerTests()
    {
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText = """CREATE TABLE crm_writes ("Key" TEXT NOT NULL PRIMARY KEY, "ExternalId" TEXT NOT NULL, "CreatedAt" TEXT NOT NULL);""";
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();

    private AutopilotDbContext Db() => new(new DbContextOptionsBuilder<AutopilotDbContext>().UseSqlite(_connection).Options);

    private EfCrmWriteLedger Ledger(AutopilotDbContext db) => new(db, _time);

    [Fact]
    public async Task Unknown_key_is_not_found()
    {
        using var db = Db();

        Assert.Null(await Ledger(db).FindAsync("task:unknown", default));
    }

    [Fact]
    public async Task Recorded_write_can_be_found_by_a_new_context()
    {
        using (var db = Db())
            Assert.True(await Ledger(db).RecordAsync("task:abc", "T-1", default));

        using var other = Db();
        var found = await Ledger(other).FindAsync("task:abc", default);

        Assert.NotNull(found);
        Assert.Equal(("task:abc", "T-1"), (found.Key, found.ExternalId));
        Assert.Equal(TestData.Now, found.CreatedAt);
    }

    [Fact]
    public async Task Recording_the_same_key_twice_does_not_throw_and_keeps_the_first_entry()
    {
        using var db = Db();
        var ledger = Ledger(db);

        Assert.True(await ledger.RecordAsync("task:abc", "T-1", default));
        Assert.False(await ledger.RecordAsync("task:abc", "T-2", default));

        Assert.Equal("T-1", (await ledger.FindAsync("task:abc", default))!.ExternalId);
        Assert.Equal(1, await db.CrmWrites.CountAsync());
    }

    [Fact]
    public async Task Parallel_contexts_recording_the_same_key_create_exactly_one_row()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
        {
            using var db = Db();
            return await Ledger(db).RecordAsync("file:shared", $"F-{i}", default);
        }));

        Assert.Equal(1, results.Count(r => r));
        using var check = Db();
        Assert.Equal(1, await check.CrmWrites.CountAsync());
    }

    [Fact]
    public async Task Different_keys_are_independent()
    {
        using var db = Db();
        var ledger = Ledger(db);

        Assert.True(await ledger.RecordAsync("task:a", "T-1", default));
        Assert.True(await ledger.RecordAsync("task:b", "T-2", default));

        Assert.Equal("T-2", (await ledger.FindAsync("task:b", default))!.ExternalId);
    }

    [Fact]
    public async Task Recording_does_not_save_other_pending_changes_of_the_context()
    {
        using var db = Db();
        var ledger = Ledger(db);
        db.CrmWrites.Add(new PvAutopilot.Core.Notifications.CrmWriteRecord { Key = "pending", ExternalId = "x", CreatedAt = TestData.Now });

        await ledger.RecordAsync("task:abc", "T-1", default);

        using var other = Db();
        Assert.Null(await Ledger(other).FindAsync("pending", default));
    }
}
