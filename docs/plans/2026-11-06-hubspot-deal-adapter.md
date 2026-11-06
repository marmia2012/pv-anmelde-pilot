# Plan: HubSpot-Adapter auf Deal-Basis

Stand 06.11.2026 · Branch `feature/hubspot-deal-adapter`

## Ziel

Der HubSpot-Adapter liest alle CRM-Daten für die NAB-Datenkarte vom Deal, findet die EVU-Vollmacht und kann Aufgaben, Audit-Notizen, Dokumente und Status nach HubSpot schreiben. Geschrieben wird erst, wenn `HubSpot:WriteEnabled` aktiv ist; bis dahin wird nur geloggt.

## Festgelegt (Klärung 06.11.2026)

| Thema | Entscheidung |
|---|---|
| CRM-Objekt | **Deal** (`0-3`). Objekttyp bleibt konfigurierbar, falls reinsolar später HubSpot-Projekte nutzt |
| Datenquelle | Alle Felder als Deal-Properties der NAB-Datenkarte; Property-Namen ausschließlich aus `HubSpot:Properties` |
| Kunde / Betreiber / Anlagenort | Betreiber-Felder leer → Betreiber = Kunde. Anlagenort-Adresse leer → Kundenadresse |
| Errichter | Aus `instance.json` (Sektion `Errichter`), je Deal per Property überschreibbar |
| Installationstermin | Der spätere von Termin Elektroinstallation und Termin Dachmontage |
| Defaults | Einspeiseart = Teileinspeisung, NH-Sicherung = 63 A |
| EVU-Vollmacht | Neuester PDF-Anhang am Deal (Notizen und E-Mails), dessen Dateiname `HubSpot:VollmachtFileNamePattern` enthält (Default „Vollmacht“). Keiner gefunden → fachliches Problem |
| Aufgaben | Zugewiesen an den Deal-Inhaber, Fallback `HubSpot:DefaultTaskOwnerId` |
| Audit | Notizen am Deal, Stufe `HubSpot:AuditLevel` = `Verbose` (alles) / `Info` (wichtige Ereignisse, Default) / `Error` (nur Fehler) |
| Ablage | Dateien privat in HubSpot-Dateien (`/pv-autopilot/<dealId>`), dazu Notiz mit Anhängen am Deal |
| Status-Properties | Status, Netzbetreiber, Fehlertext, MaStR-Nummer Anlage, MaStR-Nummer Speicher |
| MaStR-Login | Aus vorhandenen Deal-Properties lesen. **Nicht löschen** |
| Schreiben | Gebaut, aber `HubSpot:WriteEnabled = false` als Default |
| Struktur-Endpunkt | `GET /admin/hubspot/structure`, nur lesend |
| Tests | Simulierte HTTP-Antworten, nie gegen echtes HubSpot |
| Adressen | Getrennte Felder (Straße, Hausnummer, PLZ, Ort) oder eine einzeilige Adresse, die zerlegt wird; nicht zerlegbar → fachliches Problem |
| Pipeline-Prüfung | Keine zusätzliche Abfrage: Stage-IDs sind je HubSpot-Account eindeutig |
| Ansprechpartner reinsolar | Vorerst nicht gelesen (kein Formular braucht ihn) |

## Bezug zu Konzepten und Beschlüssen

- `02-architektur.md`: Trigger über Deal-Status, UI nur HubSpot, Audit in HubSpot + Serilog, Ablage am Deal, fachliche Fehler → HubSpot-Task. Wird eingehalten.
- `02-architektur.md` / Termin 3 (26.10.): „MaStR-Passwort wird nach erfolgreicher Registrierung gelöscht“. **Bewusste Abweichung** auf Martins Wunsch: Das Passwort bleibt am Deal. Bestätigung durch Michael am 09.11. (Trello-Karte). Doku wird angepasst.
- CLAUDE.md „Trigger: HubSpot-Projekt-Pipeline“ → wird auf Deal-Pipeline korrigiert.
- Memory „HubSpot nur lesend“: Schreiben bleibt per Schalter aus; Tests nutzen keine echten Keys.
- `docs/konzept/prozesssteuerung.md`: Start- und Storno-Regel hängen an der Deal-Stage (`dealstage`) statt `hs_pipeline_stage`.

