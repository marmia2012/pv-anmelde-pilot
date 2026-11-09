# 05 – Status, To-dos, offene Fragen (Stand 06.11.2026)

## Erledigt
- Kickoff (07.10.), Termin 2 (20.10.), Konzept-Review (26.10.) – Architektur abgestimmt
- Zugänge: HubSpot, SharePoint, RheinNetz, Westnetz, WebGo, Portal Netze Duisburg
- Reonic-API-Key (read-only) angelegt
- Hosting (Railway) und KI (OpenAI-API) entschieden
- MaStR einmal komplett durchgeklickt (03.11.)
- Skizze Reonic-Checkliste Duisburg für Michael (03.11.): `docs/konzept/reonic-checkliste.md` – 33 statt 71 Felder, Komponenten aus der Reonic-Hauptvariante + Katalog
- Vorschlag Kundenformular: `docs/konzept/reonic-checkliste-vorschlag.md`, Abschnitt 5
- Lageplan: Geobasis NRW WMS ALKIS als Quelle gefunden (dl-de/zero-2-0); TIM-online-API noch offen

### Code (Stand 09.11., 778 Tests grün)
- Grundgerüst: .NET 10, Postgres/EF Core, Hangfire, Docker, Serilog mit Maskierung personenbezogener Daten, Instanz-Konfiguration (`docs/konfiguration.md`)
- Prozesssteuerung komplett (`docs/konzept/prozesssteuerung.md`): alle Stufen von der Netzanmeldung bis MaStR, Ereignisse als Fakten, Modi DryRun/Review/Live, Versand-Ledger (nie doppelt), Watchdog, Blockiert/Storno, Admin-Endpunkte zum Simulieren von Ereignissen und zum Auflösen unklarer Versände
- HubSpot-Webhook (Signatur v3) und konfigurierbare Trigger-Regeln; Deal-Ereignisse (`deal.propertyChange`, Start/Storno über `dealstage`)
- HubSpot-Adapter auf Deal-Basis (`docs/konzept/hubspot-adapter.md`): Deal lesen inkl. Fallbacks Betreiber/Anlagenort, Errichter aus der Instanz, EVU-Vollmacht aus den Anhängen, Struktur-Endpunkt `GET /admin/hubspot/structure`; Schreiben (Aufgaben am Deal-Inhaber, Audit-Notizen nach Stufe, Dateien + Ablage-Notiz inkl. NAB-Datenkarte, Status-PATCH, MaStR-Login lesen) gebaut, aber hinter `HubSpot__WriteEnabled` (Default `false`); Idempotenz über Tabelle `crm_writes`
- Reonic-Adapter: Hauptvariante, Katalog, Zählernummer, Checkliste, Backoff 2/4/8/16 s
- Netzbetreiber-Ermittlung per PLZ-Tabelle
- Netze-Duisburg-Adapter: Formulare E.1, E.2, E.3 (mit Speicher), E.8 und Zählerantrag (je gewechseltem Zähler, sonst eine Fertigmeldung) werden aus der Datenkarte gefüllt (PDFsharp 6.1.1, Felder editierbar), Antragspaket je Einreichungsart inkl. Anhängen, Prüfung Netzanmeldung + Inbetriebnahme, Pflichtanhänge konfigurierbar (`RequiredAttachments`). Mapping: `docs/konzept/netze-duisburg-adapter.md`. Versandkanal Mail/Portal austauschbar (noch Platzhalter)

