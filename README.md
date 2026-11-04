# PV Autopilot

Automatisiert die Netzbetreiber-Prozesse für PV-Anlagen: Netzanmeldung → Einspeisezusage → Inbetriebnahme (E.8) → Zählerwechsel → MaStR. Projektkontext: [CLAUDE.md](CLAUDE.md) und [docs/context/](docs/context/).

## Aufbau

Die Adapter sind nach Art getrennt: **Quellsysteme** liefern Daten, **Netzbetreiber** nehmen Anträge entgegen, **Register** nehmen Registrierungen entgegen.

| Ordner / Projekt | Art | Inhalt |
|---|---|---|
| `src/PvAutopilot.Core` | Kern | Domäne und Prozesslogik: Vorgang + State-Machine, NAB-Datenkarte, Schritte, `SubmissionGate` (DryRun/Review/Live, kein Doppelversand), Schnittstellen für alle Adapter |
| `src/PvAutopilot.Infrastructure` | Kern | EF Core/Postgres (Vorgänge, Versand-Ledger, PLZ-Tabelle), Hangfire-Job, PII-Maskierung für Serilog |
| `src/PvAutopilot.Host` | Kern | ASP.NET Core: Webhook, Admin-Endpunkte, Hangfire-Server |
| `src/Sources/PvAutopilot.Sources.HubSpot` | Quellsystem | CRM: Projektdaten, Webhooks (Signatur v3), Tasks, Audit, Dokumentablage |
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
| `HubSpot__AccessToken`, `HubSpot__ClientSecret` | Private App, Webhook-Signatur |
| `Reonic__ApiKey` | v3-Key (`X-Authorization`) |
| `Admin__ApiKey` | Header `X-Admin-Key` für `/admin/*` |
| `Mastr__PortalUrl` | Default ist das MaStR-**Test**system |

## Admin-Endpunkte

- `PUT /admin/netzbetreiber-plz`: CSV `plz;ort;netzbetreiber` (nur Semikolon) ersetzt die PLZ-Tabelle; eine leere Datei wird mit 400 abgelehnt
- `GET /admin/processes/{id}`: Status und Verlauf eines Vorgangs (ohne personenbezogene Daten)
- `POST /admin/processes/{id}/run`: Lauf anstoßen (404, wenn unbekannt)
- `POST /admin/processes/{id}/resume`: blockierten Vorgang fortsetzen (404 unbekannt, 409 nicht blockiert)

## Regeln

- Keine personenbezogenen Daten in Logs. Felder sind mit `[PersonalData]` markiert und werden bei `{@Objekt}` maskiert. Einzelwerte nie direkt loggen, auch nicht in Exception-Messages.
- Externe Einreichungen laufen nur über `SubmissionGate`. Ein abgebrochener Versand wird nie automatisch wiederholt, sondern blockiert und als Task gemeldet.
