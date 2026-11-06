# Plan: Netze Duisburg – Unterlagen erzeugen

Stand 06.11.2026 · Branch `feature/netze-duisburg-forms` (eigener Worktree, parallel zu `feature/hubspot-deal-adapter`)

## Ziel

Der Netze-Duisburg-Adapter füllt die ausfüllbaren Formulare der Netzanmeldung (E.1, E.2, E.3) und der Inbetriebnahme (E.8, Zählerantrag) mit allen Daten, die ohne Datenblätter vorliegen, und stellt das Antragspaket inkl. Anhängen zusammen. Der Versand (Portal oder Mail) ist nicht Teil dieses Features.

## Festgelegt (Klärung 06.11.2026)

| Thema | Entscheidung |
|---|---|
| Umfang | Formulare erzeugen/ausfüllen + Paket zusammenstellen. **Portal-Upload später** als eigenes Feature, Versandkanäle bleiben Platzhalter |
| Netzanmeldung | E.1, E.2, E.3 (nur mit Speicher) |
| Inbetriebnahme | E.8 + Zählerantrag (Zählerantrag geht mit dem E.8). E.8-Vorlage legt Martin in `docs/context/netze-duisburg-formulare/`; offene Abschnitte ab „Symmetrie“ bleiben leer |
| E.6 / E.7 | Nur beilegen (Herstellerdokumente), nicht ausfüllen |
| Datenblattwerte | Scheinleistung, AC/DC-Kopplung (falls nicht im Katalog), Bemessungsstrom, nutzbare Kapazität, Verschiebungsfaktor: **leer lassen**, offenes To-do für später |
| Unterschrift | Keine Unterschrift in diesem Feature |
| Messkonzept | Als Text in **E.2 „Bemerkungen“** (z. B. „Messkonzept MK 2 gem. Netze-Duisburg-Messkonzepte 2026“) |
| Lageplan | Eigener Lageplan-Adapter kommt später für alle Netzbetreiber. Duisburg nimmt einen Lageplan aus der Datenkarte, wenn vorhanden; E.1-Checkbox „Lageplan beigefügt“ nur dann |
| Formularfelder | Bleiben editierbar (nicht flatten), damit im Review fehlende Werte nachgetragen werden können |

## Bezug zu Konzepten und Beschlüssen

- CLAUDE.md / `02-architektur.md`: Ein Netzbetreiber = ein Adapter; gemeinsame Formular-/PDF-Erzeugung, austauschbarer Versandkanal. Wird eingehalten: Erzeugung im Adapter, Kanal unverändert.
- `04-dataflow-feldmapping.md`, Feldmapping Netze Duisburg: Werte für E.2/E.3/E.8 (Inselbetrieb, motorischer Anlauf, Anschlusskonzept 8, Einspeisemanagement „ferngesteuert nein, 60 %, ja“ usw.) werden so übernommen.
- Termin 2 (20.10.): Blindleistung, E.7, Inselnetz „Trial and Error“ → Blindleistungskompensation „nicht vorhanden“ laut Dataflow, Rest leer.
- `prozesssteuerung.md`: Zählerantrag ist keine eigene Einreichung (`Einreichungen` = Netzanmeldung + Inbetriebnahme), bleibt so.
- Harte Regeln: keine PII in Logs (Formularinhalte nie loggen), DryRun-Default unverändert.

## Schritte (Umsetzungsreihenfolge)

### 1. Vorlagen und PDF-Basis
- Vorlagen (E.1, E.2, E.3, Zählerantrag, später E.8) als eingebettete Ressourcen in `src/Netzbetreiber/PvAutopilot.Netzbetreiber.NetzeDuisburg/Forms/Templates/`. Quelle `docs/context/netze-duisburg-formulare/` (öffentliche Formulare von Netze Duisburg).
- **PDFsharp auf 6.1.1 (MIT) zurücksetzen** (Entscheidung Martin 06.11.): 6.5.0 wirft beim Öffnen „The loaded license is not valid“. Lizenzmodell ab 6.5 wird separat geklärt, bevor wieder aktualisiert wird.
- Kleine, wiederverwendbare Ausfüll-Hilfe (`PdfFormFiller`): Textfeld setzen, Checkbox/Radio setzen, unbekannter Feldname = Fehler (Vorlagenwechsel fällt sofort auf), `NeedAppearances` setzen, nicht flatten. Liegt im Duisburg-Projekt; Umzug in ein gemeinsames Projekt, sobald ein zweiter Netzbetreiber es braucht.
- Feldnamen zentral je Formular (`NetzeDuisburgFormFields`), analog zu den Selektoren.
- Logging: Formular erzeugt (Formularname, Anzahl gesetzter/leerer Felder), nie Feldinhalte.

### 2. E.1 Antragstellung
- Anlagenanschrift (Betreiber-Name + Anlagenadresse, Telefon/E-Mail), Anschlussnehmer/Eigentümer und Anlagenbetreiber (Betreiber; abweichender Betreiber nutzt vorerst die Anlagenadresse, weil die Datenkarte keine Kundenadresse hat – Kundenadresse folgt nach dem HubSpot-Merge), Errichter (Firma, Ort, Eintragungsnummer), Anlagenart (Neuerrichtung/Erweiterung), Beilagen-Checkboxen nach tatsächlich vorhandenen Anhängen, geplanter Inbetriebsetzungstermin (Installationstermin), Datum/Ort. Unterschrift leer.

