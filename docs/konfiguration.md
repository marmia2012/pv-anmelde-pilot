# Konfiguration

Referenz aller Einstellungen: wo sie gesetzt werden, was sie bewirken und was beim Start geprüft wird.

> **Pflege:** Jede neue Einstellung (Options-Property, `Configuration["…"]`, Umgebungsvariable, Docker-/Railway-Variable) wird im selben Change hier eingetragen – und, falls geheim oder umgebungsabhängig, zusätzlich in [config/instances/reinsolar/.env.example](../config/instances/reinsolar/.env.example).

## 1. Wo wird was konfiguriert?

Die Quellen werden in dieser Reihenfolge geladen, **spätere überschreiben frühere** ([Program.cs](../src/PvAutopilot.Host/Program.cs)):

| # | Quelle | Inhalt | Im Repo? |
|---|---|---|---|
| 1 | `src/PvAutopilot.Host/appsettings.json` | produktweite Defaults (DryRun, MaStR-Testsystem, Log-Level) | ja |
| 2 | `src/PvAutopilot.Host/appsettings.<Umgebung>.json` | Abweichungen je Umgebung: `Development`, `Staging`, `Production` | ja |
| 3 | `config/instances/<instanz>/instance.json` | kundenspezifisch, **nicht geheim**: HubSpot-Properties, Pipeline-Stages, Trigger-Regeln, SharePoint-IDs, Netzbetreiber-Kanal. Wird nur geladen, wenn `Instance__ConfigFile` gesetzt ist | ja |
| 4 | `src/PvAutopilot.Host/appsettings.Local.json` | lokale Secrets für die Entwicklung | **nein** (git-ignoriert) |
| 5 | Umgebungsvariablen | alle Secrets und Umgebungsschalter; auf Railway als Service-Variablen | **nein** |
| 6 | Kommandozeile | z. B. `--Submission:Mode=Review` für Einzeltests | – |

Faustregeln:

- **Geheim** (Token, Keys, Passwörter, Connection-String) → nur Umgebungsvariable bzw. lokal `appsettings.Local.json`. Nie in eine JSON-Datei im Repo.
- **Kundenspezifisch, nicht geheim** → `instance.json` der Instanz.
- **Für alle Kunden gleich** → `appsettings.json`; je Umgebung abweichend → `appsettings.<Umgebung>.json`.
- **Schreibweise:** In Umgebungsvariablen ersetzt `__` (doppelter Unterstrich) den Doppelpunkt der Verschachtelung: `Reonic:ApiKey` → `Reonic__ApiKey`, Listen per Index: `HubSpot__TriggerRules__0__Values__0`.

### Umgebungen

| Umgebung | `ASPNETCORE_ENVIRONMENT` | Wo | Besonderheiten |
|---|---|---|---|
| Lokal | `Development` (über [launchSettings.json](../src/PvAutopilot.Host/Properties/launchSettings.json)) | `dotnet run` | Postgres aus `docker compose`, MaStR-Browser sichtbar (`Headless=false`), Hangfire-Dashboard unter `/hangfire` |
| Staging | `Staging` | Railway (eigener Service) | `DryRun` + `ContinueAfterDryRun=true` für Demos der ganzen Strecke |
| Produktion | `Production` | Railway (eigener Service) | Default ebenfalls `DryRun`; `ContinueAfterDryRun` technisch gesperrt |

## 2. Checkliste neue Umgebung / neue Instanz

1. Instanzordner `config/instances/<name>/` anlegen (Vorlage: `reinsolar`), `instance.json` mit HubSpot-IDs füllen (Abschnitt 4).
2. Railway-Service anlegen, Variablen aus [.env.example](../config/instances/reinsolar/.env.example) setzen – mindestens:
   `ASPNETCORE_ENVIRONMENT`, `Instance__Name`, `Instance__ConfigFile`, `ConnectionStrings__Autopilot`, `Admin__ApiKey`, `HubSpot__AccessToken`, `HubSpot__ClientSecret`, `Reonic__ApiKey`.