### Noch nicht umgesetzt (Platzhalter im Code)
| Baustein | Hängt an |
|---|---|
| HubSpot gegen das echte Portal (Property-Namen, Stage-IDs in `instance.json`) | Private-App-Token, Struktur-Endpunkt |
| HubSpot schreiben aktivieren (`HubSpot__WriteEnabled=true`) | Freigabe der Schreibrechte, Schreib-Scopes am Token |
| Formularwerte aus Datenblättern (Scheinleistung, Bemessungsstrom, nutzbare Kapazität, Verschiebungsfaktor, U>, Speicher-Phasen/Inselnetz) | Produktstamm bzw. KI-Auslesen der Datenblätter |
| Unterschrift in E.1, E.3, E.8, Zählerantrag | Unterschrift als Bild (Michael) |
| Errichter Straße/Telefon/E-Mail in E.3 und E.8 | `Errichter`-Optionen um Straße, Telefon, E-Mail erweitern (heute nur Firma, Ort, Eintragung) |
| E.8 ab Abschnitt „Symmetrie“ (Symmetrie, Blindleistungsverfahren, TF-Sperren, PAV,E) und Prüfungen E.8 im Duisburg-Adapter | Klärung Martin, Vorgaben aus der Einspeisezusage |
| Lageplan-Adapter (alle Netzbetreiber), Herstellerdokumente E.6/E.7 als Anhang | TIM-online/WMS ALKIS, SharePoint |
| Versand Netze Duisburg (Graph-Mail bzw. Portal-Klickstrecke) | Entscheidung Portal oder Mail |
| MaStR-Klickstrecke (Playwright) | Herkunft des MaStR-Logins |
| SharePoint-Datenblätter | Ordnerstruktur bzw. Entscheidung Reonic-Katalog |
| Alarm-Mail an Martin (Graph) | Graph-Zugang |
| Portal-Upload der Unterlagen Netze Duisburg | Entscheidung Portal oder Mail |
| Kundenadresse in den Duisburg-Formularen (Anschlussnehmer, abweichender Betreiber, Anschlussnutzer; heute Anlagenadresse) | Umstellung auf `NabDataCard.Betreiberadresse` (HubSpot-Adapter ist gemergt) |
| Wallbox > 4,2 kW → Mantelbogen Netze Duisburg | Klärung mit Michael |

## In Arbeit (Martin, bis 09.11. bzw. 15.11.)
- [ ] **Netze Duisburg + MaStR bauen, Dry-Run auf Staging (bis 15.11.)**
- [ ] Nach dem Merge des HubSpot-Adapters: Duisburg-Formulare (Anschlussnehmer/Anlagenbetreiber) auf `NabDataCard.Betreiberadresse` statt `Anlagenadresse` umstellen
- [ ] Code committen, Staging auf Railway bereitstellen, Demo für den 09.11. vorbereiten (Prozessablauf über Admin-Endpunkte + Datenkarte aus Reonic)
- [ ] HubSpot-App/Token anlegen: Staging im Entwickler-Testaccount, Produktion im reinsolar-Account (erst Lese-Scopes)
- [ ] Reonic-API gegen ein echtes Projekt testen (Requests in `reonic-checkliste-vorschlag.md`, Abschnitt 6.3)
- [ ] Lageplan: TIM-online-API prüfen
- [ ] E.8 Symmetrie und folgende Abschnitte klären
- [ ] Darstellung der Duisburg-PDFs vor Go-live in macOS-Vorschau, Chrome und HubSpot-Vorschau prüfen (Felder ohne `/AP`, `NeedAppearances`); ggf. Appearances mit eingebetteter Schrift erzeugen
- [ ] SharePoint-Ordnerstruktur je Produkt vorschlagen (oder Datenblätter aus dem Reonic-Katalog)
- [ ] Kontakt HubSpot-Consultant an Michael weiterleiten

## Offen bei reinsolar / Michael (bis 09.11.)
- [ ] Netze Duisburg: Portal Pflicht oder Mail weiter möglich?
- [ ] **Angebot/Auftragsbestätigung: in Reonic oder in HubSpot?** (siehe Tabelle unten)
- [ ] Reonic-Checkliste abnehmen (Empfehlung 4.1) und Felder in Reonic übertragen
- [ ] HubSpot-Struktur Projekte/Artikel festlegen
- [ ] HubSpot-Properties für Ereignisse und Freigaben je Stufe anlegen (bis 15.11.)
- [ ] Schreibrechte in HubSpot freigeben
- [ ] Martin als Member im OpenAI-Plattformzugang
- [ ] Unterschrift als Bild
- [ ] Annahmen Duisburg-Formulare mit Michael klären (`docs/konzept/netze-duisburg-adapter.md`, Abschnitt „Offen“): MK 3 für „Kaskade“ und Bedeutung „Kaskaden-Umbau“ · „Drehstrom“ bei dreiphasigem WR (E.2) · Zählerantrag: MK-8-Zusatzzähler, Messeinrichtung bei MK 1/MK 3, Ausbau als „Zusammenlegung“, „Erweiterung“ bei Bestandsanlage · Wallbox-Mantelbogen (> 4,2 kW) · Inselbetrieb = ja auch ohne Speicher/Notstrom (E.2) · E.8 „Drosselung 70 %“ vs. E.3 „60 %“ · Anhänge zur Inbetriebnahme
- [ ] Portal-Zugangsdaten in der Teams-Excel
- [ ] HubSpot-Vertrag/Lizenz

