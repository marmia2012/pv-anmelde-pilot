# Netze-Duisburg-Adapter: Unterlagen erzeugen

Stand: 06.11.2026 · intern. Code: `src/Netzbetreiber/PvAutopilot.Netzbetreiber.NetzeDuisburg/`. Plan: [docs/plans/2026-11-06-netze-duisburg-formulare.md](../plans/2026-11-06-netze-duisburg-formulare.md).

Der Adapter füllt die Formulare von Netze Duisburg aus der NAB-Datenkarte und stellt das Antragspaket zusammen. Der Versand (Mail oder Portal) ist davon getrennt: `Channels/Channels.cs`, noch Platzhalter.

## Bausteine

| Datei | Aufgabe |
|---|---|
| `Forms/Templates/*.pdf` | Vorlagen als eingebettete Ressourcen (`NetzeDuisburg.Templates.<Datei>`), kopiert aus `docs/context/netze-duisburg-formulare/` und in ASCII-Dateinamen umbenannt |
| `Forms/NetzeDuisburgTemplates.cs` | Formular → Vorlage/Dateiname |
| `Forms/NetzeDuisburgFormFields.cs` | **alle** Feldnamen je Formular (voll qualifiziert, exakt wie in der PDF) und die Export-Werte der Radio-Gruppen |
| `Forms/PdfFormFiller.cs` | Text/Checkbox/Radio setzen. Unbekannter Feldname, falscher Feldtyp oder unbekannte Radio-Option → `InvalidOperationException` (Vorlagenwechsel fällt sofort auf). Nicht flatten, `NeedAppearances = true`. Werte werden direkt als `/V`/`/AS` geschrieben, ohne Appearance-Rendering, damit PDFsharp im Container keine Schriften braucht; bei gesetzten Textfeldern wird `/AP` entfernt, damit der Viewer neu zeichnet. Texte über `/MaxLen` werden gekürzt und gezählt |
| `Forms/NetzeDuisburgFormGenerator.cs` | Mapping Datenkarte → Felder je Formular |
| `NetzeDuisburgAdapter.cs` | `Validate`, `PrepareAsync` (Paket je Einreichungsart), Option `RequiredAttachments` |

