# PV Autopilot

Automatisiert die Netzbetreiber-Prozesse für PV-Anlagen: Netzanmeldung → Einspeisezusage → Inbetriebnahme (E.8) → Zählerwechsel → MaStR. Projektkontext: [CLAUDE.md](CLAUDE.md) und [docs/context/](docs/context/).

## Aufbau

Die Adapter sind nach Art getrennt: **Quellsysteme** liefern Daten, **Netzbetreiber** nehmen Anträge entgegen, **Register** nehmen Registrierungen entgegen.

| Ordner / Projekt | Art | Inhalt |
|---|---|---|
| `src/PvAutopilot.Core` | Kern | Domäne und Prozesslogik: Vorgang + State-Machine, NAB-Datenkarte, Schritte, `SubmissionGate` (DryRun/Review/Live, kein Doppelversand), Schnittstellen für alle Adapter |
| `src/PvAutopilot.Infrastructure` | Kern | EF Core/Postgres (Vorgänge, Versand-Ledger, PLZ-Tabelle), Hangfire-Job, PII-Maskierung für Serilog |
| `src/PvAutopilot.Host` | Kern | ASP.NET Core: Webhook, Admin-Endpunkte, Hangfire-Server |
| `src/Sources/PvAutopilot.Sources.HubSpot` | Quellsystem | CRM (Deal): Daten der NAB-Datenkarte, EVU-Vollmacht, Webhooks (Signatur v3), Aufgaben, Audit-Notizen, Dokumentablage, Status (Schreiben nur mit `HubSpot__WriteEnabled`), siehe `docs/konzept/hubspot-adapter.md` |
| `src/Sources/PvAutopilot.Sources.Reonic` | Quellsystem | Planungsdaten: Hauptvariante + Katalog, Zählernummer, Checkliste „Netzanmeldung“; Backoff 2/4/8/16 s |
| `src/Sources/PvAutopilot.Sources.SharePoint` | Quellsystem | Zertifikate je Produkt |
| `src/Netzbetreiber/PvAutopilot.Netzbetreiber.NetzeDuisburg` | Netzbetreiber | Unterlagen + Versandkanal Mail oder Portal per Konfiguration |
| `src/Registries/PvAutopilot.Registries.Mastr` | Register | MaStR-Registrierung per Playwright |

Neuer Netzbetreiber = neues Projekt unter `src/Netzbetreiber/`, das `INetzbetreiberAdapter` implementiert. Neues Quellsystem = Projekt unter `src/Sources/` für `ICrmProjectSource` / `IPlanningDataSource` / `IProductDocumentSource`.

Wie der Reonic-Adapter die Checkliste liest: [docs/konzept/reonic-adapter.md](docs/konzept/reonic-adapter.md).

## Lokal starten

```powershell
docker compose up -d                 # Postgres auf localhost:5432
copy src\PvAutopilot.Host\appsettings.Local.example.json src\PvAutopilot.Host\appsettings.Local.json   # Secrets eintragen
dotnet run --project src/PvAutopilot.Host
dotnet test
```

Das Launch-Profil lädt die Instanz reinsolar. Ohne `Reonic:ApiKey` startet der Host nicht (ein Dummy-Wert reicht lokal). Die Migrationen laufen beim Start. Hangfire-Dashboard (nur Development): http://localhost:5033/hangfire

Windows: Playwright legt sehr tiefe Pfade unter `bin/` an. In einem tief verschachtelten Ordner schlägt der Build mit MSB3021 fehl. Dann das Repo nah am Laufwerk ablegen (z. B. `C:\projects\…`) oder lange Pfade aktivieren (`LongPathsEnabled`).

Neue Migration:

```powershell
dotnet ef migrations add <Name> --project src/PvAutopilot.Infrastructure --startup-project src/PvAutopilot.Host --output-dir Persistence/Migrations
```

## Konfiguration

Vollständige Referenz aller Einstellungen und Umgebungsvariablen: [docs/konfiguration.md](docs/konfiguration.md).

Jede Kundeninstanz hat einen eigenen Ordner unter `config/instances/<name>/`. Ladereihenfolge (spätere gewinnen):

1. `appsettings.json`, `appsettings.<Umgebung>.json`: produktweite Defaults (DryRun, MaStR-Testsystem)
2. `config/instances/<name>/instance.json`: kundenspezifisch, **ohne Secrets** (HubSpot-Properties und Stages, Netze-Duisburg-Kanal, SharePoint-IDs). Pfad über `Instance__ConfigFile`.
3. `src/PvAutopilot.Host/appsettings.Local.json`: lokale Secrets, von git ignoriert (Vorlage: `appsettings.Local.example.json`)
4. Umgebungsvariablen (Railway): alle Secrets. Vorlage mit allen Variablen: [config/instances/reinsolar/.env.example](config/instances/reinsolar/.env.example)

