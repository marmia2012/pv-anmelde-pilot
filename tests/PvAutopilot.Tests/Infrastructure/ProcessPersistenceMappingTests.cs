using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;
using PvAutopilot.Infrastructure.Jobs;
using PvAutopilot.Infrastructure.Persistence;

namespace PvAutopilot.Tests.Infrastructure;

/// <summary>Prüft das EF-Mapping ohne Datenbank (Modell-Metadaten und Wertkonverter).</summary>
public class ProcessPersistenceMappingTests
{
    private static IEntityType ProcessEntity(AutopilotDbContext db) => db.Model.FindEntityType(typeof(AnmeldeProcess))!;

    [Fact]
    public void All_process_states_fit_the_state_column()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var maxLength = ProcessEntity(db).FindProperty(nameof(AnmeldeProcess.State))!.GetMaxLength();

        Assert.All(Enum.GetNames<ProcessState>(), name => Assert.True(name.Length <= maxLength, name));
    }

    [Theory]
    [InlineData(nameof(AnmeldeProcess.State))]
    [InlineData(nameof(AnmeldeProcess.BlockedFrom))]
    [InlineData(nameof(AnmeldeProcess.ApprovedSubmission))]
    [InlineData(nameof(AnmeldeProcess.RueckfrageErledigung))]
    public void Enums_are_stored_as_text(string property)
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);

        Assert.Equal(typeof(string), ProcessEntity(db).FindProperty(property)!.GetTypeMapping().Converter!.ProviderClrType);
    }

    [Fact]
    public void Enum_values_round_trip_through_their_converters()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = ProcessEntity(db);

        AssertRoundTrip(entity, nameof(AnmeldeProcess.ApprovedSubmission), SubmissionKind.Zaehlerwechsel, "Zaehlerwechsel");
        AssertRoundTrip(entity, nameof(AnmeldeProcess.RueckfrageErledigung), RueckfrageResolution.Resubmit, "Resubmit");
        AssertRoundTrip(entity, nameof(AnmeldeProcess.State), ProcessState.InbetriebnahmeAwaitingReview, "InbetriebnahmeAwaitingReview");
    }

    [Fact]
    public void Facts_are_mapped_with_expected_columns_and_state_is_indexed()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = ProcessEntity(db);

        Assert.Equal("date", entity.FindProperty(nameof(AnmeldeProcess.EinspeisezusageDatum))!.GetColumnType());
        Assert.Equal("date", entity.FindProperty(nameof(AnmeldeProcess.Inbetriebnahmedatum))!.GetColumnType());
        Assert.Equal("date", entity.FindProperty(nameof(AnmeldeProcess.ZaehlerwechselDatum))!.GetColumnType());
        Assert.Equal(200, entity.FindProperty(nameof(AnmeldeProcess.NetzbetreiberReferenz))!.GetMaxLength());
        Assert.Equal(32, entity.FindProperty(nameof(AnmeldeProcess.MastrNummerAnlage))!.GetMaxLength());
        Assert.False(entity.FindProperty(nameof(AnmeldeProcess.Revision))!.IsNullable);
        Assert.NotNull(entity.FindProperty(nameof(AnmeldeProcess.StateChangedAt)));
        Assert.NotNull(entity.FindProperty(nameof(AnmeldeProcess.CancellationRequestedAt)));
        Assert.True(entity.FindProperty(nameof(AnmeldeProcess.RueckfrageGeschlossenAm))!.IsNullable);
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Single().Name == nameof(AnmeldeProcess.State));
    }

    [Fact]
    public void Model_has_no_pending_changes_against_the_last_migration()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);

        Assert.False(db.Database.HasPendingModelChanges(), "Modell geändert: neue Migration anlegen.");
    }

    [Fact]
    public void Down_of_process_flow_guards_new_states_and_reverts_renames()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var script = db.GetService<IMigrator>().GenerateScript("20261104100246_ProcessFlow", "20261103135101_Initial");

        var guard = script.IndexOf("RAISE EXCEPTION", StringComparison.Ordinal);
        var revert = script.IndexOf("""UPDATE processes SET "State" = 'ApplicationPrepared' WHERE "State" = 'NetzanmeldungPrepared';""", StringComparison.Ordinal);
        var drop = script.IndexOf("""DROP COLUMN "StateChangedAt" """.TrimEnd(), StringComparison.Ordinal);
        Assert.True(guard >= 0 && revert > guard && drop > revert, script);
        Assert.Contains("'MastrAwaitingReview'", script);
        Assert.Contains("""UPDATE process_transitions SET "To" = 'AwaitingReview' WHERE "To" = 'NetzanmeldungAwaitingReview';""", script);
    }

    [Fact]
    public void Run_lock_key_is_stable_per_process()
    {
        var id = Guid.Parse("019a4f3c-1d2e-7abc-8def-0123456789ab");

        Assert.Equal(PostgresProcessRunLock.LockKey(id), PostgresProcessRunLock.LockKey(Guid.Parse(id.ToString())));
        Assert.NotEqual(PostgresProcessRunLock.LockKey(id), PostgresProcessRunLock.LockKey(Guid.CreateVersion7()));
    }

    private static void AssertRoundTrip<T>(IEntityType entity, string property, T value, string stored)
    {
        var converter = entity.FindProperty(property)!.GetTypeMapping().Converter!;
        Assert.Equal(stored, converter.ConvertToProvider(value));
        Assert.Equal(value, converter.ConvertFromProvider(stored));
    }
}
