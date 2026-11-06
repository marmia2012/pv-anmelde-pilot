using Microsoft.Extensions.Options;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Netzbetreiber;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;
using PvAutopilot.Netzbetreiber.NetzeDuisburg.Channels;

namespace PvAutopilot.Netzbetreiber.NetzeDuisburg;

public enum NetzeDuisburgChannelKind { Mail, Portal }

public sealed class NetzeDuisburgOptions
{
    public const string SectionName = "Netzbetreiber:NetzeDuisburg";

    /// <summary>Antragsweg – mit Netze Duisburg noch abzustimmen.</summary>
    public NetzeDuisburgChannelKind Channel { get; set; } = NetzeDuisburgChannelKind.Mail;

    public string MailRecipient { get; set; } = string.Empty;

    public Uri PortalUrl { get; set; } =
        new("https://portal.netze-duisburg.de/sap/bc/ui5_ui5/sap/zumcui5_cg_m/index.html#/Logon");
}

/// <summary>
/// Netze Duisburg: Netzanmeldung &lt; 25 kW, Inbetriebnahme (E.8), Zählerwechsel.
/// Unterlagen werden unabhängig vom Antragsweg gleich erzeugt; nur der Versandkanal unterscheidet sich.
/// </summary>
public sealed class NetzeDuisburgAdapter(
    IOptions<NetzeDuisburgOptions> options,
    NetzeDuisburgMailChannel mailChannel,
    NetzeDuisburgPortalChannel portalChannel) : INetzbetreiberAdapter
{
    public const string NetzbetreiberCode = "netze-duisburg";

    /// <summary>Oberhalb dieser Leistung gilt ein anderes Verfahren, das noch nicht automatisiert ist.</summary>
    public const decimal MaxLeistungKw = 25m;

    public string Code => NetzbetreiberCode;

    /// <summary>Der Zähler-/Messantrag geht zusammen mit dem E.8 raus, deshalb keine eigene Zählerwechsel-Einreichung.</summary>
    public IReadOnlySet<SubmissionKind> Einreichungen { get; } =
        new HashSet<SubmissionKind> { SubmissionKind.Netzanmeldung, SubmissionKind.Inbetriebnahme };

    public ISubmissionChannel Channel => options.Value.Channel switch
    {
        NetzeDuisburgChannelKind.Portal => portalChannel,
        _ => mailChannel,
    };

    public IReadOnlyList<string> Validate(SubmissionKind kind, NabDataCard card)
    {
        var problems = new List<string>();
        if (kind != SubmissionKind.Netzanmeldung)
            return problems; // TODO: Prüfungen für E.8 und Zählerwechsel.

        // TODO: klären, ob die 25-kW-Grenze auf kWp oder WR-Leistung bezogen ist; hier vorerst die strengere Prüfung.
        var leistung = Math.Max(card.GesamtleistungKwp, card.WechselrichterLeistungKw);
        if (leistung >= MaxLeistungKw)
            problems.Add($"Anlage mit {leistung:0.##} kW: Verfahren ab 25 kW ist noch nicht automatisiert.");
        if (card.Module.Count == 0)
            problems.Add("Keine Module erfasst.");
        if (card.Wechselrichter.Count == 0)
            problems.Add("Keine Wechselrichter erfasst.");
        if (string.IsNullOrWhiteSpace(card.Hauptzaehlernummer))
            problems.Add("Hauptzählernummer fehlt (Anschlussobjekt wird darüber ermittelt).");
        if (!card.Dokumente.Any(d => d.Kind == DocumentKind.EvuVollmacht))
            problems.Add("EVU-Vollmacht fehlt.");
        return problems;
    }

    public Task<ApplicationPackage> PrepareAsync(SubmissionKind kind, AnmeldeProcess process, CancellationToken ct)
    {
        var card = process.DataCard ?? throw new InvalidOperationException("Datenkarte fehlt.");

        // TODO: Formulare E.1, E.2 (+ Messkonzept-Auswahlblatt), E.3, E.4, E.6, E.8 und Zählerantrag mit PDFsharp befüllen.
        IReadOnlyList<GeneratedDocument> documents = [];

        var attachmentKinds = kind switch
        {
            SubmissionKind.Netzanmeldung =>
            [
                DocumentKind.Lageplan, DocumentKind.Schaltplan, DocumentKind.Datenblatt,
                DocumentKind.Einheitenzertifikat, DocumentKind.NaSchutzZertifikat, DocumentKind.EvuVollmacht,
            ],
            _ => Array.Empty<DocumentKind>(),
        };
        var attachments = card.Dokumente.Where(d => attachmentKinds.Contains(d.Kind)).ToList();

        return Task.FromResult(new ApplicationPackage(process.Id, kind, Code, documents, attachments));
    }
}
