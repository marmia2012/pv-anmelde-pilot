# 02 – Architektur und Technikentscheidungen (Stand 26.10.2026, mit reinsolar abgestimmt)

## Grundsatz
- Hybrid: Code als wiederverwendbares Produkt („Anmeldepilot“), **Single-Tenant-Instanz je Kunde**.
- Datenquellen- und Netzbetreiber-Anbindungen strikt entkoppelt.
- Tech-Stack: .NET 10, Hangfire, PostgreSQL/EF Core, Playwright, PDFsharp, Serilog, Docker.

## Bausteine
| Baustein | Entscheidung |
|---|---|
| Prozesssteuerung | Eigene State-Machine (Postgres) + Hangfire-Jobs; idempotent, ereignisgesteuert |
| Trigger | HubSpot-Webhook auf Projekt-/Deal-Status (Pipeline: Termine an Kunde gesendet & unterschriebene Vollmacht zurück) |
| NB-Anbindung | Reiner Code-Adapter je Netzbetreiber |
| NB-Ermittlung | PLZ-Tabelle in der Prozess-DB, pflegbar per Admin-Endpoint/CSV |
| Einreichung | Vollautomatisch; Go-live in drei Stufen: Dry-Run auf Staging → Einreichung mit Prüfschritt vor dem Absenden → live |
| UI | Nur HubSpot (Status, Timeline, Tasks) |
| Audit-Log | HubSpot + technisches Log (Serilog) |
| Ablage erzeugter Dokumente | HubSpot (am Deal/Projekt) |
| Postfach anschlussplanung@reinsolar.de | Exchange/M365 → Microsoft Graph |
| KI | OpenAI-API über reinsolars Plattformzugang; eigene Usage-Limits, kein Training. Einsatz: Mails klassifizieren, Datenblätter auslesen, Antwortentwürfe (Mensch sendet) |
| MaStR | Playwright im Webportal mit Kundenlogin aus HubSpot (kommt über ein Webformular auf der reinsolar-Website). SOAP-Webdienst scheidet aus (bräuchte Vollmacht für alle Kunden). Passwort wird nach erfolgreicher Registrierung gelöscht |
| Lageplan | TIM-online bzw. WMS Geobasis NRW (ALKIS) – Schnittstelle noch zu prüfen |
| Datenblätter | Vorerst SharePoint; später ggf. als Anhang am HubSpot-Artikel |
| Fehler fachlich | HubSpot-Task |
| Fehler technisch | 3–5 Retries, dann Mail an Martin (mit Screenshot), Status „blockiert“ am Deal |

## HubSpot
- Plan: Professional.
- Artikel bleiben vorerst am **Deal**, das **Projekt** referenziert den Deal (Artikel lassen sich am Projekt nicht anzeigen).
- Die finale Struktur (Projekte/Artikel) legt Michael fest; der Code adaptiert → Property-Namen und Pipeline-Stages konfigurierbar halten.

## Reonic
- REST API v3, Read-only-API-Key ist vorhanden (seit 26.10.).
- Checklisten-Struktur für Duisburg skizziert Martin, Michael überträgt sie in Reonic.
- Idee: Wechselrichter über die Artikel der Auftragsbestätigung (Reonic → HubSpot) statt als Liste in der Checkliste – noch offen.

## Netze Duisburg
- PV-Anmeldeportal (SAP UI5), Zugang seit 26.10. vorhanden.
- Das Portal verlangt trotz Webformular alle Formulare zusätzlich als Upload (Doppelerfassung).
- **Portal oder Mail ist offen**, Martin tendiert zu Mail. → Versandkanal austauschbar bauen.

## Hosting
- Railway im Account von reinsolar, Region EU West (Amsterdam), Staging + Produktion.
- Staging nach Go-live abschalten → Kosten etwa halbiert (Start ~45–60 $/Monat geschätzt).
- DSGVO: Hinweis gegeben (US-Anbieter, Logs in den USA), reinsolar trägt das Risiko. **Keine Klarnamen, Adressen oder IBAN in Logs.**
- Azure ausgeschlossen. EU-Alternativen (Hetzner, Clever Cloud, Scaleway) nur Plan B.
- Postgres-Backups: Railway-Snapshots + zusätzlich `pg_dump` offsite (Backups sterben mit dem Volume).
