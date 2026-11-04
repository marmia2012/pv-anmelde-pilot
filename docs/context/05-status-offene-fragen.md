# 05 – Status, To-dos, offene Fragen (Stand 03.11.2026)

## Erledigt
- Kickoff (07.10.), Termin 2 (20.10.), Konzept-Review (26.10.) – Architektur abgestimmt
- Zugänge: HubSpot, SharePoint, RheinNetz, Westnetz, WebGo, Portal Netze Duisburg
- Reonic-API-Key (read-only) angelegt
- Hosting (Railway) und KI (OpenAI-API) entschieden
- MaStR einmal komplett durchgeklickt (03.11.)

## In Arbeit (Martin, bis 09.11. bzw. 15.11.)
- [ ] **Netze Duisburg + MaStR bauen, Dry-Run auf Staging (bis 15.11.)**
- [ ] Skizze Reonic-Checkliste Duisburg + Vorschlag Kundenformular (blockiert Michaels Reonic-Arbeit)
- [ ] Reonic-API testen: kommen ausgefüllte Checklisten-Werte strukturiert?
- [ ] Lageplan-Schnittstelle prüfen (TIM-online / WMS Geobasis NRW)
- [ ] E.8 Symmetrie und folgende Abschnitte klären
- [ ] SharePoint-Ordnerstruktur je Produkt vorschlagen

## Offen bei reinsolar / Michael (bis 09.11.)
- [ ] Netze Duisburg: Portal Pflicht oder Mail weiter möglich?
- [ ] **Angebot/Auftragsbestätigung: in Reonic oder in HubSpot?** (im Call 09.11. final klären, siehe Tabelle unten)
- [ ] HubSpot-Struktur Projekte/Artikel festlegen
- [ ] Reonic-Felder nach Martins Skizze übertragen
- [ ] Martin als Member im OpenAI-Plattformzugang
- [ ] Unterschrift als Bild
- [ ] Portal-Zugangsdaten in der Teams-Excel
- [ ] HubSpot-Vertrag/Lizenz

## Offene fachliche Fragen
| Frage | Auswirkung auf den Code |
|---|---|
| Antragsweg Netze Duisburg: Portal oder Mail? | Versandkanal des Duisburg-Adapters – austauschbar bauen |
| **Wo entsteht das Angebot / die AB – Reonic oder HubSpot?** Arbeitsannahme Martin (03.11.): In Reonic nur Planung und Datenaufnahme beim Kunden, das Angebot erstellt Michael in HubSpot. Widerspruch in den Notizen: Termin 3 spricht von „Artikel der AB (Reonic → HubSpot)“. Nachfragen: Wo wird die AB erzeugt? Wie kommen die Artikel an den HubSpot-Deal (manuell oder Sync)? Haben die HubSpot-Produkte technische Felder (Hersteller, Modell, Wp, W, kWh)? | Quelle aller Komponentendaten (Module, WR, Speicher, Wallbox) und Datenblätter: HubSpot-Line-Items vs. Reonic-Variante + Katalog; davon hängt ab, ob Komponenten doch in die Reonic-Checkliste müssen |
| ~~Wechselrichter: Liste in der Reonic-Checkliste oder Artikel der Auftragsbestätigung?~~ **Entschieden 03.11.:** Alle Daten aus Reonic, Komponenten über die Checkliste (WR 1–3 als feste Positionen), siehe `docs/konzept/reonic-checkliste.md`. Option: Komponenten aus der Reonic-Planungsvariante lesen (Vorschlag 4.1). | Reonic-Adapter mappt Checklisten-Schlüssel `na.*` auf die NAB-Datenkarte |
| Welche Daten braucht der Zählerwechsel? | Schritt Zählerwechsel noch nicht spezifiziert |
| Zweige > 25 kW und „Rückfrage = Ja“ | State-Machine braucht diese Zustände, Logik folgt |
| Fehlermeldung am Deal zusätzlich zum Status „blockiert“? | HubSpot-Property für Fehlertext |
| SharePoint-Ordner je Produkt vs. Datenblätter am HubSpot-Artikel | Datenblatt-Quelle als Adapter kapseln |
| Reonic: ausgefüllte Checklisten-Werte per API abrufbar? | Ggf. PDF-Export + KI-Auslesen als Fallback |

## Nächster Termin
Montag, 09.11.2026, 15:00 (2 h) – letzter Termin vor Martins Urlaub. Ziel: Demo des Dry-Runs, offene Fragen entscheiden, Übergabe bis zum Go-live am 04.12.
