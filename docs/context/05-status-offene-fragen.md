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

### Code (Stand 06.11., 348 Tests grün)
- Grundgerüst: .NET 10, Postgres/EF Core, Hangfire, Docker, Serilog mit Maskierung personenbezogener Daten, Instanz-Konfiguration (`docs/konfiguration.md`)
- Prozesssteuerung komplett (`docs/konzept/prozesssteuerung.md`): alle Stufen von der Netzanmeldung bis MaStR, Ereignisse als Fakten, Modi DryRun/Review/Live, Versand-Ledger (nie doppelt), Watchdog, Blockiert/Storno, Admin-Endpunkte zum Simulieren von Ereignissen und zum Auflösen unklarer Versände
- HubSpot-Webhook (Signatur v3) und konfigurierbare Trigger-Regeln
- Reonic-Adapter: Hauptvariante, Katalog, Zählernummer, Checkliste, Backoff 2/4/8/16 s
- Netzbetreiber-Ermittlung per PLZ-Tabelle
- Netze-Duisburg-Adapter: Gerüst mit Validierung Netzanmeldung, Versandkanal Mail/Portal austauschbar

### Noch nicht umgesetzt (Platzhalter im Code)
| Baustein | Hängt an |
|---|---|
| HubSpot lesen (Projekt, Deal, Kontakt) | HubSpot-Struktur (Michael), Private-App-Token |
| HubSpot schreiben (Tasks, Notizen, Dateien, Status) | Freigabe der Schreibrechte |
| PDF-Formulare E.1–E.8 + Zählerantrag (PDFsharp) | Unterschrift als Bild, E.8 Symmetrie |
| Versand Netze Duisburg (Graph-Mail bzw. Portal-Klickstrecke) | Entscheidung Portal oder Mail |
| MaStR-Klickstrecke (Playwright) | Herkunft des MaStR-Logins |
| SharePoint-Datenblätter | Ordnerstruktur bzw. Entscheidung Reonic-Katalog |
| Alarm-Mail an Martin (Graph) | Graph-Zugang |
| Datenkarte zusätzlich in HubSpot ablegen | Schreibrechte |
| Prüfungen E.8 im Duisburg-Adapter | E.8 Symmetrie |

## In Arbeit (Martin, bis 09.11. bzw. 15.11.)
- [ ] **Netze Duisburg + MaStR bauen, Dry-Run auf Staging (bis 15.11.)**
- [ ] Code committen, Staging auf Railway bereitstellen, Demo für den 09.11. vorbereiten (Prozessablauf über Admin-Endpunkte + Datenkarte aus Reonic)
- [ ] HubSpot-App/Token anlegen: Staging im Entwickler-Testaccount, Produktion im reinsolar-Account (erst Lese-Scopes)
- [ ] Reonic-API gegen ein echtes Projekt testen (Requests in `reonic-checkliste-vorschlag.md`, Abschnitt 6.3)
- [ ] Lageplan: TIM-online-API prüfen
- [ ] E.8 Symmetrie und folgende Abschnitte klären
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
| **Woher kommt der MaStR-Login des Kunden?** (Webformular → HubSpot? Wer legt das Konto an?) | `IMastrCredentialStore` in HubSpot noch nicht umgesetzt |
| 25-kW-Grenze Netze Duisburg: bezogen auf kWp oder WR-Leistung? | Validierung im Duisburg-Adapter prüft vorerst max(kWp, WR-kW) |
| Zweige > 25 kW und „Rückfrage = Ja“ | State-Machine kennt Rückfrage bereits; Verfahren > 25 kW folgt |
| Fehlermeldung am Deal zusätzlich zum Status „blockiert“? | HubSpot-Property für Fehlertext |
| SharePoint-Ordner je Produkt vs. Datenblätter am HubSpot-Artikel vs. Reonic-Katalog | Datenblatt-Quelle als Adapter kapseln |
| Reonic: ausgefüllte Checklisten-Werte per API abrufbar? | Adapter gebaut; Test mit echten Daten steht aus. Ggf. PDF-Export + KI-Auslesen als Fallback |

## Nächster Termin
Montag, 09.11.2026, 15:00 (2 h) – letzter Termin vor Martins Urlaub. Ziel: Demo des Prozessablaufs (DryRun), offene Fragen entscheiden, Übergabe bis zum Go-live am 04.12. Realistisch für die Demo: Prozesssteuerung über Admin-Endpunkte + Datenkarte aus Reonic; ein echter Dry-Run aus einem HubSpot-Projekt erst, wenn HubSpot lesen umgesetzt ist.