## Offene fachliche Fragen
| Frage | Auswirkung auf den Code |
|---|---|
| Antragsweg Netze Duisburg: Portal oder Mail? | Versandkanal des Duisburg-Adapters – austauschbar bauen |
| **Wo entsteht das Angebot / die AB – Reonic oder HubSpot?** Arbeitsannahme Martin (03.11.): In Reonic nur Planung und Datenaufnahme beim Kunden, das Angebot erstellt Michael in HubSpot. Widerspruch in den Notizen: Termin 3 spricht von „Artikel der AB (Reonic → HubSpot)“. Nachfragen: Wo wird die AB erzeugt? Wie kommen die Artikel an den HubSpot-Deal (manuell oder Sync)? Haben die HubSpot-Produkte technische Felder (Hersteller, Modell, Wp, W, kWh)? | Quelle aller Komponentendaten (Module, WR, Speicher, Wallbox) und Datenblätter: HubSpot-Line-Items vs. Reonic-Variante + Katalog; davon hängt ab, ob Komponenten doch in die Reonic-Checkliste müssen |
| ~~Wechselrichter: Liste in der Reonic-Checkliste oder Artikel der Auftragsbestätigung?~~ **Entschieden 03.11.:** Alle Daten aus Reonic, Komponenten aus der Reonic-Planungsvariante (Vorschlag 4.1), siehe `docs/konzept/reonic-checkliste.md`. Fallback 4.2: Auswahlliste in der Checkliste. | Reonic-Adapter mappt Hauptvariante + Katalog und Checklisten-Schlüssel auf die NAB-Datenkarte |
| Welche Daten braucht der Zählerwechsel? | Schritt Zählerwechsel noch nicht spezifiziert |
| **MaStR vor oder nach dem Zählerwechsel?** Konzept: danach. Die MaStR-Frist (1 Monat ab Inbetriebnahme) kann aber ablaufen, wenn der Zählerwechsel dauert. | Reihenfolge der Stufen in der State-Machine (`docs/konzept/prozesssteuerung.md`); Watchdog warnt nach 7 Tagen |
| **Wie erfährt die Automatisierung von Einspeisezusage, Rückfrage, Inbetriebnahme, Zählerwechsel und Storno?** Vorschlag bis das Postfach angebunden ist: je ein HubSpot-Property (Datum bzw. Auswahl), das Michael setzt. Freigabe im Modus Review: je Stufe ein eigenes Property, am besten als Datum statt Checkbox. | Michael legt die Properties an, wir tragen sie in `HubSpot:TriggerRules` ein |
| **Schreibrechte in HubSpot** (Tasks, Notizen, Dateien, Status-Properties) | Ohne sie kein Review-Modus und kein Go-live; bis dahin nur lesend |
| **Woher kommt der MaStR-Login des Kunden?** (Webformular → HubSpot? Wer legt das Konto an?) | `IMastrCredentialStore` liest `MastrBenutzername`/`MastrPasswort` vom Deal; Property-Namen fehlen noch |
| **MaStR-Passwort am Deal nicht löschen** (Abweichung vom Beschluss 26.10. auf Martins Wunsch) | Bestätigung durch Michael am 09.11.; bis dahin löscht der Adapter nicht |
| 25-kW-Grenze Netze Duisburg: bezogen auf kWp oder WR-Leistung? | Validierung im Duisburg-Adapter prüft vorerst max(kWp, WR-kW) |
| Zweige > 25 kW und „Rückfrage = Ja“ | State-Machine kennt Rückfrage bereits; Verfahren > 25 kW folgt |
| Fehlermeldung am Deal zusätzlich zum Status „blockiert“? | HubSpot-Property für Fehlertext |
| SharePoint-Ordner je Produkt vs. Datenblätter am HubSpot-Artikel vs. Reonic-Katalog | Datenblatt-Quelle als Adapter kapseln |
| Reonic: ausgefüllte Checklisten-Werte per API abrufbar? | Adapter gebaut; Test mit echten Daten steht aus. Ggf. PDF-Export + KI-Auslesen als Fallback |

## Nächster Termin
Montag, 09.11.2026, 15:00 (2 h) – letzter Termin vor Martins Urlaub. Ziel: Demo des Prozessablaufs (DryRun), offene Fragen entscheiden, Übergabe bis zum Go-live am 04.12. Realistisch für die Demo: Prozesssteuerung über Admin-Endpunkte + Datenkarte aus Reonic; ein echter Dry-Run aus einem HubSpot-Deal, sobald der Token da ist und Property-Namen und Stage-IDs in `instance.json` stehen (HubSpot lesen ist umgesetzt).
