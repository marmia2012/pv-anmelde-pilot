using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Netzbetreiber;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;
using PvAutopilot.Netzbetreiber.NetzeDuisburg.Channels;
using PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms;

namespace PvAutopilot.Netzbetreiber.NetzeDuisburg;

public enum NetzeDuisburgChannelKind { Mail, Portal }

public sealed class NetzeDuisburgOptions
{
    public const string SectionName = "Netzbetreiber:NetzeDuisburg";

    /// <summary>Default für <see cref="RequiredAttachments"/>.</summary>
    public static readonly IReadOnlyList<DocumentKind> DefaultRequiredAttachments =
        [DocumentKind.EvuVollmacht, DocumentKind.Schaltplan];

    /// <summary>Antragsweg – mit Netze Duisburg noch abzustimmen.</summary>
    public NetzeDuisburgChannelKind Channel { get; set; } = NetzeDuisburgChannelKind.Mail;

    public string MailRecipient { get; set; } = string.Empty;

    public Uri PortalUrl { get; set; } =
        new("https://portal.netze-duisburg.de/sap/bc/ui5_ui5/sap/zumcui5_cg_m/index.html#/Logon");

    /// <summary>
    /// Pflichtanhänge der Netzanmeldung: fehlt einer, ist das ein fachliches Problem. Nicht gesetzt =
    /// <see cref="DefaultRequiredAttachments"/>. Bewusst ohne Default-Liste in der Property, weil der
    /// Configuration-Binder konfigurierte Einträge an eine vorbelegte Liste anhängen statt sie ersetzen würde.
    /// </summary>
    public List<DocumentKind>? RequiredAttachments { get; set; }

    public IReadOnlyList<DocumentKind> EffectiveRequiredAttachments => RequiredAttachments ?? DefaultRequiredAttachments;
}

