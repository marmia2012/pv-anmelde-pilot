namespace PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms;

/// <summary>
/// Alle AcroForm-Feldnamen der Netze-Duisburg-Vorlagen an einer Stelle (voll qualifiziert, exakt wie in der PDF).
/// Ausgelesen mit PDFsharp aus den Vorlagen in <c>Forms/Templates/</c> (Stand 06.11.2026).
/// Bei einer neuen Vorlagenversion hier anpassen; ein unbekannter Name wirft beim Ausfüllen.
/// Feld → Quelle: <c>docs/konzept/netze-duisburg-adapter.md</c>.
/// </summary>
internal static class NetzeDuisburgFormFields
{
    /// <summary>E.1 Antragstellung (VDE-AR-N 4105, Formular 2034-0001).</summary>
    public static class E1
    {
        public const string AnlageName = "E.1: Anlagenanschrift: Vorname, Name";
        public const string AnlageStrasse = "E.1: Anlagenanschrift: Straße, Hausnummer";
        public const string AnlagePlzOrt = "E.1: Anlagenanschrift: PLZ, Ort";
        public const string AnlageKontakt = "E.1: Anlagenanschrift: Telefon, E-Mail";

        public const string AnschlussnehmerName = "E.1: Anschlussnehmer/Eigentümer: Vorname, Name";
        public const string AnschlussnehmerStrasse = "E.1: Anschlussnehmer/Eigentümer: Straße, Hausnummer";
        public const string AnschlussnehmerPlzOrt = "E.1: Anschlussnehmer/Eigentümer: PLZ, Ort";
        public const string AnschlussnehmerKontakt = "E.1: Anschlussnehmer/Eigentümer: Telefon/E-Mail";

        public const string BetreiberName = "E.1: Anlagenbetreiber: Vorname, Name";
        public const string BetreiberStrasse = "E.1: Anlagenbetreiber: Straße, Hausnummer";
        public const string BetreiberPlzOrt = "E.1: Anlagenbetreiber: PLZ/Ort";
        public const string BetreiberKontakt = "E.1: Anlagenbetreiber: Telefon/E-Mail";

        public const string ErrichterFirma = "E.1: Anlagenerrichter: Firma";
        public const string ErrichterOrt = "E.1: Anlagenerrichter: Ort";
        public const string ErrichterEintragungsnummer = "E.1: Anlagenerrichter: Eintragungsnummer bei Netzbetreiber";

        public const string AnlagenartNeuerrichtung = "E.1: Anlagenart: Neuerrichtung";
        public const string AnlagenartErweiterung = "E.1: Anlagenart: Erweiterung";
        public const string AnlagenartRueckbau = "E.1: Anlagenart: Rückbau";

        public const string AnmeldevordruckBeigefuegt = "E.1: Anmeldervordruck \"Anmeldung zum Netzanschluss\" beigefügt";
        public const string LageplanBeigefuegt =
            "E.1: Lageplan mit Bezeichnung und Grenzen des Grundstücks sowie Aufstellungsort der EZA beigefügt";
        public const string DatenblattBeigefuegt = "E.1: Datenblatt für EZA beigefügt";
        public const string EinheitenzertifikateLiegenVor = "E.1: Einheitenzertifikate nach VDE-AR-N 4105/4110 liegen vor?";
        public const string NaSchutzZertifikatBeigefuegt = "E.1: Zertifikat für NA-Schutz beigefügt";
        public const string LeistungsflussueberwachungZertifikat =
            "E.1: Zertifikat für Leistungsflussüberwachung am NAP (PAV,E, 70% Begrenzung nach 5.7.4.2, Symmetrieeinrichtung)";
        public const string SchaltplanBeigefuegt = "E.1: Übersichtsschaltplan ab NAP beigefügt";

        public const string GeplanterInbetriebsetzungstermin = "E.1: Geplanter Inbetriebsetzungstermin";
        public const string Unterschrift = "E.1: Unterschrift des Anschlussnehmers";
        public const string DatumOrt = "E.1: Datum, Ort";
    }

