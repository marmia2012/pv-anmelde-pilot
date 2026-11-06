using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Infrastructure.Persistence;

public sealed class AutopilotDbContext(DbContextOptions<AutopilotDbContext> options) : DbContext(options)
{
    public DbSet<AnmeldeProcess> Processes => Set<AnmeldeProcess>();
    public DbSet<SubmissionRecord> Submissions => Set<SubmissionRecord>();
    public DbSet<NetzbetreiberPlzEntry> NetzbetreiberPlz => Set<NetzbetreiberPlzEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureProcess(modelBuilder.Entity<AnmeldeProcess>());
        ConfigureSubmission(modelBuilder.Entity<SubmissionRecord>());

        modelBuilder.Entity<NetzbetreiberPlzEntry>(b =>
        {
            b.ToTable("netzbetreiber_plz");
            b.HasKey(e => e.Id);
            b.Property(e => e.Plz).HasMaxLength(5);
            b.Property(e => e.Ort).HasMaxLength(100);
            b.Property(e => e.NetzbetreiberCode).HasMaxLength(64);
            b.HasIndex(e => e.Plz);
        });
    }

    private static readonly JsonSerializerOptions DataCardJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static void ConfigureProcess(EntityTypeBuilder<AnmeldeProcess> b)
    {
        b.ToTable("processes");
        b.HasKey(p => p.Id);
        b.Property(p => p.CrmProjectId).HasMaxLength(64);
        b.HasIndex(p => p.CrmProjectId).IsUnique();
        // Längster Zustandsname hat 28 Zeichen (InbetriebnahmeAwaitingReview); 40 lässt Luft.
        b.Property(p => p.State).HasConversion<string>().HasMaxLength(40);
        // Für ListOpenAsync (Watchdog) und Auswertungen je Zustand.
        b.HasIndex(p => p.State);
        b.Property(p => p.BlockedFrom).HasConversion<string>().HasMaxLength(40);
        b.Property(p => p.NetzbetreiberCode).HasMaxLength(64);
        b.Property(p => p.ApprovedSubmission).HasConversion<string>().HasMaxLength(40);

        // Fakten aus Ereignissen
        b.Property(p => p.NetzbetreiberReferenz).HasMaxLength(200);
        b.Property(p => p.RueckfrageErledigung).HasConversion<string>().HasMaxLength(40);
        // MaStR-Nummern haben 15 Zeichen (z. B. SEE…); Puffer für Formatänderungen.
        b.Property(p => p.MastrNummerAnlage).HasMaxLength(32);
        b.Property(p => p.MastrNummerSpeicher).HasMaxLength(32);
        b.Property(p => p.DataCard)
            .HasColumnType("jsonb")
            .HasConversion(
                card => JsonSerializer.Serialize(card, DataCardJson),
                json => JsonSerializer.Deserialize<NabDataCard>(json, DataCardJson));
        b.Property<uint>("Version").IsRowVersion();

        b.OwnsMany(p => p.Transitions, t =>
        {
            t.ToTable("process_transitions");
            t.WithOwner().HasForeignKey("ProcessId");
            t.Property<long>("Id");
            t.HasKey("Id");
            t.Property(x => x.From).HasConversion<string>().HasMaxLength(40);
            t.Property(x => x.To).HasConversion<string>().HasMaxLength(40);
            t.Property(x => x.Note).HasMaxLength(500);
        });
        b.Navigation(p => p.Transitions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureSubmission(EntityTypeBuilder<SubmissionRecord> b)
    {
        b.ToTable("submissions");
        b.HasKey(s => s.IdempotencyKey);
        b.Property(s => s.IdempotencyKey).HasMaxLength(100);
        b.Property(s => s.Kind).HasConversion<string>().HasMaxLength(40);
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.Channel).HasMaxLength(64);
        b.Property(s => s.ExternalReference).HasMaxLength(200);
        b.HasIndex(s => s.ProcessId);
        b.Property<uint>("Version").IsRowVersion();
    }
}

/// <summary>Eine Zeile der PLZ-Tabelle. <see cref="Ort"/> nur, wenn eine PLZ mehrere Netzbetreiber hat.</summary>
public sealed class NetzbetreiberPlzEntry
{
    public int Id { get; set; }
    public required string Plz { get; set; }
    public string? Ort { get; set; }
    public required string NetzbetreiberCode { get; set; }
}

/// <summary>Für <c>dotnet ef migrations</c>; verbindet sich nicht mit einer Datenbank.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AutopilotDbContext>
{
    public AutopilotDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AutopilotDbContext>().UseNpgsql("Host=localhost;Database=pvautopilot").Options);
}