PDFsharp ist bewusst auf **6.1.1** (MIT) festgelegt, siehe [konfiguration.md](../konfiguration.md#netzbetreiber-netzbetreibercode).

## Paket je Einreichungsart

| Einreichung | Formulare | Anhänge aus der Datenkarte |
|---|---|---|
| Netzanmeldung | `E1_Antragstellung.pdf`, `E2_Datenblatt_Erzeugungsanlage.pdf`, `E3_Datenblatt_Speicher.pdf` (nur mit Speicher) | Lageplan, Schaltplan, Datenblatt, Einheitenzertifikat, NA-Schutz-Zertifikat (E.6), Prüfbericht NA-Schutz (E.7, `DocumentKind.NaSchutzPruefbericht`), EVU-Vollmacht – soweit vorhanden |
| Inbetriebnahme | `E8_Inbetriebsetzungsprotokoll.pdf`, `Zaehlerantrag_<n>.pdf` je gewechseltem Zähler, ohne Zählerwechsel genau eine Fertigmeldung | keine |
| Zählerwechsel | – (Zählerantrag geht mit dem E.8, keine eigene Einreichung) | – |

Nicht ausgefüllt und nicht eingebettet: Messkonzept-Auswahlblatt (Auswahl steht als Text in E.2 „Bemerkungen“), E.6 und E.7 (Herstellerdokumente, werden nur beigelegt).

Log: `Formular erzeugt: {Formular}, {GesetzteFelder} Felder gesetzt, {LeereFelder} Felder leer, {GekuerzteFelder} Felder gekürzt` je Formular, `Paket vorbereitet: {Einreichungsart}, {Formulare} Formulare, {Anhaenge} Anhänge`, Warnung `Optionaler Anhang fehlt: {DocumentKind}`. Nie Feldinhalte.

## Prüfung (`Validate`)

| Einreichung | Fachliches Problem (→ HubSpot-Task) |
|---|---|
| Netzanmeldung | max(kWp, WR-kW) ≥ 25 kW · keine Module · keine Wechselrichter · Hauptzählernummer fehlt · Pflichtanhang aus `RequiredAttachments` fehlt (Default EVU-Vollmacht, Übersichtsschaltplan) |
| Inbetriebnahme | Zählernummer fehlt bei Tausch, Ausbau, Kaskaden-Umbau · Zählerbauform fehlt bei Tausch, Neusetzung, Kaskaden-Umbau |

Optionale Anhänge (alle übrigen der Netzanmeldung: Lageplan und Herstellerdokumente haben noch keinen Adapter) → nur Warnung im Log.
Das **Inbetriebnahmedatum** steht nicht in der Datenkarte, sondern am Vorgang. `Validate` bekommt nur die Datenkarte; die Prozesssteuerung startet die Stufe erst mit bekanntem und erreichtem Datum (`InbetriebnahmeReady`). Fehlt es trotzdem, wirft `PrepareAsync` (technischer Fehler).

## Gemeinsame Werte

- **Name** = Vorname + Nachname des Anlagenbetreibers · **Adresse** = Anlagenadresse (Straße + Hausnummer; PLZ + Ort).
- Die Datenkarte kennt nur Anlagenbetreiber und Anlagenadresse. Anschlussnehmer/Eigentümer, abweichender Betreiber, Grundstückseigentümer und Anschlussnutzer bekommen deshalb vorerst dieselben Werte.
- Zahlen deutsch formatiert ohne Einheit (`0.##`, Einheit steht im Formular), Datum `dd.MM.yyyy`.
- Keine Unterschrift in diesem Feature.

## E.1 Antragstellung

| Feld | Quelle / Festwert |
|---|---|
| Anlagenanschrift: Vorname, Name / Straße, Hausnummer / PLZ, Ort | Betreiber-Name, Anlagenadresse |
| Anlagenanschrift: Telefon, E-Mail | Betreiber Telefon, E-Mail („Telefon, E-Mail“) |
| Anschlussnehmer/Eigentümer: alle vier Felder | wie Anlagenanschrift |
| Anlagenbetreiber: alle vier Felder | wie Anlagenanschrift |
| Anlagenerrichter: Firma / Ort / Eintragungsnummer bei Netzbetreiber | `Errichter.Firma` / `.Ort` / `.Eintragungsnummer` |
| Anlagenart: Neuerrichtung bzw. Erweiterung | `Installationsart` |
| Lageplan beigefügt | nur wenn Lageplan in der Datenkarte |
| Datenblatt für EZA beigefügt | immer (E.2 wird erzeugt) |
| Einheitenzertifikate liegen vor | nur wenn Einheitenzertifikat in der Datenkarte |
| Zertifikat für NA-Schutz beigefügt | nur wenn NA-Schutz-Zertifikat in der Datenkarte |
| Übersichtsschaltplan ab NAP beigefügt | nur wenn Schaltplan in der Datenkarte |
| Geplanter Inbetriebsetzungstermin | `Installationstermin` |
| Datum, Ort | „Ort der Anlage, Antragsdatum“: Eintritt der aktuellen Revision in `NetzbetreiberResolved` (Europe/Berlin), stabil bei erneutem Erzeugen, siehe „Nachträge und erneutes Erzeugen“ |

Bewusst leer: Anlagenart Rückbau · Anmeldevordruck „Anmeldung zum Netzanschluss“ (nicht Teil des Pakets) · Zertifikat Leistungsflussüberwachung (nicht erforderlich) · Unterschrift des Anschlussnehmers.

## E.2 Datenblatt Erzeugungsanlage

| Feld | Quelle / Festwert |
|---|---|
| Anlagenanschrift (3 Felder) | Betreiber-Name, Anlagenadresse |
| Energieart: Sonne | ja |
| Erzeugungseinheiten: Hersteller / Typ / Anzahl baugleicher Einheiten | **erster** Wechselrichter: Hersteller / Modell / Anzahl |
| EZA: Max. Wirkleistung (kW) | Summe WR-Leistung (`WechselrichterLeistungKw`) |
| Netzeinspeisung | nur wenn alle WR dieselbe Phasenzahl aus dem Katalog haben: 1 → 1-phasig, 2 → 2-phasig, 3 → **Drehstrom** (dreiphasiger Umrichter; „3-phasig“ steht für drei einphasige Einheiten). Sonst leer |
| Inselbetrieb vorgesehen? | ja (Dataflow) |
| Motorischer Anlauf vorgesehen? | nein (Dataflow) |
| Überschusseinspeisung? / Volleinspeisung? | aus `Einspeiseart`: Teileinspeisung → Überschuss ja, Voll nein; Volleinspeisung → umgekehrt |
| Blindleistungskompensationsanlage: nicht vorhanden | ja (Dataflow „Blindleistung = nicht vorhanden“) |
| Bemerkungen (1) | Messkonzept, z. B. „Messkonzept MK 2 gem. Netze-Duisburg-Messkonzepte 2026“ |
| Bemerkungen (2)–(4) | weitere WR-Typen „Weitere Erzeugungseinheit: Hersteller Modell, Anzahl n“; was nicht passt, steht mit „; “ in Zeile 4 |

Messkonzept-Zuordnung (neutraler Wert der Datenkarte → Netze-Duisburg-Katalog 2026):

| Datenkarte (`NabDataCard.Messkonzept`) | Netze Duisburg |
|---|---|
| Volleinspeisung – Erzeugungszähler | MK 1 |
| Überschusseinspeisung – Zweirichtungszähler | MK 2 |
| Kaskade – Zweirichtungs- + Erzeugungszähler | MK 3 (Annahme: Erzeugungszähler Z2 für den Selbstverbrauch) |
| Überschuss + separater Zähler steuerbare Verbrauchseinrichtung (§ 14a) | MK 8 |
| Sonstiges (siehe Bemerkung) | „Messkonzept: <Bemerkung>“ |
| unbekannter Wert | „Messkonzept: <Wert>“ |

Bewusst leer: Max. Scheinleistung (Datenblatt) · Energieart Wind/Wasser/Sonstige · BHKW-Felder · Blindleistungskompensation „vorhanden mit“, Stufen, kleinste Stufe, Verdrosselungsgrad.

## E.3 Datenblatt Speicher (nur mit Speicher)

| Feld | Quelle / Festwert |
|---|---|
| Anlagenanschrift (3 Felder) | Betreiber-Name, Anlagenadresse |
| Anlagenerrichter: Firma, Ort | `Errichter.Firma`, `Errichter.Ort` |
| Speichersystem: Hersteller/Typ / Anzahl | `Speicher.Hersteller Speicher.Modell` / 1 (die Datenkarte kennt genau einen Speicher) |
| AC-gekoppelt / DC-gekoppelt | `Speicher.Kopplung`, nur wenn bekannt |
| Allpolige Trennung vom Netz bei Netzersatzbetrieb | ja (Dataflow) |
| NA-Schutz nach VDE-AR-N 4105 vorhanden | ja (Dataflow) |
| Anschlusskonzept: Nummer der Abbildung | 8 (Dataflow) |
| Übersichtsschaltplan beigefügt | nur wenn Schaltplan in der Datenkarte |
| Verwendete Primärenergieträger | Sonne |
| Energie des Speichersystems wird nicht vom Netz bezogen … | ja (Dataflow) |
| Nachweise: Einheitenzertifikate liegen vor | nur wenn Einheitenzertifikat in der Datenkarte |
| Einspeisemanagement: Ferngesteuert | nein (Dataflow) |
| dauerhaft auf % begrenzt / ja | 60 / ja (Dataflow) |
| Nachweis Errichter: Ausweis-Nr / bei Netzbetreiber | `Errichter.Eintragungsnummer` / `Errichter.EingetragenBeiNetzbetreiber` |

Bewusst leer: Errichter Straße/Telefon (Errichter-Konfiguration kommt mit dem HubSpot-Branch) · Inselnetz bildendes System (je Produkt, Datenblatt) · Wechselstrom L1/L2/L3 bzw. Drehstrom (Dataflow „AC → Wechselstrom, DC → Drehstrom“ braucht die Phasen des Speicher-Umrichters; nicht im Plan, offen) · nutzbare Speicherkapazität · Umrichter Hersteller/Typ, Anzahl, Verschiebungsfaktor, Scheinleistung, Wirkleistung, Bemessungsstrom (Datenblatt) · Primärenergieträger getrennt erfasst, Einspeisevergütungen korrekt erfasst (nicht im Dataflow) · Ferngesteuert ja · Bemerkungen · Unterschrift Errichter, Ort/Datum.

## E.8 Inbetriebsetzungsprotokoll

| Feld | Quelle / Festwert |
|---|---|
| Anlagenanschrift (3 Felder) | Betreiber-Name, Anlagenadresse |
| Anlagenerrichter: Firma, Ort | `Errichter.Firma`, `Errichter.Ort` |
| Max. Wirkleistung (kW) | Summe WR-Leistung, wie in E.2 (Übereinstimmung E.2/E.8) |
| Modulleistung (kWp) | `GesamtleistungKwp` (Dataflow „installierte kWp“) |
| Übereinstimmung von E.2 und E.3 mit Anlagenaufbau | ja |
| Abrechnungsmessung: Vorinbetriebsetzung & Inbetriebsetzungsprüfung | ja |
| Einheitenzertifikate vorhanden? | ja |
| Zertifikat für Leistungsflussüberwachung am NAP | nein (Dataflow „Leistungsüberwachung = Nein“) |
| Zertifikat für NA-Schutz vorhanden | ja |
| Drosselung auf 70 % im Umrichter | ja (Dataflow „Drosselung = Ja“) |
| Energieflussrichtungssensor – Funktionstest bestanden | ja |
| Datum der Inbetriebsetzung | `AnmeldeProcess.Inbetriebnahmedatum` |

Bewusst leer:
- Max. Scheinleistung (Datenblatt bzw. „aus Anfrage“, kommt mit den Datenblattwerten).
- Errichter Straße/Telefon (siehe E.3).
- Integrierter NA-Schutz: Dataflow „ja“, das Formular fragt aber nur den eingestellten Wert U> ab (Datenblatt/Einstellung) → leer.
- Zentraler NA-Schutz (Wert U>, Auslösetest, Ruhestromprinzip) – integrierter NA-Schutz.
- Zertifizierte technische Steuerung 70 %, fernsteuerbare Leistungsreduzierung (E.3: ferngesteuert nein).
- **Ab Abschnitt „Symmetrie“ (offen, Martin klärt):** Symmetriebedingung (dreiphasiger Umrichter, Aufteilung L1–L3 vorhanden/neu, Symmetrieeinrichtung), Blindleistungsverfahren (Q(U), cos φ(P), fester cos φ + Wert), TF-Sperren (gefordert ja/nein, eingebaut, Prüfprotokoll), PAV,E-Überwachung (Funktionstest, eingestellte Wirkleistung).
- Unterschrift Anlagenbetreiber, Unterschrift Anlagenerrichter, Ort/Datum.

## Zählerantrag (Antrag auf Zählermontage – Netznutzung Strom)

Ein Antrag je **gewechseltem** Zähler der Datenkarte (Tausch, Ausbau, Neusetzung, Kaskaden-Umbau; Vorgabe im Formular). Zähler ohne Wechsel bekommen keinen eigenen Antrag. Wird gar kein Zähler gewechselt, entsteht genau **eine Fertigmeldung/Inbetriebsetzung** für die Anlage (`Zaehlerantrag_1.pdf`). Feldnamen der Vorlage sind teils irreführend (`Stromlieferant_2` = Installateur); die Lage im Formular steht in `NetzeDuisburgFormFields.Zaehlerantrag`. Radio-Export-Werte über Tooltips und Lage der Widgets ermittelt.

| Feld | Quelle / Festwert |
|---|---|
| Auftrag (Radio) | aus `Zaehler.Wechseltyp`: Tausch Zweirichtungszähler → 4 Zählerwechsel · Kaskaden-Umbau → 4 Zählerwechsel (Annahme, kein eigener Auftrag) · Ausbau → 3 Zählerausbau · Neusetzung → 2 Zählereinbau · kein Zähler gewechselt → 1 Fertigmeldung/Inbetriebsetzung |
| Auszuwechselnder Zähler / Zählernummer Ausbau | `Zaehlernummer` beim Zählerwechsel bzw. Ausbau |
| Bemerkungen des Installateurs (`Stromlieferant`) | nur bei der Fertigmeldung: „Hauptzähler <Hauptzählernummer>“ (das Formular hat dort kein Feld für die Zählernummer) |
| Grundstückseigentümer | Betreiber-Name |
| Zählerstandort Straße / PLZ, Ort | Anlagenadresse |
| Lage der Verbrauchsstelle | `LageVerbrauchsstelle`, `BezeichnungVerbrauchsstelle` (Fertigmeldung: leer) |
| Einbauort | `Einbauort` (Fertigmeldung: leer) |
| Anschlussnutzer Name / Adresse / PLZ, Ort / E-Mail / Telefon | Betreiber-Name, Anlagenadresse, Betreiber E-Mail, Telefon |
| Gewünschte Messeinrichtung (Radio `Zaehler`) | nur wenn ein Zähler gesetzt wird (Tausch, Neusetzung, Kaskade): 3-Punkt → 2 Zweirichtungszähler 3-Punkt · eHZ → 8 eHZ Zweirichtungszähler. **Nicht** beim MK-8-Zusatzzähler (siehe unten) |
| Messebene (Radio `NSP`) | 1 Niederspannung |
| Absicherung im vorhandenen HAK 3x … A | `NhSicherungAmpere` (Default 63) |
| Es handelt sich um (Radio `WIB`) | Ausbau → 7 Zusammenlegung · sonst bei `Installationsart.Erweiterung` → 6 Erweiterung · sonst 5 „Eine Anlage kann in Betrieb gehen“ |
| Nutzungsart (Radio `HH`) | 6 Eigenerzeugung – nicht beim Ausbau und nicht beim MK-8-Zusatzzähler |
| maximale erzeugte Leistung kW/kWp (`eegPeak`) | `GesamtleistungKwp` – nicht beim Ausbau und nicht beim MK-8-Zusatzzähler |

**MK 8** (Messkonzept „Überschuss + separater Zähler steuerbare Verbrauchseinrichtung (§ 14a)“): Der neu gesetzte Zähler (`Neusetzung`) gilt als Zusatzzähler der steuerbaren Verbrauchseinrichtung. Messeinrichtung, Nutzungsart und Peakleistung bleiben dort leer, bis mit Michael geklärt ist, was einzutragen ist (z. B. Drehstromzähler, Wärmepumpe/Ladepunkt).

**Zu bestätigen mit Michael:** MK-8-Zusatzzähler (Erkennung über `Neusetzung` und Werte) · MK 1 (Volleinspeisung) und MK 3 (Kaskade): welche Messeinrichtung (Erzeugungs- bzw. Einrichtungszähler statt Zweirichtungszähler)? · Ausbau als „Zusammenlegung“ · „Erweiterung“ bei Erweiterung einer Bestandsanlage · Fertigmeldung mit Hauptzählernummer in den Bemerkungen.

Bewusst leer: intelligentes Messsystem · Wandlergröße (die Vorlage hat „5“ als Sekundärstrom vorbelegt) · Leistungsbedarf, Jahresverbrauch · installierte Gesamtleistung vorhanden/neu · Versorgung aus, Art der Maßnahme, Messstellenbetreiber · Art des Gewerbes · erwartete Jahreseinspeisung · Bemerkungen des Installateurs (außer Fertigmeldung), Installateur, Terminwunsch · Ort/Datum, verantwortliche Fachkraft, Unterschriften.

Hinweis: Die Feldschrift (`/DA`) einiger Felder im Zählerantrag verweist auf eine Schrift, die die Vorlage nicht mitliefert. Viewer, die wegen `NeedAppearances` neu zeichnen, nehmen eine Ersatzschrift (xpdf meldet „Unknown font in field's DA string“). Darstellung vor Go-live prüfen (siehe „Offen“).

## Nachträge und erneutes Erzeugen

- Fehlende oder falsche Werte werden **in der Datenkarte** nachgetragen (HubSpot bzw. Reonic), nicht im PDF. Die Felder bleiben zwar editierbar, aber jedes erneute Erzeugen (Retry, Wechsel DryRun → Review, Neueinreichung) baut die Formulare aus der Datenkarte neu und überschreibt Änderungen im PDF.
- Damit ein neu erzeugtes Formular dem archivierten gleicht, ist das E.1-Datum stabil: Eintritt der aktuellen Revision in `NetzbetreiberResolved` (ein Resume nach Blockade zählt nicht), ohne solchen Übergang der Anlagezeitpunkt des Vorgangs. Die anderen Formulare enthalten kein Erzeugungsdatum (E.8: Inbetriebnahmedatum des Vorgangs).

## Offen

- Datenblattwerte (Scheinleistung, Bemessungsstrom, nutzbare Kapazität, Verschiebungsfaktor, U>, AC/DC-Kopplung und Phasen des Speichers, Inselnetzfähigkeit) – Produktstamm/KI.
- Unterschrift (Bild von Michael) für E.1, E.3, E.8, Zählerantrag.
- Errichter Straße/Telefon/E-Mail (E.3, E.8) – kommt mit der Errichter-Konfiguration.
- **Kundenadresse in der Datenkarte** (nach dem Merge des HubSpot-Branchs): Anschlussnehmer, abweichender Betreiber, Anschlussnutzer und Grundstückseigentümer nutzen bis dahin die Anlagenadresse.
- E.8 ab „Symmetrie“, Vorgaben aus der Einspeisezusage (Blindleistungsverfahren, TF-Sperren).
- **Versand nach Freigabe:** ob die archivierten (geprüften) Dokumente oder neu erzeugte gesendet werden, entscheidet das Versandkanal-Feature.
- **Darstellung vor Go-live prüfen** in macOS-Vorschau, Chrome und der HubSpot-Vorschau (Felder ohne `/AP`, Darstellung über `NeedAppearances`); ggf. Appearances mit eingebetteter Schrift erzeugen.
- Gekürzte Texte (`/MaxLen`, z. B. Zählernummer 14 Zeichen): Anzahl steht im Log (`GekuerzteFelder`); ggf. als fachliches Problem melden.
- **Mit Michael klären:**
  - Messkonzept MK 3 für „Kaskade“ und „Drehstrom“ für dreiphasige WR (E.2).
  - Bedeutung „Kaskaden-Umbau“ (heute: Zählerwechsel mit Zweirichtungszähler).
  - Zählerantrag: MK 8, MK 1/MK 3, Ausbau/Erweiterung (siehe Abschnitt Zählerantrag); Anhänge zur Inbetriebnahme (z. B. Installateurausweis, wenn nicht bei ND eingetragen).
  - Wallbox > 4,2 kW Ladeleistung → Mantelbogen (separater Zähler, MK 2, Fachplaner Michael, Unterschrift) – noch nicht umgesetzt.
  - E.2 „Inselbetrieb vorgesehen = ja“ (Dataflow) auch bei Anlagen ohne Speicher bzw. Notstrom?
  - E.8 „Drosselung auf 70 % im Umrichter“ vs. E.3 dauerhafte Begrenzung auf 60 %.
- Portal-Upload / Mailversand, Lageplan-Adapter.