## Schritte (Umsetzungsreihenfolge)

### 1. Grundlage
- `HubSpotOptions`: `ProjectObjectTypeId` → `ObjectTypeId` (Default `0-3`), neu `WriteEnabled` (false), `AuditLevel` (Info), `DefaultTaskOwnerId`, `VollmachtFileNamePattern` („Vollmacht“), `EinspeiseartValues` (HubSpot-Optionswert → Einspeiseart).
- `ExponentialBackoffHandler` aus `Sources.Reonic` nach `PvAutopilot.Core/Http/` verschieben; Reonic und HubSpot nutzen ihn. HubSpot-HttpClient bekommt Backoff und Timeout analog Reonic.
- Validierung: `ObjectTypeId` nicht leer; `AuditLevel` gültig.
- Logging: Start-Info mit Objekttyp, WriteEnabled, AuditLevel (keine Secrets).

### 2. Lesen (`HubSpotCrmProjectSource`)
- `GET crm/v3/objects/{ObjectTypeId}/{id}?properties=<alle konfigurierten Namen>`.
- `CrmProjectData` bekommt `Problems` (wie `PlanningData`), Aggregator übernimmt sie.
- Mapping: Kunde (Anrede, Vorname, Nachname, E-Mail, Telefon, IBAN, Adresse), Betreiber mit Fallback auf Kunde, Anlagenadresse mit Fallback auf Kundenadresse, Installationstermin = max(Elektro, Dach), Einspeiseart über `EinspeiseartValues` (unbekannter Wert → Problem, leer → Default), NH-Sicherung, Bestandsanlage, Reonic-Projekt-ID, Errichter-Overrides.
- Adressen: getrennte Properties bevorzugt, sonst einzeilige Property zerlegen.
- 404 → fachlich („Deal nicht gefunden“), 401/403/5xx → technisch (`HttpRequestException`, Message nur Pfad + Status).
- Logging: Lesen Start/Ende mit Deal-ID und Anzahl gefundener Felder; fehlende Felder nur mit Feldnamen.

### 3. Errichter aus der Instanz
- Neue Optionen `ErrichterOptions` (Sektion `Errichter`: Firma, Ort, Eintragungsnummer, EingetragenBeiNetzbetreiber) in Core.
- `NabDataCardAggregator`: Errichter = CRM-Override, sonst Instanz-Konfiguration; beides leer → fehlendes Feld.

### 4. EVU-Vollmacht finden
- `GET crm/v4/objects/{type}/{id}/associations/notes` und `/emails` → Batch-Read mit `hs_attachment_ids`, `hs_timestamp` → `GET files/v3/files/{fileId}` (Name, Extension, Zeitpunkt).
- Auswahl: PDF, Name enthält Muster (case-insensitive), neuester gewinnt. Ergebnis `DocumentRef(EvuVollmacht, "hubspot", <fileId>, <Name>)`. Download erst beim Versand (nicht Teil dieses Features).
- Logging: Anzahl geprüfter Anhänge, gefunden ja/nein (keine Dateinamen, da sie Kundennamen enthalten können).

### 5. Struktur-Endpunkt
- `GET /admin/hubspot/structure` (Admin-Key): Pipelines des Objekttyps mit Stages (`crm/v3/pipelines/{type}`), Properties (`crm/v3/properties/{type}`: Name, Label, Typ, Gruppe, Optionen). Nur Metadaten, keine Datensätze.
- Logging: Abruf mit Anzahl Pipelines/Properties.