    /// <summary>E.2 Datenblatt für Erzeugungsanlagen (Formular 2035-0001).</summary>
    public static class E2
    {
        public const string AnlageName = "E.2: Anlagenanschrift: Vorname, Name";
        public const string AnlageStrasse = "E.2: Anlagenanschrift: Straße, Hausnummer";
        public const string AnlagePlzOrt = "E.2: Anlagenanschrift: PLZ, Ort";

        public const string EnergieartSonne = "E.2: Energieart: Sonne";
        public const string EnergieartWind = "E.2: Energieart: Wind";
        public const string EnergieartWasser = "E.2: Energieart: Wasser";
        public const string EnergieartSonstige = "E.2: Energieart: Sonstige";

        public const string BhkwBiogas = "E.2: BHKW mit: Biogas";
        public const string BhkwErdgas = "E.2: BHKW mit: Erdgas";
        public const string BhkwOel = "E.2: BHKW mit: Öl";
        public const string BhkwSonstige = "E.2: BHKW mit: Sonstige";
        public const string BhkwMonovalent = "E.2: BHKW mit: monovalenter Betriebsweise";

        public const string EinheitenHersteller = "E.2: Erzeugungseinheiten: Hersteller";
        public const string EinheitenTyp = "E.2: Erzeugungseinheiten: Typ";
        public const string EinheitenAnzahl = "E.2: Erzeugungseinheiten: Anzahl baugleicher Einheiten";

        /// <summary>kW (Einheit steht im Formular).</summary>
        public const string MaxWirkleistung = "E.2: EZA: Max. Wirkleistung";

        /// <summary>kVA – Datenblattwert, bleibt vorerst leer.</summary>
        public const string MaxScheinleistung = "E.2: EZA: Max. Scheinleistung";

        public const string Netzeinspeisung1Phasig = "E.2: Netzeinspeisung: 1-phasig";
        public const string Netzeinspeisung2Phasig = "E.2: Netzeinspeisung: 2-phasig";
        public const string Netzeinspeisung3Phasig = "E.2: Netzeinspeisung: 3-phasig";
        public const string NetzeinspeisungDrehstrom = "E.2: Netzeinspeisung: Drehstrom";

        public const string InselbetriebJa = "E.2: Betriebsweise: Inselbetrieb vorgesehen? - ja";
        public const string InselbetriebNein = "E.2: Betriebsweise: Inselbetrieb vorgesehen? - nein";
        public const string MotorischerAnlaufJa = "E.2: Betriebsweise: Motorischer Anlauf vorgesehen? - ja";
        public const string MotorischerAnlaufNein = "E.2: Betriebsweise: Motorischer Anlauf vorgesehen? - nein";
        public const string UeberschusseinspeisungJa = "E.2: Betriebsweise: Überschusseinspeisung? - ja";
        public const string UeberschusseinspeisungNein = "E.2: Betriebsweise: Überschusseinspeisung? - nein";
        public const string VolleinspeisungJa = "E.2: Betriebsweise: Volleinspeisung? - ja";
        public const string VolleinspeisungNein = "E.2: Betriebsweise: Volleinspeisung? - nein";

        public const string BlindleistungskompensationNichtVorhanden = "E.2: Blindleistungskompensationsanlage: nicht vorhanden";
        public const string BlindleistungskompensationVorhandenMit = "E.2: Blindleistungskompensationsanlage: vorhanden mit";
        public const string BlindleistungskompensationStufen = "E.2: Blindleistungskompensationsanlage: Anzahl Stufen";
        public const string BlindleistungskompensationKleinsteStufe =
            "E.2: Blindleistungskompensationsanlage: Blindleistung d. kleinsten Stufe";
        public const string BlindleistungskompensationVerdrosselung =
            "E.2: Blindleistungskompensationsanlage: Verdrosselungsgrad bzw. Resonanzfrequenz";

        /// <summary>Vier Zeilen „Bemerkungen“: Messkonzept, weitere Wechselrichter-Typen.</summary>
        public static readonly IReadOnlyList<string> Bemerkungen =
        [
            "E.2: Bemerkungen (1)",
            "E.2: Bemerkungen (2)",
            "E.2: Bemerkungen (3)",
            "E.2: Bemerkungen (4)",
        ];
    }

