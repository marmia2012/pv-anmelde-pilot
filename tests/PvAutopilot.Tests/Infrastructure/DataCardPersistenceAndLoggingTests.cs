using Microsoft.EntityFrameworkCore;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Processes;
using PvAutopilot.Infrastructure.Logging;
using PvAutopilot.Infrastructure.Persistence;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace PvAutopilot.Tests.Infrastructure;

public class DataCardPersistenceAndLoggingTests
{
    private static NabDataCard FullCard() => TestData.Card() with
    {
        Installationsart = Installationsart.Erweiterung,
        Zaehler =
        [
            new Zaehler("SECRET-ZNR-0815", ZaehlerwechselTyp.TauschZweirichtungszaehler, "Wohnung", "Wohnung Familie Geheimname", "Keller", Zaehlerbauform.Ehz),
            new Zaehler(null, ZaehlerwechselTyp.Neusetzung, "Wallbox", null, "Garage", null),
        ],
        Wechselrichter = [new WechselrichterPosition("Hersteller B", "WR 8K", null, 8000.5m, 1, null, 3)],
        Speicher = new Batteriespeicher("Hersteller C", "Speicher 2.56", 10.24m, Speicherkopplung.Dc),
        Wallbox = new Wallbox("Hersteller D", "Wallbox 11", 11m),
        Messkonzept = "Überschusseinspeisung – Zweirichtungszähler",
        MesskonzeptBemerkung = "SECRET-Bemerkung",
    };

    [Fact]
    public void New_personal_fields_are_masked_in_logs()
    {
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration()
            .Destructure.With<PersonalDataDestructuringPolicy>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("Karte {@Card}", FullCard());

        var rendered = sink.Events.Single().Properties["Card"].ToString();
        Assert.DoesNotContain("SECRET", rendered);
        Assert.DoesNotContain("Geheimname", rendered);
        Assert.Contains("TauschZweirichtungszaehler", rendered);
        Assert.Contains("Keller", rendered);
    }

    [Fact]
    public void Data_card_survives_the_database_json_round_trip()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var converter = db.Model.FindEntityType(typeof(AnmeldeProcess))!.FindProperty(nameof(AnmeldeProcess.DataCard))!.GetValueConverter()!;
        var card = FullCard();

        var json = (string)converter.ConvertToProvider(card)!;
        var restored = (NabDataCard)converter.ConvertFromProvider(json)!;

        Assert.Equal(card.Zaehler, restored.Zaehler);
        Assert.Equal(card.Module, restored.Module);
        Assert.Equal(card.Wechselrichter, restored.Wechselrichter);
        Assert.Equal(card.Speicher, restored.Speicher);
        Assert.Equal(card.Wallbox, restored.Wallbox);
        Assert.Equal(card.Dokumente, restored.Dokumente);
        Assert.Equal(card.Installationsart, restored.Installationsart);
        Assert.True(restored.BestandsanlageVorhanden);
        Assert.Equal(card.MesskonzeptBemerkung, restored.MesskonzeptBemerkung);
        Assert.Equal(card.GesamtleistungKwp, restored.GesamtleistungKwp);
        Assert.Contains("\"TauschZweirichtungszaehler\"", json);
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