Wichtigste Variablen:

| Variable | Zweck |
|---|---|
| `Instance__Name`, `Instance__ConfigFile` | Instanz, z. B. `reinsolar` und `/app/config/instances/reinsolar/instance.json` |
| `ConnectionStrings__Autopilot` | Postgres |
| `Submission__Mode` | `DryRun` (Default) · `Review` · `Live` |
| `Submission__ContinueAfterDryRun` | Nur Staging-Demo: nach einem DryRun weiterlaufen, als wäre versendet worden. Nur mit `Mode=DryRun` und nie in der Umgebung `Production` erlaubt (auch nicht per Umgebungsvariable), sonst startet der Host nicht. Staging: `true`, Produktion: immer `false` |
| `HubSpot__AccessToken`, `HubSpot__ClientSecret` | Private App, Webhook-Signatur |
| `Reonic__ApiKey` | v3-Key (`X-Authorization`) |
| `Admin__ApiKey` | Header `X-Admin-Key` für `/admin/*` |
| `Mastr__PortalUrl` | Default ist das MaStR-**Test**system |

### HubSpot-Ereignisse (`HubSpot:TriggerRules`)

Welche Property-Änderung am Projekt welches Ereignis auslöst, steht je Instanz in `instance.json` (Ablauf: [docs/konzept/prozesssteuerung.md](docs/konzept/prozesssteuerung.md)). Eine Regel:

| Feld | Bedeutung |
|---|---|
| `Kind` | `Start`, `ReviewApproved`, `EinspeisezusageReceived`, `RueckfrageReceived`, `RueckfrageResolved`, `InbetriebnahmeReported`, `ZaehlerwechselCompleted`, `MastrCredentialsProvided`, `Cancelled` |
| `PropertyName` | interner HubSpot-Name. Leer = Regel inaktiv (Warnung beim Start) |
| `Values` | auslösende Werte (Groß-/Kleinschreibung egal). Leer = jeder nicht-leere Wert. `Start` und `Cancelled` brauchen Werte (Stage-IDs), sonst inaktiv |
| `Submission` | Pflicht bei `ReviewApproved`: `Netzanmeldung`, `Inbetriebnahme`, `Zaehlerwechsel`, `Mastr` |
| `Resolution` | Pflicht bei `RueckfrageResolved`: `AnsweredDirectly` oder `Resubmit` |
| `IsDate` | Wert ist ein Datum (Epoch-Millisekunden oder `yyyy-MM-dd`). Ungültige Werte werden ignoriert und ohne Wert geloggt |

Der Host startet nur mit mindestens einer `Start`-Regel mit `PropertyName`.

**Wichtig für Freigabe und Rückfrage:** Wir setzen keine Freigabe- oder Rückfrage-Property zurück. HubSpot schickt einen Webhook nur, wenn sich ein Wert *ändert*. Steht eine Freigabe-Checkbox oder die Erledigungs-Auswahl einer Rückfrage noch von früher auf dem auslösenden Wert, muss sie erst zurückgesetzt und dann neu gesetzt werden (steht so auch im jeweiligen HubSpot-Task). Verspätete oder wiederholte alte Ereignisse erkennt der Kern am `occurredAt` des Webhooks und ignoriert sie. Empfehlung: Freigabe und Erledigung später als Datum-/Zeitstempel-Property mit leeren `Values` abbilden – dann ist jedes Setzen eine Änderung, Leeren löst nichts aus.

### Watchdog (`Watchdog`)

Täglicher Hangfire-Job `process-watchdog`: legt Tasks für überfällige Wartezustände an, plant hängengebliebene Vorgänge neu ein (vorbereitete Einreichungen nur außerhalb von DryRun, z. B. nach dem Wechsel auf Review/Live) und holt Vorgänge ab, deren Inbetriebnahmedatum erreicht ist. Die Defaults stehen in `ProcessWatchdogOptions` und müssen nicht konfiguriert werden. Überschreiben z. B. per Umgebungsvariable:

| Variable | Default |
|---|---|
| `Watchdog__Schedule` | `0 7 * * 1-5` (Cron, UTC; 5 oder 6 Felder) |
| `Watchdog__StalledAfter` | `01:00:00` |
| `Watchdog__MaxWait__<Zustand>` | z. B. `Watchdog__MaxWait__RueckfrageOpen=5.00:00:00`; Fristen siehe Konzept |

