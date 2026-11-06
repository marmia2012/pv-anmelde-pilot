using System.Globalization;
using Microsoft.Extensions.Logging;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Submission;
using F = PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms.NetzeDuisburgFormFields;

namespace PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms;

/// <summary>
/// Füllt die Netze-Duisburg-Formulare aus der NAB-Datenkarte. Nur Werte aus der Datenkarte und die Festwerte laut
/// Dataflow (<c>docs/context/04-dataflow-feldmapping.md</c>). Datenblattwerte, Unterschriften und der E.8-Abschnitt
/// ab „Symmetrie“ bleiben leer. Mapping je Feld: <c>docs/konzept/netze-duisburg-adapter.md</c>.
/// </summary>
/// <remarks>Geloggt werden nur Formularname und Anzahl der Felder, nie Inhalte.</remarks>
public sealed class NetzeDuisburgFormGenerator(ILogger<NetzeDuisburgFormGenerator> logger)
{
    public const string PdfContentType = "application/pdf";

    /// <summary>Abbildung im FNN-Hinweis „Anschluss und Betrieb von Speichern am Niederspannungsnetz“ (Dataflow: 8).</summary>
    public const string Anschlusskonzept = "8";

    /// <summary>Dauerhafte Begrenzung der Einspeiseleistung in % (Dataflow: 60).</summary>
    public const string DauerhafteBegrenzungProzent = "60";