    /// <summary>E.3 Datenblatt für Speicher (Formular 2036-0001), nur bei Anlagen mit Speicher.</summary>
    public static class E3
    {
        public const string AnlageName = "E.3: Anlagenanschrift: Vorname, Name";
        public const string AnlageStrasse = "E.3: Anlagenanschrift: Straße, Hausnummer";
        public const string AnlagePlzOrt = "E.3: Anlagenanschrift: PLZ, Ort";

        public const string ErrichterFirmaOrt = "E.3: Anlagenerrichter: Firma, Ort";
        public const string ErrichterStrasse = "E.3: Anlagenerrichter: Straße, Hausnummer";
        public const string ErrichterKontakt = "E.3: Anlagenerrichter: Telefon, E-Mail";

        public const string SpeicherHerstellerTyp = "E.3: Speichersystem: Hersteller/Typ";
        public const string SpeicherAnzahl = "E.3: Speichersystem: Anzahl";

        public const string AcGekoppelt = "E.3: Anschluss des Speichersystems: AC-gekoppelt";
        public const string DcGekoppelt = "E.3: Anschluss des Speichersystems: DC-gekoppelt";

        /// <summary>Schreibweise „bildenes“ wie in der Vorlage.</summary>
        public const string InselnetzBildend = "E.3: Anschluss des Speichersystems: Inselnetz bildenes System";
        public const string Wechselstrom = "E.3: Anschluss des Speichersystems: Wechselstrom";
        public const string WechselstromL1 = "E.3: Anschluss des Speichersystems: Wechselstrom (L1)";
        public const string WechselstromL2 = "E.3: Anschluss des Speichersystems: Wechselstrom (L2)";
        public const string WechselstromL3 = "E.3: Anschluss des Speichersystems: Wechselstrom (L3)";
        public const string Drehstrom = "E.3: Anschluss des Speichersystems: Drehstrom";
        public const string NutzbareKapazitaet = "E.3: Anschluss des Speichersystems: Nutzbare Speicherkapazität";
        public const string AllpoligeTrennung =
            "E.3: Anschluss des Speichersystems: Allpolige Trennung vom Netz bei Netzersatzbetrieb";
        public const string NaSchutzVorhanden = "E.3: Anschluss des Speichersystems: NA-Schutz nach VDE AR-N 4105 vorhanden";

        public const string UmrichterHerstellerTyp = "E.3: Umrichter des Speichersystems: Hersteller/Typ";
        public const string UmrichterAnzahl = "E.3: Umrichter des Speichersystems: Anzahl";
        public const string UmrichterVerschiebungsfaktor = "E.3: Umrichter des Speichersystems: Verschiebungsfaktor (Bezug)";
        public const string UmrichterScheinleistung = "E.3: Umrichter des Speichersystems: Scheinleistung";
        public const string UmrichterWirkleistung = "E.3: Umrichter des Speichersystems: Wirkleistung";
        public const string UmrichterBemessungsstrom = "E.3: Umrichter des Speichersystems: Bemessungsstrom";

        public const string Anschlusskonzept = "E.3: Anschlusskonzept: Nummer der Abbildung nach FNN-Hinweis Abschnitt 5";
        public const string SchaltplanBeigefuegt = "E.3: Anschlusskonzept: Übersichtsschaltplan beigefügt";
        public const string Primaerenergietraeger = "E.3: Anschlusskonzept: Verwendete Primärenergieträger";
        public const string PrimaerenergietraegerGetrennt =
            "E.3: Anschlusskonzept: Unterschiedliche Primärenergieträger werden getrennt erfasst";
        public const string EinspeiseverguetungenKorrekt =
            "E.3: Anschlusskonzept: Unterschiedliche Einspeisevergütungen werden korrekt erfasst";
        public const string SpeicherNichtVomNetz =
            "E.3: Anschlusskonzept: Energie des Speichersystems wird nicht vom Netz bezogen und als geförderte Energie eingespeist";

