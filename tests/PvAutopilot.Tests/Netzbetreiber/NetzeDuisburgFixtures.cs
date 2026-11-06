using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Processes;
using PvAutopilot.Netzbetreiber.NetzeDuisburg;
using PvAutopilot.Netzbetreiber.NetzeDuisburg.Channels;
using PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms;

namespace PvAutopilot.Tests.Netzbetreiber;

/// <summary>Synthetische Testdaten für die Netze-Duisburg-Unterlagen (erfundene Personen, mit Umlauten).</summary>
internal static class NetzeDuisburgFixtures
{
    public const string Vorname = "Jürgen";
    public const string Nachname = "Müller-Lüdenscheidt";
    public const string Strasse = "Äußere Gärtnerstraße";
    public const string Hausnummer = "12b";
    public const string Email = "juergen.mueller@example.org";
    public const string Telefon = "0203 123456";
    public const string Zaehlernummer = "1EMH0012345678";
    public const string Hauptzaehlernummer = "1ESY9988776655";

    public static readonly DateOnly Heute = new(2026, 11, 6);

    public static Zaehler Zaehler(
        ZaehlerwechselTyp typ = ZaehlerwechselTyp.TauschZweirichtungszaehler,
        Zaehlerbauform? bauform = Zaehlerbauform.Ehz,
        string? nummer = Zaehlernummer) =>
        new(nummer, typ, "Keller", "Wohnung links", "Zählerschrank", bauform);

    public static NabDataCard Card(
        IReadOnlyList<DocumentKind>? dokumente = null,
        Batteriespeicher? speicher = null,
        IReadOnlyList<Zaehler>? zaehler = null) => new()
    {
        Anlagenbetreiber = new Person("Herr", Vorname, Nachname, Email, Telefon, "DE00 0000 0000 0000 0000 00"),
        Anlagenadresse = new PostalAddress(Strasse, Hausnummer, "47228", "Duisburg"),
        Errichter = new Anlagenerrichter("Sonnenschein Elektro GmbH", "Mülheim", "E-4711", "netze-duisburg"),
        Hauptzaehlernummer = Hauptzaehlernummer,
        Installationstermin = new DateOnly(2027, 3, 15),
        Module = [new ModulPosition("Hersteller A", "Modul 450", 450, 20)],
        Wechselrichter = [new WechselrichterPosition("Hersteller B", "WR 8K", null, 8000, 1, 8m, 3)],
        Speicher = speicher,
        Zaehler = zaehler ?? [Zaehler()],
        Dokumente = (dokumente ?? [DocumentKind.EvuVollmacht, DocumentKind.Schaltplan])
            .Select((k, i) => new DocumentRef(k, "test", $"ref-{i}", $"{k}.pdf")).ToList(),
    };

    public static readonly Batteriespeicher Speicher = new("Speicher GmbH", "Akku 10", 10m, Speicherkopplung.Dc);

    public static NetzeDuisburgFormGenerator Generator(ILogger<NetzeDuisburgFormGenerator>? logger = null) =>
        new(logger ?? NullLogger<NetzeDuisburgFormGenerator>.Instance);

    public static NetzeDuisburgAdapter Adapter(
        NetzeDuisburgOptions? options = null,
        ILogger<NetzeDuisburgFormGenerator>? formLogger = null,
        ILogger<NetzeDuisburgAdapter>? adapterLogger = null) =>
        new(Options.Create(options ?? new NetzeDuisburgOptions()), new NetzeDuisburgMailChannel(), new NetzeDuisburgPortalChannel(),
            Generator(formLogger), new FixedTime(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero)),
            adapterLogger ?? NullLogger<NetzeDuisburgAdapter>.Instance);

    public static AnmeldeProcess Process(NabDataCard? card, DateOnly? inbetriebnahme = null)
    {
        var process = new AnmeldeProcess("crm-1", TestData.Now);
        if (card is not null)
            process.AttachDataCard(card, TestData.Now);
        if (inbetriebnahme is { } datum)
            process.RecordInbetriebnahme(datum, TestData.Now);
        return process;
    }
}

/// <summary>Liest die Feldwerte eines erzeugten PDFs wieder ein (PDFsharp), unabhängig vom Produktivcode.</summary>
internal sealed class PdfReadback
{
    private readonly Dictionary<string, PdfDictionary> _fields = new(StringComparer.Ordinal);

    private PdfReadback(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        Document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        var acro = Document.Internals.Catalog.Elements.GetDictionary("/AcroForm")!;
        NeedAppearances = acro.Elements.GetBoolean("/NeedAppearances");
        foreach (var item in acro.Elements.GetArray("/Fields")!.Elements)
            Collect(Resolve(item)!, "");
    }

    public PdfDocument Document { get; }
    public bool NeedAppearances { get; }
    public IReadOnlyCollection<string> Names => _fields.Keys;

    public static PdfReadback Of(byte[] pdf) => new(pdf);

    /// <summary>Wert eines Textfelds; <c>null</c> = leer.</summary>
    public string? Text(string name)
    {
        var v = Value(name);
        return v is PdfString s && !string.IsNullOrEmpty(s.Value) ? s.Value : null;
    }

    public bool IsChecked(string name) => Value(name) is PdfName n && n.Value != "/Off";

    /// <summary>Gewählte Radio-Option (Export-Wert ohne Schrägstrich), <c>null</c> = keine.</summary>
    public string? Radio(string name) => Value(name) is PdfName n && n.Value != "/Off" ? n.Value.TrimStart('/') : null;

    /// <summary>Alle Felder, die read-only markiert sind (Ff Bit 1).</summary>
    public IEnumerable<string> ReadOnlyFields() =>
        _fields.Where(f => Flags(f.Value) is var ff && (ff & 1) != 0).Select(f => f.Key);

    private static int Flags(PdfDictionary field)
    {
        for (var current = field; current is not null; current = Resolve(current.Elements["/Parent"]))
        {
            if (current.Elements["/Ff"] is { } ff)
                return (ff is PdfReference r ? r.Value : ff) is PdfInteger i ? i.Value : 0;
        }
        return 0;
    }

    private PdfItem? Value(string name)
    {
        if (!_fields.TryGetValue(name, out var field))
            throw new KeyNotFoundException($"Feld nicht im PDF: {name}");
        var v = field.Elements["/V"];
        return v is PdfReference r ? r.Value : v;
    }

    private void Collect(PdfDictionary node, string prefix)
    {
        var partial = node.Elements.GetString("/T");
        var name = string.IsNullOrEmpty(partial) ? prefix : prefix.Length == 0 ? partial : $"{prefix}.{partial}";
        var kids = node.Elements.GetArray("/Kids")?.Elements.Select(Resolve).OfType<PdfDictionary>()
            .Where(k => k.Elements.ContainsKey("/T")).ToList() ?? [];
        if (kids.Count > 0)
        {
            foreach (var kid in kids)
                Collect(kid, name);
            return;
        }
        if (name.Length > 0)
            _fields[name] = node;
    }

    private static PdfDictionary? Resolve(PdfItem? item) => item switch
    {
        PdfReference r => r.Value as PdfDictionary,
        PdfDictionary d => d,
        _ => null,
    };
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var text = formatter(state, exception);
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            text += " | " + string.Join(", ", pairs.Select(p => $"{p.Key}={p.Value}"));
        Entries.Add((logLevel, text));
    }
}
