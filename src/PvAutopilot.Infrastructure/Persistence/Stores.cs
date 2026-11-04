using Microsoft.EntityFrameworkCore;
using Npgsql;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Netzbetreiber;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Infrastructure.Persistence;

public sealed class EfProcessStore(AutopilotDbContext db, TimeProvider time) : IProcessStore
{
    public Task<AnmeldeProcess?> GetAsync(Guid id, CancellationToken ct) =>
        db.Processes.SingleOrDefaultAsync(p => p.Id == id, ct);

    public Task<AnmeldeProcess?> FindByCrmProjectAsync(string crmProjectId, CancellationToken ct) =>
        db.Processes.SingleOrDefaultAsync(p => p.CrmProjectId == crmProjectId, ct);

    public async Task<AnmeldeProcess> GetOrCreateAsync(string crmProjectId, CancellationToken ct)
    {
        if (await FindByCrmProjectAsync(crmProjectId, ct) is { } existing)
            return existing;

        var process = new AnmeldeProcess(crmProjectId, time.GetUtcNow());
        db.Processes.Add(process);
        try
        {
            await db.SaveChangesAsync(ct);
            return process;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Paralleler Webhook war schneller.
            db.Entry(process).State = EntityState.Detached;
            return await FindByCrmProjectAsync(crmProjectId, ct)
                ?? throw new InvalidOperationException("Vorgang nach Unique-Konflikt nicht gefunden.", ex);
        }
    }

    public Task SaveAsync(AnmeldeProcess process, CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class EfSubmissionLedger(AutopilotDbContext db, TimeProvider time) : ISubmissionLedger
{
    public Task<SubmissionRecord?> FindAsync(string idempotencyKey, CancellationToken ct) =>
        db.Submissions.SingleOrDefaultAsync(s => s.IdempotencyKey == idempotencyKey, ct);

    public async Task RecordDryRunAsync(ApplicationPackage package, string channel, CancellationToken ct)
    {
        var record = await FindAsync(package.IdempotencyKey, ct);
        if (record is null)
            db.Submissions.Add(NewRecord(package, channel, SubmissionStatus.DryRun));
        else if (record.Status == SubmissionStatus.DryRun)
            record.UpdatedAt = time.GetUtcNow();
        else
            throw new InvalidOperationException($"{package.IdempotencyKey} hat Status {record.Status}, DryRun nicht möglich.");
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> TryReserveAsync(ApplicationPackage package, string channel, CancellationToken ct)
    {
        var record = await FindAsync(package.IdempotencyKey, ct);
        if (record is null)
        {
            record = NewRecord(package, channel, SubmissionStatus.Sending);
            db.Submissions.Add(record);
        }
        else if (record.Status is SubmissionStatus.DryRun or SubmissionStatus.Failed)
        {
            record.Status = SubmissionStatus.Sending;
            record.Channel = channel;
            record.UpdatedAt = time.GetUtcNow();
        }
        else
        {
            return false;
        }

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(record).State = EntityState.Detached;
            return false;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(record).State = EntityState.Detached;
            return false;
        }
    }

    public Task MarkSentAsync(string idempotencyKey, string? externalReference, CancellationToken ct) =>
        SetStatusAsync(idempotencyKey, SubmissionStatus.Sent, externalReference, ct);

    public Task MarkFailedAsync(string idempotencyKey, CancellationToken ct) =>
        SetStatusAsync(idempotencyKey, SubmissionStatus.Failed, null, ct);

    private async Task SetStatusAsync(string key, SubmissionStatus status, string? externalReference, CancellationToken ct)
    {
        var record = await FindAsync(key, ct) ?? throw new InvalidOperationException($"{key} nicht im Ledger.");
        record.Status = status;
        record.ExternalReference = externalReference ?? record.ExternalReference;
        record.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private SubmissionRecord NewRecord(ApplicationPackage package, string channel, SubmissionStatus status)
    {
        var now = time.GetUtcNow();
        return new SubmissionRecord
        {
            IdempotencyKey = package.IdempotencyKey,
            ProcessId = package.ProcessId,
            Kind = package.Kind,
            Channel = channel,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}

/// <summary>Netzbetreiber-Ermittlung über die PLZ-Tabelle; bei mehrdeutiger PLZ entscheidet der Ort.</summary>
public sealed class PlzNetzbetreiberResolver(AutopilotDbContext db) : INetzbetreiberResolver
{
    public async Task<string?> ResolveAsync(PostalAddress address, CancellationToken ct)
    {
        var entries = await db.NetzbetreiberPlz.AsNoTracking().Where(e => e.Plz == address.Plz).ToListAsync(ct);
        var codes = entries.Select(e => e.NetzbetreiberCode).Distinct().ToList();
        if (codes.Count <= 1)
            return codes.SingleOrDefault();

        var byOrt = entries
            .Where(e => string.Equals(e.Ort, address.Ort, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.NetzbetreiberCode).Distinct().ToList();
        return byOrt.Count == 1 ? byOrt[0] : null;
    }
}

/// <summary>Ersetzt die PLZ-Tabelle aus einer CSV-Datei (<c>plz;ort;netzbetreiber</c>, Ort optional).</summary>
public sealed class NetzbetreiberPlzImporter(AutopilotDbContext db)
{
    public async Task<int> ReplaceAllAsync(TextReader csv, CancellationToken ct)
    {
        var entries = new List<NetzbetreiberPlzEntry>();
        var lineNumber = 0;
        while (await csv.ReadLineAsync(ct) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("plz", StringComparison.OrdinalIgnoreCase))
                continue;

            // Nur Semikolon: Ortsnamen dürfen Kommas enthalten.
            var parts = line.Split(';').Select(p => p.Trim()).ToArray();
            if (parts.Length != 3 || parts[0].Length != 5 || !parts[0].All(char.IsAsciiDigit) || parts[2].Length == 0)
                throw new FormatException($"Zeile {lineNumber}: erwartet 'plz;ort;netzbetreiber'.");

            entries.Add(new NetzbetreiberPlzEntry
            {
                Plz = parts[0],
                Ort = parts[1].Length == 0 ? null : parts[1],
                NetzbetreiberCode = parts[2],
            });
        }

        // Ein leerer Upload würde die Netzbetreiber-Ermittlung für alle Vorgänge lahmlegen.
        if (entries.Count == 0)
            throw new FormatException("Die CSV enthält keine Datenzeilen; die PLZ-Tabelle bleibt unverändert.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.NetzbetreiberPlz.ExecuteDeleteAsync(ct);
        db.NetzbetreiberPlz.AddRange(entries);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return entries.Count;
    }
}