        public const string EinheitenzertifikateLiegenVor = "E.3: Nachweise: Einheitenzertifikate liegen vor";

        public const string FerngesteuertJa = "E.3: Einspeisemanagement: Ferngesteuert: ja";
        public const string FerngesteuertNein = "E.3: Einspeisemanagement: Ferngesteuert: nein";
        public const string DauerhaftBegrenztProzent = "E.3: Einspeisemanagement: dauerhaft auf % begrenzt";

        /// <summary>Schreibweise „Einspeisemangement“ wie in der Vorlage.</summary>
        public const string DauerhaftBegrenztJa = "E.3: Einspeisemangement: dauerhaft auf % begrenzt: ja";

        public const string ErrichterAusweisNr = "E.3: Nachweis Errichter: Ausweis-Nr";
        public const string ErrichterBeiNetzbetreiber = "E.3: Nachweis Errichter: bei Netzbetreiber";
        public const string Bemerkungen = "E.3: Bemerkungen";
        public const string UnterschriftErrichter = "E.3: Unterschrift Errichter";
        public const string OrtDatum = "E.3: Ort, Datum";
    }

    /// <summary>E.8 Inbetriebsetzungsprotokoll für Erzeugungsanlagen/Speicher (Formular 2041-0001).</summary>
    public static class E8
    {
        public const string AnlageName = "E.8: Anlagenanschrift: Vorname, Name";
        public const string AnlageStrasse = "E.8: Anlagenanschrift: Straße, Hausnummer";
        public const string AnlagePlzOrt = "E.8: Anlagenanschrift: PLZ, Ort";

        public const string ErrichterFirmaOrt = "E.8: Anlagenerrichter: Firma, Ort";
        public const string ErrichterStrasse = "E.8: Anlagenerrichter: Straße, Hausnummer";
        public const string ErrichterKontakt = "E.8: Anlagenerrichter: Telefon, E-Mail";

        /// <summary>kVA – Datenblattwert, bleibt vorerst leer.</summary>
        public const string MaxScheinleistung = "E.8: Max. Scheinleistung";

        /// <summary>kW (Einheit steht im Formular).</summary>
        public const string MaxWirkleistung = "E.8: Max. Wirkleistung";

        /// <summary>kWp, für die Einspeisevergütung maßgebend.</summary>
        public const string Modulleistung = "E.8: Modulleistung";

        /// <summary>Kein Doppelpunkt nach „E.8“ – Schreibweise wie in der Vorlage.</summary>
        public const string UebereinstimmungE2E3 = "E.8 Übereinstimmung von E.2 und E.3 mit Anlagenaufbau";
        public const string Abrechnungsmessung = "E.8: Abrechnungsmessung: Vorinbetriebsetzung & Inbetriebsetzungsprüfung";
        public const string EinheitenzertifikateVorhanden = "E.8: Einheitenzertifikate vorhanden?";
        public const string LeistungsflussueberwachungZertifikat = "E.8: Zertifikat für Leistungsflussüberwachung am NAP vorhanden";
        public const string NaSchutzZertifikatVorhanden = "E.8: Zertifikat für NA-Schutz vorhanden";

        public const string IntegrierterNaSchutzUMax =
            "E.8: Integrierter NA-Schutz: Eingestellter Wert Spannungssteigerungsschutz U>";
        public const string ZentralerNaSchutzUMax =
            "E.8: Zentraler NA-Schutz: Eingestellter Wert Spannungssteigerungsschutz U>";
        public const string ZentralerNaSchutzAusloesetest =
            "E.8: Zentraler NA-Schutz vorhanden: Auslösetest zentraler NA-Schutz - Kuppelschalter durchgeführt";
        public const string ZentralerNaSchutzRuhestrom =
            "E.8: Zentraler NA-Schutz vorhanden: Auslösekreis Zentraler NA-Schutz - Kuppelschalter nach Ruhestromprinzip ausgeführt und geprüft";

