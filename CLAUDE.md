# PV Autopilot – Instanz reinsolar (Pilotkunde)

Automatisiert für Solarteure die Netzbetreiber-Prozesse rund um PV-Anlagen: **Einspeiseanfrage/Netzanmeldung → Einspeisezusage → Inbetriebnahme (E.8) → Zählerwechsel → Marktstammdatenregister (MaStR)**. Datenquellen sind HubSpot, Reonic und SharePoint.

Pilotkunde: reinsolar GmbH (Duisburg), Ansprechpartner Michael Roquet. Umfang V1: 8 Netzbetreiber, Datenquellen Reonic + HubSpot.

> Ausführlicher Kontext liegt in `docs/context/`. Lies die passende Datei, bevor du an einem Bereich arbeitest:
> - `01-projekt.md` – Ziel, Scope, Zeitplan, Rollen
> - `02-architektur.md` – verbindliche Architektur- und Technikentscheidungen
> - `03-schnittstellen.md` – Rechercheergebnisse zu Netze Duisburg, MaStR, Reonic, Railway, VNB, Lageplan
> - `04-dataflow-feldmapping.md` – Datenfluss, Datenquellen je Feld, Formular-Mapping Netze Duisburg (E.2/E.3/E.4/E.6/E.8)
> - `05-status-offene-fragen.md` – aktueller Stand, To-dos, offene Fragen (Stand 06.11.2026)
> - `meetings/` – Protokolle der Termine mit reinsolar
> - `docs/konfiguration.md` – alle Einstellungen und Umgebungsvariablen: wo setzen, was sie bewirken

## Produktgrundsatz (wichtig für jede Code-Entscheidung)
- **Wiederverwendbares Produkt, Single-Tenant-Instanz je Kunde.** reinsolar ist die erste Instanz; der Code soll für weitere Solarteure nutzbar sein.
- **Datenquellen- und Netzbetreiber-Anbindungen strikt entkoppelt.** Ein Netzbetreiber = ein Code-Adapter. Eine Datenquelle (HubSpot, Reonic, SharePoint) = ein Adapter. Kundenspezifisches (Feld-IDs, Pipeline-Stages, Defaults, PLZ-Tabelle) gehört in Konfiguration, nicht in die Kernlogik.
- Repo gehört Martin; reinsolar bekommt den Code nach Go-live auf Wunsch. → **Keine Secrets, Zugangsdaten oder Kundendaten ins Repo.**

## Tech-Stack
.NET 10 · Hangfire · PostgreSQL + EF Core · Playwright for .NET · PDFsharp · Serilog · Docker · Hosting Railway (EU West, Amsterdam), Staging + Produktion.

## Architektur in Kürze
- **Prozesssteuerung:** eigene State-Machine in Postgres + Hangfire-Jobs. Idempotent, ereignisgesteuert (HubSpot-Webhook auf Projekt-/Deal-Status).
- **Trigger:** HubSpot-Projekt-Pipeline „Termine an Kunde gesendet & unterschriebene Vollmacht zurück“.
- **NAB-Datenkarte:** aggregiert HubSpot + Reonic + SharePoint, gespeichert in Prozess-DB und HubSpot.
- **Netzbetreiber-Ermittlung:** PLZ-Tabelle in der Prozess-DB (pflegbar per Admin-Endpoint/CSV); VNB Digital nur optional als Adapter.
- **Einreichung:** vollautomatisch, Go-live in drei Stufen → als Modus abbilden: `DryRun` (Staging) → `Review` (Prüfschritt vor dem Absenden) → `Live`.
- **UI:** nur HubSpot (Status, Timeline, Tasks). Erzeugte Dokumente werden am Deal/Projekt in HubSpot abgelegt.
- **Audit-Log:** in HubSpot UND im technischen Log (Serilog).
- **Postfach** anschlussplanung@reinsolar.de: Exchange/M365 → Microsoft Graph.
- **KI:** OpenAI-API (Account von reinsolar), Usage-Limits, kein Training. Nur für: Mails klassifizieren, Datenblätter auslesen, Antwortentwürfe (ein Mensch sendet).
- **MaStR:** Playwright im Webportal mit dem Kundenlogin aus HubSpot. Das MaStR-Passwort wird nach erfolgreicher Registrierung gelöscht.
- **Fehler:** fachlich → HubSpot-Task. Technisch → 3–5 Retries, dann Mail an Martin (mit Screenshot) und Status „blockiert“ am Deal.

## Harte Regeln
- **Keine personenbezogenen Daten in Logs** (Namen, Adressen, IBAN, E-Mail, Telefon maskieren). Railway-Logs liegen in den USA.
- Keine Secrets im Code. Konfiguration über Environment-Variablen / Railway-Variablen.
- Jeder Job muss idempotent und wiederaufnehmbar sein. Externe Einreichungen nie doppelt absenden.
- Externe Systeme ohne offizielle API (Netze-Duisburg-Portal, MaStR-Webportal, VNB Digital) hinter Adapter-Interfaces kapseln. Selektoren bzw. Endpunkte zentral halten.
- Reonic-Rate-Limit: exponentielles Backoff (2/4/8/16 s).
- Im Zweifel `DryRun` als Default, nie unbeabsichtigt live einreichen.
- **Neue oder geänderte Konfiguration** (Options-Property, `Configuration["…"]`, Umgebungsvariable, Default, Startvalidierung) im selben Change in `docs/konfiguration.md` dokumentieren: Schlüssel, Ort (ENV / `instance.json` / `appsettings`), Default, Wirkung. Secrets und umgebungsabhängige Werte zusätzlich in `config/instances/<instanz>/.env.example` eintragen.

## Aktueller Fokus (bis 15.11.2026)
Komplette Strecke **Netze Duisburg + MaStR** bauen, Dry-Run auf Staging. Duisburg live am 04.12.2026. Danach RheinNetz/ENNI + Westnetz (bis 15.12.), restliche Netzbetreiber im Januar 2027.
Offener Blocker: **Antragsweg Netze Duisburg (Portal oder Mail)** ist noch nicht entschieden → Adapter so schneiden, dass beides möglich ist (gemeinsame Formular-/PDF-Erzeugung, austauschbarer Versandkanal).

## Konventionen (Vorschlag – bei Bedarf anpassen)
- Code und Identifier auf Englisch; Fachbegriffe bleiben deutsch, wo es keine saubere Übersetzung gibt (z. B. `Netzbetreiber`, `Einspeisezusage`, `MaStR`).
- Dokumentation und Commit-Messages auf Deutsch.