### 6. Schreiben (nur bei `WriteEnabled`)
- **Aufgaben** (`IHumanTaskSink`): Deal-Owner lesen, `POST crm/v3/objects/tasks` mit Verknüpfung zum Deal.
- **Audit** (`IAuditSink`): `AuditEvent` bekommt `AuditLevel`; Engine/Lifecycle setzen die Stufe (blocked/Fehler = Error, wichtige Zustände = Info, Rest = Verbose). Sink filtert nach `HubSpot:AuditLevel`, `POST crm/v3/objects/notes`.
- **Ablage** (`IDocumentArchive`): Upload `files/v3/files` (privat, Ordner `/pv-autopilot/<dealId>`), dann Notiz mit `hs_attachment_ids`. `AggregateDataCardStep` legt die Datenkarte als JSON-Dokument über das Archiv ab (offenes TODO).
- **Status** (`ICrmStatusSink`): `PATCH` am Deal; leerer Property-Name = Feld nicht schreiben; Fehlertext ohne PII.
- **MaStR-Login** (`IMastrCredentialStore`): `GetAsync` liest Benutzername/Passwort-Properties; `DeleteAsync` löscht nicht, loggt nur.
- Ist `WriteEnabled` aus: bisheriges Verhalten (Log statt Schreiben).
- Logging: je Schreibvorgang Typ, Deal-ID, Schlüssel; übersprungene Writes auf Debug.

### 7. Idempotenz der Writes
- Neue Tabelle `crm_writes` (Key eindeutig, ExternalId, CreatedAt) + Migration; Store-Interface in Core, EF-Implementierung in Infrastructure.
- Aufgaben: Key = taskKey. Notizen: Key = Prozess + Ereignis. Dateien: Key = Prozess + Dokumentname + Inhalts-Hash. Vorhandener Key → nichts anlegen.

### 8. Webhook, Konfiguration, Doku
- Webhook akzeptiert `deal.propertyChange` (Filter über `subscriptionType` bzw. `objectTypeId`).
- `instance.json`: `ObjectTypeId: "0-3"`, Start/Storno auf `dealstage`, neue Properties (Kunde/Betreiber/Anlage, MaStR), Sektion `Errichter`.
- `docs/konfiguration.md`, `.env.example`, neues `docs/konzept/hubspot-adapter.md`, `prozesssteuerung.md`, `02-architektur.md` (MaStR-Passwort), CLAUDE.md (Deal-Pipeline, MaStR-Passwort), `05-status-offene-fragen.md`.

## Tests

| Schritt | Abgesichertes Verhalten |
|---|---|
| 1 | Optionen-Validierung, Backoff weiterhin für Reonic und neu für HubSpot |
| 2 | Mapping aller Felder; Fallback Betreiber/Anlagenort; späterer Termin gewinnt; Defaults; unbekannte Einspeiseart → Problem; einzeilige Adresse zerlegt, unzerlegbar → Problem; 404 fachlich, 5xx technisch; keine PII in Logs |
| 3 | Override vor Instanz-Errichter; beides leer → fehlendes Feld |
| 4 | Neueste passende PDF gewinnt; Nicht-PDF und falscher Name ignoriert; keine Vollmacht → Problem |
| 5 | Endpunkt nur mit Admin-Key, liefert Stages und Properties |
| 6 | WriteEnabled=false → kein HTTP-Write; Task am Deal-Owner bzw. Fallback; Audit-Filter je Stufe; Ablage = Upload + Notiz; Status-PATCH lässt leere Namen aus; MaStR-Delete schreibt nichts |
| 7 | Gleicher Key → kein zweiter Write, auch nach Retry |
| 8 | `deal.propertyChange` löst Start/Storno über `dealstage` aus |

## Annahmen und offene Punkte

- Interne Property-Namen und Stage-IDs kommen erst mit dem Key (Struktur-Endpunkt); bis dahin bleiben sie in `instance.json` leer.
- Scopes für Michaels Anleitung ändern sich (Deals, Notizen, Aufgaben, Dateien statt Projekte/Kontakte) – Doc wird nach dem Feature aktualisiert.
- Download der Vollmacht und Einbindung ins Antragspaket kommt mit der PDF-Erzeugung.
- MaStR-Passwort nicht löschen: Bestätigung durch Michael am 09.11.