3. `Submission__Mode` bewusst setzen (Default `DryRun`).
4. Nach dem Deploy das Start-Log auf Warnungen zu inaktiven Trigger-Regeln prüfen.
5. PLZ-Tabelle per `PUT /admin/netzbetreiber-plz` hochladen (siehe [README](../README.md#admin-endpunkte)).

## 3. Einstellungen im Einzelnen

Spalte **Wo**: `ENV` = Umgebungsvariable (Railway) bzw. lokal `appsettings.Local.json` · `INST` = `instance.json` · `APP` = `appsettings*.json`.
Spalte **Start**: ✗ = Host startet ohne gültigen Wert nicht.

### Laufzeit und Instanz

| Schlüssel (ENV-Schreibweise) | Wo | Default | Wirkung | Start |
|---|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | ENV | `Production` | wählt `appsettings.<Umgebung>.json`; `Production` sperrt `ContinueAfterDryRun` | |
| `Instance__Name` | ENV / INST | – | Instanzname, wird an jeden Logeintrag gehängt (`Instance`); ohne Wert `unbekannt` | |
| `Instance__ConfigFile` | ENV | – | Pfad zur `instance.json` (relativ zum Content-Root oder absolut). Im Container: `/app/config/instances/<name>/instance.json`. Gesetzt, aber Datei fehlt → Start bricht ab | (✗) |
| `ASPNETCORE_HTTP_PORTS` | Dockerfile | `8080` | HTTP-Port im Container | |
| `PLAYWRIGHT_BROWSERS_PATH` | Dockerfile | `/ms-playwright` | Ablage des beim Build installierten Chromium | |

### Datenbank

| Schlüssel | Wo | Default | Wirkung | Start |
|---|---|---|---|---|
| `ConnectionStrings__Autopilot` | ENV (lokal: `appsettings.Development.json`) | lokal `Host=localhost;Port=5432;Database=pvautopilot;Username=postgres` | Postgres für Prozess-DB **und** Hangfire. Migrationen laufen beim Start automatisch. Railway: `Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}}` | ✗ |

### Einreichung (`Submission`)

| Schlüssel | Wo | Default | Wirkung | Start |
|---|---|---|---|---|
| `Submission__Mode` | ENV / APP | `DryRun` | `DryRun`: Unterlagen werden erzeugt und abgelegt, **nichts versendet**. `Review`: vor jedem Versand ist eine Freigabe in HubSpot nötig (Trigger `ReviewApproved`). `Live`: vollautomatischer Versand | |
| `Submission__ContinueAfterDryRun` | APP (Staging) / ENV | `false` (Staging: `true`) | Nur Demo: nach einem DryRun weiterlaufen, als wäre versendet worden. Nur mit `Mode=DryRun` und nie in `Production` erlaubt. Wer Staging auf `Review`/`Live` stellt, muss hier `false` setzen | ✗ |

### Admin-Endpunkte

| Schlüssel | Wo | Default | Wirkung |
|---|---|---|---|
| `Admin__ApiKey` | ENV | leer | Erwarteter Wert im Header `X-Admin-Key` für alle `/admin/*`-Endpunkte. **Leer = Admin-Endpunkte gesperrt** (immer 401). Langen Zufallswert verwenden |

### HubSpot (`HubSpot`)

| Schlüssel | Wo | Default | Wirkung | Start |
|---|---|---|---|---|
| `HubSpot__AccessToken` | ENV | – | Token der Private App (Bearer) für die HubSpot-API | |
| `HubSpot__ClientSecret` | ENV | – | Client-Secret der App, prüft die Webhook-Signatur v3. **Leer = jeder Webhook wird abgelehnt** | |
| `HubSpot__BaseUrl` | APP | `https://api.hubapi.com/` | API-Basis-URL, nur für Tests ändern | |
| `HubSpot:ProjectObjectTypeId` | INST | leer | Objekttyp-ID der Projekte (z. B. `2-12345678`). Webhooks anderer Objekttypen werden ignoriert; leer = keine Filterung | |
| `HubSpot:TriggerRules` | INST | – | welche Property-Änderung welches Prozessereignis auslöst, siehe [Abschnitt 4](#4-hubspot-trigger-regeln-hubspottriggerrules) | ✗ |
| `HubSpot:Properties` | INST | leer | Zuordnung fachlicher Feldname → interner HubSpot-Property-Name, siehe [Abschnitt 5](#5-hubspot-property-zuordnung-hubspotproperties). Leerer Name = Feld wird weder gelesen noch geschrieben | |

### Reonic (`Reonic`)

| Schlüssel | Wo | Default | Wirkung | Start |
|---|---|---|---|---|
| `Reonic__ApiKey` | ENV | – | v3-Key `rnc_v3_…` (Header `X-Authorization`), Leserechte auf projects, checklists, components, files. Lokal reicht ein Dummy-Wert zum Starten | ✗ |
| `Reonic__BaseUrl` | APP | `https://api.reonic.com/rest/v3/` | API-Basis-URL, **muss mit `/` enden** | ✗ |
| `Reonic__BypassCache` | APP | `true` | Reonic cacht GET-Antworten bis 1 h. `true` liest Checklisten ungecacht (Komponentenkatalog bleibt gecacht); zählt gegen das kleinere „uncached“-Rate-Limit | |
| `Reonic:ChecklistStage` | INST | `offer` | Phase, deren Checkliste den Block „Netzanmeldung“ enthält: `request`, `offer` oder `installation` | |

### SharePoint (`SharePoint`) – Adapter noch nicht umgesetzt

| Schlüssel | Wo | Default | Wirkung |
|---|---|---|---|
| `SharePoint:SiteId` | INST | leer | Graph-Site-ID der Produktunterlagen |
| `SharePoint:DriveId` | INST | leer | Graph-Drive-ID (Dokumentbibliothek) |
| `SharePoint:ProductRootPath` | INST | leer | Wurzelordner, darunter `{Hersteller}/{Modell}/` |

### Netzbetreiber (`Netzbetreiber:<Code>`)

Je Netzbetreiber-Adapter ein Unterabschnitt.

| Schlüssel | Wo | Default | Wirkung |
|---|---|---|---|
| `Netzbetreiber:NetzeDuisburg:Channel` | INST | `Mail` | Antragsweg: `Mail` oder `Portal`. Unterlagen sind identisch, nur der Versandkanal wechselt. Noch mit Netze Duisburg zu klären |
| `Netzbetreiber:NetzeDuisburg:MailRecipient` | INST | leer | Empfängeradresse beim Kanal `Mail` |
| `Netzbetreiber__NetzeDuisburg__PortalUrl` | APP | Login-URL des SAP-Portals | Einstieg beim Kanal `Portal` |
| `Netzbetreiber:NetzeDuisburg:RequiredAttachments` | INST | `EvuVollmacht`, `Schaltplan` | Pflichtanhänge der Netzanmeldung (Werte von `DocumentKind`, z. B. `Lageplan`, `Datenblatt`, `Einheitenzertifikat`, `NaSchutzZertifikat`, `NaSchutzPruefbericht`). Fehlt einer in der Datenkarte → fachliche Prüfung schlägt fehl (HubSpot-Task). Alle übrigen Anhänge der Netzanmeldung sind optional: fehlen sie, nur Warnung im Log (`Optionaler Anhang fehlt`). Eine gesetzte Liste **ersetzt** den Default komplett, z. B. `"RequiredAttachments": ["EvuVollmacht", "Schaltplan", "Lageplan"]` bzw. ENV `Netzbetreiber__NetzeDuisburg__RequiredAttachments__0=EvuVollmacht` |

Die Formularvorlagen (E.1, E.2, E.3, E.8, Zählerantrag) sind im Duisburg-Adapter eingebettet und nicht konfigurierbar; Feld-Mapping in [konzept/netze-duisburg-adapter.md](konzept/netze-duisburg-adapter.md).

**PDFsharp-Version:** bewusst **6.1.1** (MIT), Entscheidung Martin 06.11.2026. 6.5.0 wirft beim Öffnen „The loaded license is not valid“. Vor einem Update das Lizenzmodell ab 6.5 klären. Eine Lizenz-Einstellung gibt es nicht.

### MaStR (`Mastr`)

| Schlüssel | Wo | Default | Wirkung |
|---|---|---|---|
| `Mastr__PortalUrl` | ENV / APP | `https://test.marktstammdatenregister.de/` | **Testsystem.** Produktiv `https://www.marktstammdatenregister.de/` erst zum Go-live und nur per Umgebungsvariable |
| `Mastr__Headless` | APP | `true` (Development: `false`) | Playwright-Browser unsichtbar; lokal zum Zuschauen `false` |

### Watchdog (`Watchdog`)

Täglicher Hangfire-Job `process-watchdog`: Tasks für überfällige Wartezustände, Neueinplanung hängengebliebener Vorgänge. Defaults in `ProcessWatchdogOptions`, Konfiguration optional.

| Schlüssel | Default | Wirkung | Start |
|---|---|---|---|
| `Watchdog__Schedule` | `0 7 * * 1-5` | Cron (UTC, 5 oder 6 Felder) | ✗ bei ungültigem Cron |
| `Watchdog__StalledAfter` | `01:00:00` | so lange in einem aktiven Zustand → Vorgang wird neu eingeplant | ✗ wenn ≤ 0 |
| `Watchdog__MaxWait__<Zustand>` | siehe unten | Frist je Wartezustand, danach HubSpot-Task. Format `d.hh:mm:ss`, z. B. `Watchdog__MaxWait__RueckfrageOpen=5.00:00:00` | ✗ wenn ≤ 0 |

Default-Fristen: `NetzanmeldungSubmitted` 30 d · `RueckfrageOpen` 5 d · `*AwaitingReview` (alle vier Stufen) 2 d · `EinspeisezusageReceived` 180 d · `ZaehlerwechselRequested` 42 d · `ZaehlerwechselCompleted` 7 d · `Blocked` 1 d.

### Logging (`Serilog`)

| Schlüssel | Default | Wirkung |
|---|---|---|
| `Serilog__MinimumLevel__Default` | `Information` | globales Log-Level |
| `Serilog__MinimumLevel__Override__<Namespace>` | `Warning` für `Microsoft.AspNetCore`, `Microsoft.EntityFrameworkCore`, `System.Net.Http.HttpClient`, `Hangfire` | Level je Namespace, z. B. zum Debuggen `Serilog__MinimumLevel__Override__Hangfire=Information` |

Ausgabe immer als kompaktes JSON auf die Konsole; personenbezogene Daten werden maskiert (unabhängig vom Level).

### Vorgesehen, noch nicht verwendet

Stehen bereits auskommentiert in der `.env.example` und werden mit den jeweiligen Adaptern aktiv:

| Schlüssel | Zweck |
|---|---|
| `Graph__TenantId`, `Graph__ClientId`, `Graph__ClientSecret`, `Graph__Mailbox` | Microsoft Graph: SharePoint-Datenblätter, Postfach `anschlussplanung@` |
| `OpenAI__ApiKey` | Mails klassifizieren, Datenblätter auslesen |
| `Alerts__Recipient` | Empfänger der Alarm-Mails bei technischen Fehlern |

## 4. HubSpot-Trigger-Regeln (`HubSpot:TriggerRules`)

In `instance.json`. Jede Regel übersetzt eine Property-Änderung am HubSpot-Projekt in ein Prozessereignis (Ablauf: [docs/konzept/prozesssteuerung.md](konzept/prozesssteuerung.md)).

| Feld | Bedeutung |
|---|---|
| `Kind` | `Start`, `ReviewApproved`, `EinspeisezusageReceived`, `RueckfrageReceived`, `RueckfrageResolved`, `InbetriebnahmeReported`, `ZaehlerwechselCompleted`, `MastrCredentialsProvided`, `Cancelled` |
| `PropertyName` | interner HubSpot-Name. Leer = Regel inaktiv (Warnung beim Start) |
| `Values` | auslösende Werte, Groß-/Kleinschreibung egal. Leer = jeder nicht-leere Wert. `Start` und `Cancelled` **brauchen** Werte (Stage-IDs), sonst inaktiv |
| `Submission` | Pflicht bei `ReviewApproved`: `Netzanmeldung`, `Inbetriebnahme`, `Zaehlerwechsel`, `Mastr` |
| `Resolution` | Pflicht bei `RueckfrageResolved`: `AnsweredDirectly` oder `Resubmit` |
| `IsDate` | Wert ist ein Datum (Epoch-Millisekunden oder `yyyy-MM-dd`) und wird als Ereignisdatum übernommen; ungültige Werte werden ignoriert |

Startprüfung: mindestens eine `Start`-Regel mit `PropertyName`; `ReviewApproved` ohne `Submission` bzw. `RueckfrageResolved` ohne `Resolution` → Host startet nicht. Unvollständige Regeln erscheinen als Warnung im Start-Log.

Was für reinsolar einzutragen ist:

| Regel | Was eintragen |
|---|---|
| `Start` | Stage-ID „Termine an Kunde gesendet & unterschriebene Vollmacht zurück“ der Projekt-Pipeline (`hs_pipeline_stage`) |
| `ReviewApproved` (je Stufe) | je Stufe eine eigene Checkbox- oder (empfohlen) Zeitstempel-Property |
| `EinspeisezusageReceived`, `InbetriebnahmeReported`, `ZaehlerwechselCompleted` | jeweilige Datums-Property |
| `RueckfrageReceived` | Checkbox „Rückfrage eingegangen“ |
| `RueckfrageResolved` | Auswahl-Property mit zwei Optionen; interne Optionswerte (`direkt_beantwortet`, `neu_einreichen`) |
| `MastrCredentialsProvided` | z. B. die MaStR-Benutzername-Property |
| `Cancelled` | Stage-ID „storniert“ |

**Achtung:** HubSpot schickt einen Webhook nur, wenn sich ein Wert *ändert*, und wir setzen keine Property zurück. Eine noch gesetzte Checkbox muss erst geleert und dann neu gesetzt werden. Datum-/Zeitstempel-Properties mit leeren `Values` vermeiden das.

## 5. HubSpot-Property-Zuordnung (`HubSpot:Properties`)

In `instance.json`: fachlicher Schlüssel → interner HubSpot-Property-Name des Kundenportals. Wird beim Aufbau der NAB-Datenkarte gelesen bzw. (nach Freigabe der Schreibrechte) geschrieben. Stand 04.11.2026: Lesen und Schreiben sind im HubSpot-Adapter noch nicht umgesetzt, die Schlüssel stehen aber fest.

| Schlüssel | Richtung | Inhalt |
|---|---|---|
| `ReonicProjectId` | lesen | Verknüpfung zum Reonic-Projekt |
| `Einspeiseart` | lesen | Voll-/Überschusseinspeisung |
| `NhSicherungAmpere` | lesen | NH-Sicherung in A |
| `BestandsanlageVorhanden` | lesen | Bestandsanlage ja/nein |
| `TerminElektroinstallation`, `TerminDachmontage` | lesen | geplante Termine |
| `ErrichterFirma`, `ErrichterOrt`, `ErrichterEintragungsnummer`, `ErrichterNetzbetreiber` | lesen | Elektro-Errichter (Installateurverzeichnis) |
| `EvuVollmacht` | lesen | Vollmacht des Kunden |
| `MastrBenutzername`, `MastrPasswort` | lesen | MaStR-Login des Kunden; Passwort wird nach erfolgreicher Registrierung gelöscht |
| `Status`, `Netzbetreiber`, `Fehlertext` | schreiben | Prozessstatus, ermittelter Netzbetreiber, Fehlermeldung am Projekt |
| `MastrNummerAnlage`, `MastrNummerSpeicher` | schreiben | MaStR-Nummern nach Registrierung |

Neue fachliche Felder: Schlüssel hier und in `instance.json` ergänzen, nie einen HubSpot-Namen im Code verdrahten.
