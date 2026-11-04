using Microsoft.EntityFrameworkCore;
using PvAutopilot.Infrastructure.Persistence;

namespace PvAutopilot.Tests.Infrastructure;

/// <summary>Nur die Prüfungen vor dem Datenbankzugriff; der Import selbst ist im Systemtest gegen Postgres geprüft.</summary>
public class NetzbetreiberPlzImporterTests
{
    private static NetzbetreiberPlzImporter Importer() =>
        new(new AutopilotDbContext(new DbContextOptionsBuilder<AutopilotDbContext>()
            .UseNpgsql("Host=nicht-erreichbar.invalid").Options));

    [Theory]
    [InlineData("")]
    [InlineData("plz;ort;netzbetreiber\n")]
    [InlineData("\n\n")]
    public async Task Empty_csv_is_rejected_and_table_stays_unchanged(string csv)
    {
        var error = await Assert.ThrowsAsync<FormatException>(() => Importer().ReplaceAllAsync(new StringReader(csv), default));

        Assert.Contains("keine Datenzeilen", error.Message);
    }

    [Theory]
    [InlineData("47051,Duisburg,netze-duisburg")]
    [InlineData("4705;Duisburg;netze-duisburg")]
    [InlineData("47051;Duisburg;")]
    public async Task Invalid_line_is_rejected_with_line_number(string line)
    {
        var error = await Assert.ThrowsAsync<FormatException>(() =>
            Importer().ReplaceAllAsync(new StringReader("plz;ort;netzbetreiber\n" + line), default));

        Assert.Contains("Zeile 2", error.Message);
    }
}