### 3. E.2 Datenblatt Erzeugungsanlage
- Anlagenanschrift, Energieart Sonne, Erzeugungseinheiten = Wechselrichter (Hersteller, Typ, Anzahl; mehrere WR-Typen → erster in die Felder, weitere in Bemerkungen), max. Wirkleistung = Summe WR-Leistung, Netzeinspeisung nach Phasen (falls im Katalog bekannt, sonst leer), Betriebsweise laut Dataflow (Inselbetrieb, motorischer Anlauf nein), Überschuss-/Volleinspeisung aus Einspeiseart, Blindleistungskompensation „nicht vorhanden“, Messkonzept in Bemerkungen. Scheinleistung leer.

### 4. E.3 Datenblatt Speicher (nur mit Speicher)
- Anlagenanschrift, Errichter (Firma/Ort; Straße/Telefon leer bis Errichter-Konfiguration erweitert ist), Speicher Hersteller/Typ + Anzahl, AC/DC-Kopplung wenn bekannt, NA-Schutz vorhanden ja, allpolige Trennung ja, Anschlusskonzept 8, Übersichtsschaltplan beigefügt (wenn Schaltplan vorhanden), Primärenergieträger Sonne, Speicher bezieht nicht vom Netz ja, Einheitenzertifikate (wenn vorhanden), Einspeisemanagement ferngesteuert nein / dauerhaft 60 % ja, Nachweis Errichter. Umrichter- und Kapazitätswerte leer.

### 5. Zählerantrag + E.8 (Inbetriebnahme)
- Zählerantrag: Auftrag Inbetriebsetzung bzw. Zählerwechsel je Zähler der Datenkarte, Zählernummern, Grundstückseigentümer (Kunde), Zählerstandort (Anlagenadresse), Anschlussnutzer (Betreiber, Kontakt), Zählerart aus Zählerbauform (3-Punkt/eHZ, Zweirichtung), Niederspannung, EEG-Peakleistung = kWp, Kundengruppe Eigenerzeugung, „Eine Anlage kann in Betrieb gehen“, Sicherung HAK = NH-Sicherung.
- E.8: sobald die Vorlage im Ordner liegt; Werte laut Dataflow (installierte kWp, Übereinstimmung E.2/E.3 ja, Abrechnungsmessung ja, Einheitenzertifikat ja, Leistungsüberwachung nein, NA-Schutz-Zertifikat ja, integrierter NA-Schutz ja, Drosselung ja, Energieflussrichtungssensor ja); Abschnitte ab Symmetrie und Scheinleistung leer. Fehlt die Vorlage beim Bau, wird E.8 als letzter Schritt nachgezogen.

### 6. Paket und Prüfung
- `PrepareAsync` erzeugt je Einreichungsart die Formulare und hängt die Dokumente der Datenkarte an (Schaltplan, Datenblätter, Einheitenzertifikat, NA-Schutz-Zertifikat/E.6, E.7, EVU-Vollmacht, Lageplan).
- `Validate` für Inbetriebnahme ergänzen (Inbetriebnahmedatum, Zählerangaben); bestehende Netzanmeldungs-Prüfung bleibt (25-kW-Grenze streng).
- Neue Option `Netzbetreiber:NetzeDuisburg:RequiredAttachments` (Default: EVU-Vollmacht, Schaltplan): fehlt ein Pflichtanhang → fachliches Problem; fehlende optionale Anhänge (Lageplan, Herstellerdokumente – Adapter dafür fehlen noch) → Warnung im Log, kein Abbruch.
- Logging: Paket vorbereitet (Einreichungsart, Anzahl Formulare/Anhänge, fehlende optionale Anhänge nach Art).

### 7. Doku
- `docs/konfiguration.md` (RequiredAttachments, ggf. PDFsharp-Lizenz), neues `docs/konzept/netze-duisburg-adapter.md` (Formular-Mapping je Feld, Quellen, leere Felder und warum), `05-status-offene-fragen.md` (offene To-dos: Datenblattwerte, Unterschrift, Errichter-Adresse, Portal-Upload, Lageplan-Adapter).

## Tests

| Schritt | Abgesichertes Verhalten |
|---|---|
| 1 | Unbekannter Feldname wirft; Text/Checkbox/Radio gesetzt und wieder auslesbar; Felder bleiben editierbar |
| 2–4 | Je Formular: Pflichtwerte aus der Datenkarte in den richtigen Feldern; Neuerrichtung vs. Erweiterung; Teil- vs. Volleinspeisung; Messkonzept in Bemerkungen; mehrere WR-Typen; Beilagen-Checkboxen nur bei vorhandenem Anhang; E.3 nur mit Speicher; Datenblattfelder bleiben leer |
| 5 | Zählerantrag je Zählertyp/Bauform; E.8-Festwerte; Symmetrie-Abschnitt leer |
| 6 | Paketinhalt je Einreichungsart; Pflichtanhang fehlt → Problem; optionaler fehlt → kein Problem; keine Formularinhalte im Log |

Testdaten nur synthetisch (Fake-Datenkarte), Formulare werden nach dem Füllen mit PDFsharp wieder eingelesen und geprüft.

## Annahmen und offene Punkte

- Paralleler HubSpot-Branch ändert `CrmProjectData`, `NabDataCardAggregator` und führt `ErrichterOptions` ein. Dieser Branch ändert die Datenkarte möglichst nicht; Merge-Konflikte löst der zweite PR.
- E.8-Vorlage fehlt noch (Martin).
- Datenblattwerte, Unterschrift, Errichter-Straße/Telefon, Portal-Upload und Lageplan-Adapter sind Folge-Features.
- Mehrere Wechselrichter-Typen: E.2 hat nur eine Zeile; Annahme „erster Typ in die Felder, Rest in Bemerkungen“.