Ungültiger Cron oder Fristen ≤ 0 verhindern den Start.

### Parallele Läufe

Ein Vorgang läuft nie zweimal gleichzeitig: `ProcessJob` nimmt ein Postgres-Advisory-Lock je Vorgang. Ist es belegt, wartet der Lauf bis zu 30 s und plant sich sonst in einer Minute neu ein (kein Fehler, kein verbrauchter Retry).

## Admin-Endpunkte

- `PUT /admin/netzbetreiber-plz`: CSV `plz;ort;netzbetreiber` (nur Semikolon) ersetzt die PLZ-Tabelle; eine leere Datei wird mit 400 abgelehnt
- `GET /admin/hubspot/structure`: Pipelines (mit Stage-IDs) und Properties (interner Name, Label, Typ, Gruppe, Optionswerte) des Objekttyps `HubSpot:ObjectTypeId` – zum Füllen von `instance.json`. Nur Metadaten, keine Datensätze. 502, wenn HubSpot einen Fehler liefert (Meldung nur mit Pfad und Status)
- `GET /admin/processes/{id}`: Status, Fakten (Revision, Daten, Netzbetreiber-Referenz, MaStR-Nummern, Storno) und Verlauf eines Vorgangs (ohne personenbezogene Daten)
- `POST /admin/processes/{id}/run`: Lauf anstoßen (404, wenn unbekannt)
- `POST /admin/processes/{id}/resume`: blockierten Vorgang fortsetzen (404 unbekannt, 409 nicht blockiert)
- `POST /admin/processes/{id}/submissions/{kind}/resolve`: unklaren Versand nach manueller Prüfung beim Netzbetreiber bzw. im MaStR klären. `kind` = `Netzanmeldung`, `Inbetriebnahme`, `Zaehlerwechsel` oder `Mastr`; maßgeblich ist die aktuelle Einreichung (bei der Netzanmeldung die aktuelle Revision). Body `{"outcome": "Sent", "reference": "NB-4711"}` (angekommen, `reference` optional; bei `Mastr` im Format `anlage=SEE…;speicher=SSE…`) oder `{"outcome": "NotSent"}` (nicht angekommen, der nächste Lauf versendet erneut). Nur für blockierte Vorgänge und nur, wenn gerade kein Lauf den Vorgang bearbeitet (Prozess-Lock) – sonst könnte ein laufender Versand doppelt ausgelöst werden. Danach wird der Vorgang fortgesetzt. Antwort 200, 409 Vorgang nicht blockiert, gerade in Bearbeitung oder Eintrag steht nicht auf „Versand läuft“ (`Sending`), 404 Vorgang oder Eintrag unbekannt, 400 ungültig. Schreibt einen Audit-Eintrag. Im Modus Review ist nach `NotSent` eine neue Freigabe nötig
- `POST /admin/processes/{id}/events`: Ereignis festhalten, solange Postfach und HubSpot-Properties fehlen (und für Demos). Body z. B. `{"kind": "EinspeisezusageReceived", "date": "2026-11-12", "reference": "NB-4711"}`; weitere Felder `submission` (bei `ReviewApproved`) und `resolution` (bei `RueckfrageResolved`). Antwort 202 übernommen, 409 passt nicht zum Stand (z. B. Freigabe ohne wartende Stufe), 404 unbekannt, 400 ungültig. `Start` ist hier nicht erlaubt.

Demo auf Staging (`Mode=DryRun`, `ContinueAfterDryRun=true`):

```powershell
$h = @{ "X-Admin-Key" = $env:ADMIN_KEY }
$u = "https://<host>/admin/processes/<id>/events"
Invoke-RestMethod $u -Method Post -Headers $h -ContentType application/json -Body '{"kind":"EinspeisezusageReceived","date":"2026-11-12"}'
Invoke-RestMethod $u -Method Post -Headers $h -ContentType application/json -Body '{"kind":"InbetriebnahmeReported","date":"2026-12-01"}'
Invoke-RestMethod $u -Method Post -Headers $h -ContentType application/json -Body '{"kind":"ZaehlerwechselCompleted","date":"2026-12-15"}'
```

## Regeln

- Keine personenbezogenen Daten in Logs. Felder sind mit `[PersonalData]` markiert und werden bei `{@Objekt}` maskiert. Einzelwerte nie direkt loggen, auch nicht in Exception-Messages.
- Externe Einreichungen laufen nur über `SubmissionGate`. Ein abgebrochener Versand wird nie automatisch wiederholt, sondern blockiert und als Task gemeldet. Weiter geht es erst, wenn ein Mensch das Ergebnis über `…/submissions/{kind}/resolve` meldet.