/// <summary>
/// Netze Duisburg: Netzanmeldung &lt; 25 kW, Inbetriebnahme (E.8), Zählerwechsel.
/// Unterlagen werden unabhängig vom Antragsweg gleich erzeugt; nur der Versandkanal unterscheidet sich.
/// </summary>
public sealed class NetzeDuisburgAdapter(
    IOptions<NetzeDuisburgOptions> options,
    NetzeDuisburgMailChannel mailChannel,
    NetzeDuisburgPortalChannel portalChannel,
    NetzeDuisburgFormGenerator forms,
    ILogger<NetzeDuisburgAdapter> logger) : INetzbetreiberAdapter
{
    public const string NetzbetreiberCode = "netze-duisburg";

    /// <summary>Oberhalb dieser Leistung gilt ein anderes Verfahren, das noch nicht automatisiert ist.</summary>
    public const decimal MaxLeistungKw = 25m;

    /// <summary>
    /// Dokumente der Datenkarte, die der Netzanmeldung beiliegen (E.6 = NA-Schutz-Zertifikat, E.7 = Prüfbericht).
    /// Was davon nicht Pflicht ist (<see cref="NetzeDuisburgOptions.RequiredAttachments"/>), ist optional.
    /// </summary>
    public static readonly IReadOnlyList<DocumentKind> NetzanmeldungAttachments =
    [
        DocumentKind.Lageplan, DocumentKind.Schaltplan, DocumentKind.Datenblatt, DocumentKind.Einheitenzertifikat,
        DocumentKind.NaSchutzZertifikat, DocumentKind.NaSchutzPruefbericht, DocumentKind.EvuVollmacht,
    ];

    public string Code => NetzbetreiberCode;

    /// <summary>Der Zähler-/Messantrag geht zusammen mit dem E.8 raus, deshalb keine eigene Zählerwechsel-Einreichung.</summary>
    public IReadOnlySet<SubmissionKind> Einreichungen { get; } =
        new HashSet<SubmissionKind> { SubmissionKind.Netzanmeldung, SubmissionKind.Inbetriebnahme };

    public ISubmissionChannel Channel => options.Value.Channel switch
    {
        NetzeDuisburgChannelKind.Portal => portalChannel,
        _ => mailChannel,
    };

    public IReadOnlyList<string> Validate(SubmissionKind kind, NabDataCard card) => kind switch
    {
        SubmissionKind.Netzanmeldung => ValidateNetzanmeldung(card),
        SubmissionKind.Inbetriebnahme => ValidateInbetriebnahme(card),
        _ => [],
    };

    public Task<ApplicationPackage> PrepareAsync(SubmissionKind kind, AnmeldeProcess process, CancellationToken ct)
    {
        var card = process.DataCard ?? throw new InvalidOperationException("Datenkarte fehlt.");

        var documents = new List<GeneratedDocument>();
        List<DocumentRef> attachments;
        switch (kind)
        {
            case SubmissionKind.Netzanmeldung:
                var required = options.Value.EffectiveRequiredAttachments;
                attachments = card.Dokumente
                    .Where(d => NetzanmeldungAttachments.Contains(d.Kind) || required.Contains(d.Kind))
                    .ToList();
                var present = attachments.Select(a => a.Kind).ToHashSet();
                documents.Add(forms.CreateE1(card, present, Antragsdatum(process, SubmissionStages.Netzanmeldung.Ready)));
                documents.Add(forms.CreateE2(card));
                if (card.Speicher is not null)
                    documents.Add(forms.CreateE3(card, present));
                LogMissingOptionalAttachments(present);
                break;

            case SubmissionKind.Inbetriebnahme:
                // Die Stufe startet erst, wenn das Inbetriebnahmedatum bekannt und erreicht ist (InbetriebnahmeReady).
                var datum = process.Inbetriebnahmedatum
                    ?? throw new InvalidOperationException("Inbetriebnahmedatum fehlt für das E.8.");
                documents.Add(forms.CreateE8(card, datum));
                // Der Zählerantrag geht mit dem E.8 raus: je gewechseltem Zähler ein Antrag, sonst eine Fertigmeldung.
                documents.AddRange(forms.CreateZaehlerantraege(card));
                attachments = [];
                break;

            default:
                throw new InvalidOperationException($"Netze Duisburg erwartet keine eigene Einreichung {kind}.");
        }

        logger.LogInformation(
            "Paket vorbereitet: {Einreichungsart}, {Formulare} Formulare, {Anhaenge} Anhänge",
            kind, documents.Count, attachments.Count);
        return Task.FromResult(new ApplicationPackage(process.Id, kind, Code, documents, attachments));
    }

    private List<string> ValidateNetzanmeldung(NabDataCard card)
    {
        var problems = new List<string>();

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

        foreach (var required in options.Value.EffectiveRequiredAttachments.Distinct())
        {
            if (!card.Dokumente.Any(d => d.Kind == required))
                problems.Add($"{Label(required)} fehlt (Pflichtanhang).");
        }
        return problems;
    }

    /// <summary>
    /// Stabiles Antragsdatum für E.1 „Datum, Ort“: Eintritt der aktuellen Revision in den Bereit-Zustand der Stufe.
    /// Gleich bei jedem erneuten Erzeugen (Retry, DryRun → Review, Resume); ein Resume zählt nicht als neuer Eintritt.
    /// Ohne passenden Übergang (z. B. direkt angelegter Vorgang) gilt der Anlagezeitpunkt des Vorgangs.
    /// </summary>
    internal static DateOnly Antragsdatum(AnmeldeProcess process, ProcessState ready)
    {
        var transitions = process.Transitions.OrderBy(t => t.At).ToList();
        var revisionStart = transitions.FindLastIndex(t => t.To == ProcessState.Created);
        var entered = transitions.Skip(revisionStart + 1).FirstOrDefault(t => t.To == ready && t.From != ProcessState.Blocked);
        return DateOnly.FromDateTime(ProcessCalendar.ToBerlin(entered?.At ?? process.CreatedAt).DateTime);
    }

    /// <summary>
    /// Zählerangaben für den Zählerantrag. Ohne gewechselten Zähler entsteht eine Fertigmeldung – kein Problem.
    /// Das Inbetriebnahmedatum sichert die Prozesssteuerung (InbetriebnahmeReady).
    /// </summary>
    private static List<string> ValidateInbetriebnahme(NabDataCard card)
    {
        var problems = new List<string>();
        for (var i = 0; i < card.Zaehler.Count; i++)
        {
            var zaehler = card.Zaehler[i];
            var nummerNoetig = zaehler.Wechseltyp is ZaehlerwechselTyp.TauschZweirichtungszaehler
                or ZaehlerwechselTyp.Ausbau or ZaehlerwechselTyp.UmbauKaskade;
            if (nummerNoetig && string.IsNullOrWhiteSpace(zaehler.Zaehlernummer))
                problems.Add($"Zähler {i + 1}: Zählernummer fehlt.");

            var neuerZaehler = zaehler.Wechseltyp is ZaehlerwechselTyp.TauschZweirichtungszaehler
                or ZaehlerwechselTyp.Neusetzung or ZaehlerwechselTyp.UmbauKaskade;
            if (neuerZaehler && zaehler.Bauform is null)
                problems.Add($"Zähler {i + 1}: Zählerbauform (3-Punkt oder eHZ) fehlt.");
        }
        return problems;
    }

    /// <summary>Lageplan und Herstellerdokumente haben noch keinen Adapter – fehlen sie, nur eine Warnung.</summary>
    private void LogMissingOptionalAttachments(IReadOnlySet<DocumentKind> present)
    {
        var required = options.Value.EffectiveRequiredAttachments;
        foreach (var kind in NetzanmeldungAttachments.Where(k => !required.Contains(k) && !present.Contains(k)))
            logger.LogWarning("Optionaler Anhang fehlt: {DocumentKind}", kind);
    }

    private static string Label(DocumentKind kind) => kind switch
    {
        DocumentKind.EvuVollmacht => "EVU-Vollmacht",
        DocumentKind.Schaltplan => "Übersichtsschaltplan",
        DocumentKind.Lageplan => "Lageplan",
        DocumentKind.Datenblatt => "Datenblatt",
        DocumentKind.Einheitenzertifikat => "Einheitenzertifikat",
        DocumentKind.NaSchutzZertifikat => "NA-Schutz-Zertifikat (E.6)",
        DocumentKind.NaSchutzPruefbericht => "Prüfbericht NA-Schutz (E.7)",
        _ => kind.ToString(),
    };
}