        public const string DrosselungImUmrichter =
            "E.8: Technische Einrichtung zur Reduzierung der Einspeiseleistung: Drosselung auf 70% im Umrichter";
        public const string ZertifizierteSteuerungDrosselung =
            "E.8: Technische Einrichtung zur Reduzierung der Einspeiseleistung: Zertifizierte technische Steuerung zur Drosselung auf 70% vorgesehen";
        public const string FernsteuerbareLeistungsreduzierung =
            "E.8: Technische Einrichtung zur Reduzierung der Einspeiseleistung: Technische Einrichtung zur fernsteuerbaren Leistungsreduzierung der Einspeiseleistung durch den Netzbetreiber";
        public const string Energieflussrichtungssensor =
            "E.8: Energieflussrichtungssensor - Funktionstest durch Errichter durchgeführt und bestanden";

        // Ab hier: Abschnitt Symmetrie, Blindleistung, TF-Sperren – offen (Martin klärt), bleiben leer.
        public const string SymmetrieDreiphasig =
            "E.8: Einhaltung der Symmetriebedingung: durch einen Drehstromgenerator/dreiphasigen Umrichter";
        public const string SymmetrieAufteilung =
            "E.8: Einhaltung der Symmetriebedingung: durch folgende Aufteilung einphasig angeschlossener EZE je Außenleiter";
        public const string SymmetrieVorhandenL1 =
            "E.8: Einhaltung der Symmetriebedingung: durch folgende Aufteilung einphasig angeschlossener EZE je Außenleiter: Summe SEmax der vorhandenen EZA/Speicher (L1)";
        public const string SymmetrieVorhandenL2 =
            "E.8: Einhaltung der Symmetriebedingung: durch folgende Aufteilung einphasig angeschlossener EZE je Außenleiter: Summe der vorhandenen EZA/Speicher SEmax (L2)";
        public const string SymmetrieVorhandenL3 =
            "E.8: Einhaltung der Symmetriebedingung: durch folgende Aufteilung einphasig angeschlossener EZE je Außenleiter: Summe der vorhandenen EZA/Speicher SEmax (L3)";
        public const string SymmetrieNeuL1 =
            "E.8: Einhaltung der Symmetriebedingung: durch folgende Aufteilung einphasig angeschlossener EZE je Außenleiter: Summe der neu hinzukommenden EZA/Speicher SEmax (L1)";
        public const string SymmetrieNeuL2 =
            "E.8: Einhaltung der Symmetriebedingung: durch folgende Aufteilung einphasig angeschlossener EZE je Außenleiter: Summe der neu hinzukommenden EZA/Speicher SEmax (L2)";
        public const string SymmetrieNeuL3 =
            "E.8: Einhaltung der Symmetriebedingung: durch folgende Aufteilung einphasig angeschlossener EZE je Außenleiter: Summe der neu hinzukommenden EZA/Speicher SEmax (L3)";
        public const string SymmetrieEinrichtung =
            "E.8: Einhaltung der Symmetriebedingung: durch eine Symmetrieeinrichtung, die den Unsymmetriewert auf 4,6 kVA begrenzt";

        public const string BlindleistungQu = "E.8: Verfahren zur Blindleistungsbereitstellung: Q(U)-Standard-Kennlinie";
        public const string BlindleistungCosPhiP = "E.8: Verfahren zur Blindleistungsbereitstellung: cos phi(P)-Standard-Kennlinie";
        public const string BlindleistungFesterCosPhi = "E.8: Verfahren zur Blindleistungsbereitstellung: Fester Verschiebungsfaktor cos(phi)";
        public const string BlindleistungFesterCosPhiWert =
            "E.8: Verfahren zur Blindleistungsbereitstellung: Fester Verschiebungsfaktor cos(phi): welcher";

        public const string TfSperrenGefordertJa = "E.8: TF-Sperren gefordert? - ja";
        public const string TfSperrenGefordertNein = "E.8: TF-Sperren gefordert? - nein";
        public const string TfSperrenEingebaut = "E.8: TF-Sperren eingebaut?";
        public const string TfSperrenPruefprotokoll = "E.8: TF-Sperren: Prüfprotokoll liegt vor?";

