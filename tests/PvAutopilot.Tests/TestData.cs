using PvAutopilot.Core.DataCard;

namespace PvAutopilot.Tests;

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Fiktive Testdaten – keine echten Kundendaten verwenden.</summary>
internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    public static NabDataCard Card(int modulAnzahl = 20, int modulWp = 450) => new()
    {
        Anlagenbetreiber = new Person("Frau", "Erika", "Mustermann", "erika@example.org", "0203 000000", "DE00 0000 0000 0000 0000 00"),
        Anlagenadresse = new PostalAddress("Musterstraße", "1", "47051", "Duisburg"),
        Errichter = new Anlagenerrichter("Beispiel Elektro GmbH", "Duisburg", "E-123", "netze-duisburg"),
        Hauptzaehlernummer = "1ESY0000000000",
        Module = [new ModulPosition("Hersteller A", "Modul 450", modulWp, modulAnzahl)],
        Wechselrichter = [new WechselrichterPosition("Hersteller B", "WR 8K", null, 8000, 1, 8m)],
        Dokumente = [new DocumentRef(DocumentKind.EvuVollmacht, "test", "1", "vollmacht.pdf")],
    };
}