    public const string Primaerenergietraeger = "Sonne";

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// Netzbetreiberneutrale Messkonzepte der Datenkarte (Schreibweise wie im Reonic-Adapter,
    /// <c>NetzanmeldungChecklist.Messkonzepte</c>) → Nummer im Netze-Duisburg-Katalog „Messkonzepte 2026“.
    /// </summary>
    private static readonly Dictionary<string, string> Messkonzepte = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Volleinspeisung – Erzeugungszähler"] = "MK 1",
        ["Überschusseinspeisung – Zweirichtungszähler"] = "MK 2",
        ["Kaskade – Zweirichtungs- + Erzeugungszähler"] = "MK 3",
        ["Überschuss + separater Zähler steuerbare Verbrauchseinrichtung (§ 14a)"] = "MK 8",
    };

    private const string MesskonzeptSonstiges = "Sonstiges (siehe Bemerkung)";

    /// <summary>E.1 Antragstellung. <paramref name="attachments"/> steuert die Beilagen-Checkboxen.</summary>
    public GeneratedDocument CreateE1(NabDataCard card, IReadOnlyCollection<DocumentKind> attachments, DateOnly today) =>
        Fill(NetzeDuisburgForm.E1, form =>
        {
            var betreiber = card.Anlagenbetreiber;
            var adresse = card.Anlagenadresse;

            form.SetText(F.E1.AnlageName, Name(betreiber));
            form.SetText(F.E1.AnlageStrasse, Strasse(adresse));
            form.SetText(F.E1.AnlagePlzOrt, PlzOrt(adresse));
            form.SetText(F.E1.AnlageKontakt, Kontakt(betreiber));

            // Anschlussnehmer und abweichender Betreiber: die Datenkarte kennt nur den Betreiber und die Anlagenadresse.
            form.SetText(F.E1.AnschlussnehmerName, Name(betreiber));
            form.SetText(F.E1.AnschlussnehmerStrasse, Strasse(adresse));
            form.SetText(F.E1.AnschlussnehmerPlzOrt, PlzOrt(adresse));
            form.SetText(F.E1.AnschlussnehmerKontakt, Kontakt(betreiber));

            form.SetText(F.E1.BetreiberName, Name(betreiber));
            form.SetText(F.E1.BetreiberStrasse, Strasse(adresse));
            form.SetText(F.E1.BetreiberPlzOrt, PlzOrt(adresse));
            form.SetText(F.E1.BetreiberKontakt, Kontakt(betreiber));

            form.SetText(F.E1.ErrichterFirma, card.Errichter.Firma);
            form.SetText(F.E1.ErrichterOrt, card.Errichter.Ort);
            form.SetText(F.E1.ErrichterEintragungsnummer, card.Errichter.Eintragungsnummer);

            form.SetCheckBox(F.E1.AnlagenartNeuerrichtung, card.Installationsart == Installationsart.Neuerrichtung);
            form.SetCheckBox(F.E1.AnlagenartErweiterung, card.Installationsart == Installationsart.Erweiterung);

            form.SetCheckBox(F.E1.LageplanBeigefuegt, attachments.Contains(DocumentKind.Lageplan));
            form.SetCheckBox(F.E1.DatenblattBeigefuegt, true); // E.2 wird immer erzeugt.
            form.SetCheckBox(F.E1.EinheitenzertifikateLiegenVor, attachments.Contains(DocumentKind.Einheitenzertifikat));
            form.SetCheckBox(F.E1.NaSchutzZertifikatBeigefuegt, attachments.Contains(DocumentKind.NaSchutzZertifikat));
            form.SetCheckBox(F.E1.SchaltplanBeigefuegt, attachments.Contains(DocumentKind.Schaltplan));

            form.SetText(F.E1.GeplanterInbetriebsetzungstermin, Date(card.Installationstermin));
            form.SetText(F.E1.DatumOrt, $"{adresse.Ort}, {Date(today)}");
            // Unterschrift des Anschlussnehmers: bewusst leer (keine Unterschrift in diesem Feature).
        });

    /// <summary>E.2 Datenblatt Erzeugungsanlage: Erzeugungseinheiten = Wechselrichter.</summary>
    public GeneratedDocument CreateE2(NabDataCard card) =>
        Fill(NetzeDuisburgForm.E2, form =>
        {
            SetAnlagenanschrift(form, card, F.E2.AnlageName, F.E2.AnlageStrasse, F.E2.AnlagePlzOrt);
            form.SetCheckBox(F.E2.EnergieartSonne, true);

            // Das Formular hat nur eine Zeile: erster Wechselrichter-Typ in die Felder, weitere in die Bemerkungen.
            var bemerkungen = new List<string>();
            if (MesskonzeptText(card) is { } messkonzept)
                bemerkungen.Add(messkonzept);
            if (card.Wechselrichter.Count > 0)
            {
                var erster = card.Wechselrichter[0];
                form.SetText(F.E2.EinheitenHersteller, erster.Hersteller);
                form.SetText(F.E2.EinheitenTyp, erster.Modell);
                form.SetText(F.E2.EinheitenAnzahl, Number(erster.Anzahl));
                bemerkungen.AddRange(card.Wechselrichter.Skip(1).Select(w =>
                    $"Weitere Erzeugungseinheit: {w.Hersteller} {w.Modell}, Anzahl {Number(w.Anzahl)}"));
            }
            form.SetText(F.E2.MaxWirkleistung, card.Wechselrichter.Count > 0 ? Number(card.WechselrichterLeistungKw) : null);
            // Max. Scheinleistung: Datenblattwert, bleibt leer.

            if (NetzeinspeisungField(card) is { } phasen)
                form.SetCheckBox(phasen, true);

            form.SetCheckBox(F.E2.InselbetriebJa, true);
            form.SetCheckBox(F.E2.MotorischerAnlaufNein, true);
            var volleinspeisung = card.Einspeiseart == Einspeiseart.Volleinspeisung;
            form.SetCheckBox(F.E2.UeberschusseinspeisungJa, !volleinspeisung);
            form.SetCheckBox(F.E2.UeberschusseinspeisungNein, volleinspeisung);
            form.SetCheckBox(F.E2.VolleinspeisungJa, volleinspeisung);
            form.SetCheckBox(F.E2.VolleinspeisungNein, !volleinspeisung);
            form.SetCheckBox(F.E2.BlindleistungskompensationNichtVorhanden, true);

            SetLines(form, F.E2.Bemerkungen, bemerkungen);
        });

    /// <summary>E.3 Datenblatt Speicher. Nur aufrufen, wenn die Anlage einen Speicher hat.</summary>
    public GeneratedDocument CreateE3(NabDataCard card, IReadOnlyCollection<DocumentKind> attachments)
    {
        var speicher = card.Speicher ?? throw new InvalidOperationException("E.3 nur für Anlagen mit Speicher.");
        return Fill(NetzeDuisburgForm.E3, form =>
        {
            SetAnlagenanschrift(form, card, F.E3.AnlageName, F.E3.AnlageStrasse, F.E3.AnlagePlzOrt);
            form.SetText(F.E3.ErrichterFirmaOrt, FirmaOrt(card.Errichter));
            // Errichter Straße/Telefon: leer, bis die Errichter-Konfiguration erweitert ist.

            form.SetText(F.E3.SpeicherHerstellerTyp, $"{speicher.Hersteller} {speicher.Modell}");
            form.SetText(F.E3.SpeicherAnzahl, "1");
            form.SetCheckBox(F.E3.AcGekoppelt, speicher.Kopplung == Speicherkopplung.Ac);
            form.SetCheckBox(F.E3.DcGekoppelt, speicher.Kopplung == Speicherkopplung.Dc);
            form.SetCheckBox(F.E3.AllpoligeTrennung, true);
            form.SetCheckBox(F.E3.NaSchutzVorhanden, true);
            // Nutzbare Kapazität, Umrichter (Hersteller/Typ, Anzahl, cos φ, Scheinleistung, Wirkleistung, Bemessungsstrom):
            // Datenblattwerte, bleiben leer.

            form.SetText(F.E3.Anschlusskonzept, Anschlusskonzept);
            form.SetCheckBox(F.E3.SchaltplanBeigefuegt, attachments.Contains(DocumentKind.Schaltplan));
            form.SetText(F.E3.Primaerenergietraeger, Primaerenergietraeger);
            form.SetCheckBox(F.E3.SpeicherNichtVomNetz, true);
            form.SetCheckBox(F.E3.EinheitenzertifikateLiegenVor, attachments.Contains(DocumentKind.Einheitenzertifikat));

            form.SetCheckBox(F.E3.FerngesteuertNein, true);
            form.SetText(F.E3.DauerhaftBegrenztProzent, DauerhafteBegrenzungProzent);
            form.SetCheckBox(F.E3.DauerhaftBegrenztJa, true);

            form.SetText(F.E3.ErrichterAusweisNr, card.Errichter.Eintragungsnummer);
            form.SetText(F.E3.ErrichterBeiNetzbetreiber, card.Errichter.EingetragenBeiNetzbetreiber);
            // Unterschrift Errichter, Ort/Datum: bewusst leer.
        });
    }

    /// <summary>E.8 Inbetriebsetzungsprotokoll. Abschnitte ab „Symmetrie“ und die Scheinleistung bleiben leer.</summary>
    public GeneratedDocument CreateE8(NabDataCard card, DateOnly inbetriebnahmedatum) =>
        Fill(NetzeDuisburgForm.E8, form =>
        {
            SetAnlagenanschrift(form, card, F.E8.AnlageName, F.E8.AnlageStrasse, F.E8.AnlagePlzOrt);
            form.SetText(F.E8.ErrichterFirmaOrt, FirmaOrt(card.Errichter));
            // Errichter Straße/Telefon: leer, bis die Errichter-Konfiguration erweitert ist.

            // Max. Scheinleistung: Datenblattwert, bleibt leer.
            form.SetText(F.E8.MaxWirkleistung, card.Wechselrichter.Count > 0 ? Number(card.WechselrichterLeistungKw) : null);
            form.SetText(F.E8.Modulleistung, card.Module.Count > 0 ? Number(card.GesamtleistungKwp) : null);

            form.SetCheckBox(F.E8.UebereinstimmungE2E3, true);
            form.SetCheckBox(F.E8.Abrechnungsmessung, true);
            form.SetCheckBox(F.E8.EinheitenzertifikateVorhanden, true);
            form.SetCheckBox(F.E8.LeistungsflussueberwachungZertifikat, false);
            form.SetCheckBox(F.E8.NaSchutzZertifikatVorhanden, true);
            // Integrierter NA-Schutz „ja“: das Formular fragt nur den eingestellten Wert U> ab (Datenblatt) → leer.
            form.SetCheckBox(F.E8.DrosselungImUmrichter, true);
            form.SetCheckBox(F.E8.Energieflussrichtungssensor, true);

            // Symmetrie, Blindleistungsverfahren, TF-Sperren, PAV,E: offen (Martin klärt), bleiben leer.
            form.SetText(F.E8.Inbetriebsetzungsdatum, Date(inbetriebnahmedatum));
            // Unterschriften Anlagenbetreiber/-errichter, Ort/Datum: bewusst leer.
        });

    /// <summary>Antrag auf Zählermontage – je Zähler ein Antrag (Vorgabe im Formular).</summary>
    public GeneratedDocument CreateZaehlerantrag(NabDataCard card, Zaehler zaehler, int nummer) =>
        Fill(NetzeDuisburgForm.Zaehlerantrag, form =>
        {
            var z = AuftragFor(zaehler.Wechseltyp);
            form.SetRadio(F.Zaehlerantrag.Auftrag, z.Auftrag);
            if (z.NummerFeld is { } nummerFeld)
                form.SetText(nummerFeld, zaehler.Zaehlernummer);

            var betreiber = card.Anlagenbetreiber;
            var adresse = card.Anlagenadresse;
            form.SetText(F.Zaehlerantrag.Grundstueckseigentuemer, Name(betreiber));
            form.SetText(F.Zaehlerantrag.StandortStrasse, Strasse(adresse));
            form.SetText(F.Zaehlerantrag.StandortPlzOrt, PlzOrt(adresse));
            form.SetText(F.Zaehlerantrag.LageVerbrauchsstelle, Join(", ", zaehler.LageVerbrauchsstelle, zaehler.BezeichnungVerbrauchsstelle));
            form.SetText(F.Zaehlerantrag.Einbauort, zaehler.Einbauort);

            form.SetText(F.Zaehlerantrag.AnschlussnutzerName, Name(betreiber));
            form.SetText(F.Zaehlerantrag.AnschlussnutzerStrasse, Strasse(adresse));
            form.SetText(F.Zaehlerantrag.AnschlussnutzerPlzOrt, PlzOrt(adresse));
            form.SetText(F.Zaehlerantrag.AnschlussnutzerEmail, betreiber.Email);
            form.SetText(F.Zaehlerantrag.AnschlussnutzerTelefon, betreiber.Telefon);

            if (z.MitMesseinrichtung && zaehler.Bauform is { } bauform)
            {
                form.SetRadio(F.Zaehlerantrag.Zaehler, bauform == Zaehlerbauform.Ehz
                    ? F.Zaehlerantrag.ZaehlerOption.EhzZweirichtung
                    : F.Zaehlerantrag.ZaehlerOption.DreiPunktZweirichtung);
            }
            form.SetRadio(F.Zaehlerantrag.Messebene, F.Zaehlerantrag.MessebeneOption.Niederspannung);
            form.SetText(F.Zaehlerantrag.SicherungHak, card.NhSicherungAmpere.ToString(German));
            form.SetRadio(F.Zaehlerantrag.Massnahme, F.Zaehlerantrag.MassnahmeOption.AnlageKannInBetriebGehen);
            form.SetRadio(F.Zaehlerantrag.Nutzungsart, F.Zaehlerantrag.NutzungsartOption.Eigenerzeugung);
            form.SetText(F.Zaehlerantrag.EegPeakleistung, card.Module.Count > 0 ? Number(card.GesamtleistungKwp) : null);
            // Installateur, Ort/Datum, Fachkraft, Unterschriften, Terminwunsch, Lieferant: bewusst leer.
        }, $"Zaehlerantrag_{nummer}.pdf");

    /// <param name="Auftrag">Export-Wert des Radios „Auftrag“.</param>
    /// <param name="NummerFeld">Feld für die Zählernummer, <c>null</c> = keine Nummer im Formular.</param>
    /// <param name="MitMesseinrichtung">Ein (neuer) Zähler wird gesetzt → gewünschte Messeinrichtung angeben.</param>
    private sealed record ZaehlerAuftrag(string Auftrag, string? NummerFeld, bool MitMesseinrichtung);

    /// <summary>
    /// Wechseltyp der Datenkarte → Auftrag im Zählerantrag. Kaskaden-Umbau läuft als Zählerwechsel (Annahme,
    /// das Formular kennt keinen eigenen Auftrag); ohne Wechsel bleibt die Fertigmeldung/Inbetriebsetzung.
    /// </summary>
    private static ZaehlerAuftrag AuftragFor(ZaehlerwechselTyp typ) => typ switch
    {
        ZaehlerwechselTyp.TauschZweirichtungszaehler or ZaehlerwechselTyp.UmbauKaskade =>
            new(F.Zaehlerantrag.AuftragOption.Zaehlerwechsel, F.Zaehlerantrag.ZaehlernummerWechsel, true),
        ZaehlerwechselTyp.Ausbau => new(F.Zaehlerantrag.AuftragOption.Zaehlerausbau, F.Zaehlerantrag.ZaehlernummerAusbau, false),
        ZaehlerwechselTyp.Neusetzung => new(F.Zaehlerantrag.AuftragOption.Zaehlereinbau, null, true),
        _ => new(F.Zaehlerantrag.AuftragOption.FertigmeldungInbetriebsetzung, null, false),
    };

    private GeneratedDocument Fill(NetzeDuisburgForm kind, Action<PdfFormFiller> fill, string? fileName = null)
    {
        using var template = NetzeDuisburgTemplates.Open(kind);
        using var form = PdfFormFiller.Open(kind.ToString(), template);
        fill(form);
        var content = form.Save();
        logger.LogInformation(
            "Formular erzeugt: {Formular}, {GesetzteFelder} Felder gesetzt, {LeereFelder} Felder leer",
            kind, form.SetCount, form.EmptyCount);
        return new GeneratedDocument(fileName ?? NetzeDuisburgTemplates.FileName(kind), PdfContentType, content);
    }

    private static void SetAnlagenanschrift(PdfFormFiller form, NabDataCard card, string name, string strasse, string plzOrt)
    {
        form.SetText(name, Name(card.Anlagenbetreiber));
        form.SetText(strasse, Strasse(card.Anlagenadresse));
        form.SetText(plzOrt, PlzOrt(card.Anlagenadresse));
    }

    /// <summary>Verteilt Zeilen auf die vorhandenen Felder; was nicht passt, kommt in die letzte Zeile.</summary>
    private static void SetLines(PdfFormFiller form, IReadOnlyList<string> fields, IReadOnlyList<string> lines)
    {
        for (var i = 0; i < fields.Count && i < lines.Count; i++)
        {
            var isLast = i == fields.Count - 1;
            form.SetText(fields[i], isLast ? string.Join("; ", lines.Skip(i)) : lines[i]);
        }
    }

    /// <summary>
    /// Phasen nur, wenn alle Wechselrichter dieselbe bekannte Phasenzahl haben. Dreiphasiger Wechselrichter = „Drehstrom“
    /// („3-phasig“ steht für drei einphasige Einheiten auf drei Außenleitern).
    /// </summary>
    private static string? NetzeinspeisungField(NabDataCard card)
    {
        var phasen = card.Wechselrichter.Select(w => w.Phasen).Distinct().ToList();
        return phasen is [{ } p] ? p switch
        {
            1 => F.E2.Netzeinspeisung1Phasig,
            2 => F.E2.Netzeinspeisung2Phasig,
            3 => F.E2.NetzeinspeisungDrehstrom,
            _ => null,
        } : null;
    }

    private static string? MesskonzeptText(NabDataCard card)
    {
        if (string.IsNullOrWhiteSpace(card.Messkonzept))
            return null;
        var messkonzept = card.Messkonzept.Trim();
        if (Messkonzepte.TryGetValue(messkonzept, out var nummer))
            return $"Messkonzept {nummer} gem. Netze-Duisburg-Messkonzepte 2026";
        if (string.Equals(messkonzept, MesskonzeptSonstiges, StringComparison.OrdinalIgnoreCase))
            return Join(": ", "Messkonzept", card.MesskonzeptBemerkung ?? messkonzept);
        return $"Messkonzept: {messkonzept}";
    }

    private static string Name(Person person) => Join(" ", person.Vorname, person.Nachname) ?? string.Empty;

    private static string Strasse(PostalAddress address) => Join(" ", address.Strasse, address.Hausnummer) ?? string.Empty;

    private static string PlzOrt(PostalAddress address) => Join(" ", address.Plz, address.Ort) ?? string.Empty;

    private static string? Kontakt(Person person) => Join(", ", person.Telefon, person.Email);

    private static string? FirmaOrt(Anlagenerrichter errichter) => Join(", ", errichter.Firma, errichter.Ort);

    private static string? Join(string separator, params string?[] parts)
    {
        var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
        return present.Count == 0 ? null : string.Join(separator, present);
    }

    private static string Number(decimal value) => value.ToString("0.##", German);

    private static string? Date(DateOnly? date) => date?.ToString("dd.MM.yyyy", German);
}