        public const string Inbetriebsetzungsdatum = "E.8: Datum der Inbetriebsetzung der EZA/des Speichers";

        public const string PaveFunktionstest = "E.8: PAV,E-Überwachung vorhanden: Funktionstest erfolgreich durchgeführt";
        public const string PaveWirkleistung = "E.8: PAV,E-Überwachung vorhanden: Eingestellte Wirkleistung PAV,E";

        public const string UnterschriftBetreiber = "E.8: Unterschrift Anlagenbetreiber";

        /// <summary>Schreibweise „Analgenerrichter“ wie in der Vorlage.</summary>
        public const string UnterschriftErrichter = "E.8: Unterschrift Analgenerrichter";
        public const string OrtDatum = "E.8: Ort, Datum";
    }

    /// <summary>
    /// Antrag auf Zählermontage – Netznutzung Strom (4-2003, Version 005/08.2023), ein Antrag je Zähler.
    /// Feldnamen ohne Hierarchie; Umlaute stehen in der Vorlage als <c>#C3#A4</c> o. ä. im Namen.
    /// </summary>
    public static class Zaehlerantrag
    {
        /// <summary>Radio, siehe <see cref="AuftragOption"/>.</summary>
        public const string Auftrag = "Auftrag";

        /// <summary>Zählernummer beim Auftrag Zählerausbau.</summary>
        public const string ZaehlernummerAusbau = "Z#C3#A4hlernummer Ausbau";

        /// <summary>Zählernummer beim Auftrag Zählerwechsel.</summary>
        public const string ZaehlernummerWechsel = "Auszuwechselnder Z#C3#A4hler";

        public const string Grundstueckseigentuemer = "Name des Grundst#C3#BCckseigent#C3#BCmers";
        public const string StandortStrasse = "Strasse und Hausnummer Z#C3#A4hlerstandort";
        public const string StandortPlzOrt = "PLZ und Ort Z#C3#A4hlerstandort";
        public const string LageVerbrauchsstelle = "Lage der Verbrauchsstelle";
        public const string Einbauort = "Einbauort des Z#C3#A4hlers";

        /// <summary>Schreibweise „Anschlussnutzerss“ wie in der Vorlage.</summary>
        public const string AnschlussnutzerName = "Name/Firmenname des Anschlussnutzerss";
        public const string AnschlussnutzerStrasse = "Adresse Anschlussnutzer";
        public const string AnschlussnutzerPlzOrt = "Postleitzahl/Wohnort Anschlussnutzer";
        public const string AnschlussnutzerEmail = "EMail-Adresse Anschlussnutzer";
        public const string AnschlussnutzerTelefon = "Telefonnummer Anschlussnutzer";

        public const string IntelligentesMesssystem = "Intelligentes Messsystem";

        /// <summary>Radio, siehe <see cref="ZaehlerOption"/>.</summary>
        public const string Zaehler = "Zaehler";
        public const string WandlerPrimaerstrom = "Prim#C3#A4rstrom";
        public const string WandlerSekundaerstrom = "Sekund#C3#A4rstrom";

        /// <summary>Radio Messebene, siehe <see cref="MessebeneOption"/>.</summary>
        public const string Messebene = "NSP";

        /// <summary>Leistungsbedarf in kW.</summary>
        public const string Leistungsbedarf = "MaxLeistung";
        public const string Jahresverbrauch = "Jahresverbrauch";

        /// <summary>Installierte Gesamtleistung vorhanden (kW).</summary>
        public const string GesamtleistungVorhanden = "WEmWW";

        /// <summary>Installierte Gesamtleistung neu (kW).</summary>
        public const string GesamtleistungNeu = "WEoWW";

        /// <summary>Absicherung im vorhandenen HAK 3x … A.</summary>
        public const string SicherungHak = "Sicherung HAK";

        /// <summary>Radio „Es handelt sich um“, siehe <see cref="MassnahmeOption"/>.</summary>
        public const string Massnahme = "WIB";

        /// <summary>Art der Maßnahme bei Baustrom/Veranstaltung.</summary>
        public const string ArtDerMassnahme = "Textfeld 1";

        /// <summary>Radio „Versorgung aus“: 1 Überflurverteiler, 2 Ortsnetzstation, 3 vorh. HAK.</summary>
        public const string VersorgungAus = "Ueberflurverteiler";

        public const string Messstellenbetreiber = "Name des Messstellenbetreibers";

        /// <summary>Radio „Nutzungsart / Lastprofil“, siehe <see cref="NutzungsartOption"/>.</summary>
        public const string Nutzungsart = "HH";

        /// <summary>Art des Gewerbes (Tooltip: maximale Entnahmeleistung).</summary>
        public const string ArtDesGewerbes = "LpLeistung";

        /// <summary>Maximale erzeugte Leistung in kW/kWp (Tooltip: Peakleistung der Anlage).</summary>
        public const string EegPeakleistung = "eegPeak";
        public const string Jahreseinspeisung = "Jahreserzeugung";

        /// <summary>Lage im Formular: „Bemerkungen des Installateurs“ (Name in der Vorlage irreführend).</summary>
        public const string BemerkungenInstallateur = "Stromlieferant";

        /// <summary>Lage im Formular: „Installateur“.</summary>
        public const string Installateur = "Stromlieferant_2";

        /// <summary>Lage im Formular: „Terminwunsch zur Inbetriebsetzung“.</summary>
        public const string Terminwunsch = "Stromlieferant_3";

        /// <summary>Lage im Formular: „Ort, Datum“ des Installateurs.</summary>
        public const string OrtDatumInstallateur = "MaxLeistung_2";

        /// <summary>Lage im Formular: „verantwortliche Fachkraft (Blockschrift)“.</summary>
        public const string VerantwortlicheFachkraft = "MaxLeistung_3";

        /// <summary>„Ort, Datum“ des Anschlussnutzers.</summary>
        public const string OrtDatumAnschlussnutzer = "Anutz";

        /// <summary>Export-Werte des Radios <see cref="Auftrag"/>.</summary>
        public static class AuftragOption
        {
            public const string FertigmeldungInbetriebsetzung = "1";
            public const string Zaehlereinbau = "2";
            public const string Zaehlerausbau = "3";
            public const string Zaehlerwechsel = "4";
        }

        /// <summary>Export-Werte des Radios <see cref="Zaehler"/> (gewünschte Messeinrichtung).</summary>
        public static class ZaehlerOption
        {
            public const string DreiPunktDrehstrom = "1";
            public const string DreiPunktZweirichtung = "2";
            public const string DreiPunktDoppeltarif = "3";
            public const string DreiPunktWechselstrom = "4";
            public const string Wandler = "5";
            public const string DreiPunktLastgang = "6";
            public const string EhzDrehstrom = "7";
            public const string EhzZweirichtung = "8";
        }

        /// <summary>Export-Werte des Radios <see cref="Messebene"/>.</summary>
        public static class MessebeneOption
        {
            public const string Niederspannung = "1";
            public const string Mittelspannung = "2";
        }

        /// <summary>Export-Werte des Radios <see cref="Massnahme"/> („Es handelt sich um“).</summary>
        public static class MassnahmeOption
        {
            public const string Wiederinbetriebnahme = "1";
            public const string Stilllegung = "2";
            public const string Neuanlage = "3";
            public const string BaustromVeranstaltung = "4";

            /// <summary>Tooltip der Vorlage: „Eine Anlage kann in Betrieb gehen“.</summary>
            public const string AnlageKannInBetriebGehen = "5";
            public const string Erweiterung = "6";
            public const string Zusammenlegung = "7";
        }

        /// <summary>Export-Werte des Radios <see cref="Nutzungsart"/>.</summary>
        public static class NutzungsartOption
        {
            public const string Haushalt = "1";
            public const string Gewerbe = "2";
            public const string Allgemein = "3";
            public const string Waermepumpe = "4";
            public const string Ladepunkt = "5";
            public const string Eigenerzeugung = "6";
        }
    }
}
