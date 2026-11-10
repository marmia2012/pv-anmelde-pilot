# HubSpot-Adapter: Deal lesen, nach HubSpot schreiben

Stand: 06.11.2026 · intern. Code: `src/Sources/PvAutopilot.Sources.HubSpot/`. Plan: [2026-11-06-hubspot-deal-adapter.md](../plans/2026-11-06-hubspot-deal-adapter.md). Einstellungen: [konfiguration.md](../konfiguration.md#hubspot-hubspot).

## Grundsätze

- CRM-Objekt ist der **Deal** (`HubSpot:ObjectTypeId` = `0-3`). Der Objekttyp bleibt konfigurierbar, falls reinsolar später HubSpot-Projekte oder ein Custom Object nutzt.
- Property-Namen, Stage-IDs und Optionswerte kommen ausschließlich aus `instance.json` (`HubSpot:Properties`, `TriggerRules`, `EinspeiseartValues`). Im Code stehen nur HubSpot-eigene Properties (`hubspot_owner_id`, `hs_attachment_ids`, `hs_timestamp`, `hs_note_body`, `hs_task_*`).
- Ein typisierter Client (`Api/HubSpotApiClient`) für Lesen und Schreiben; Bearer-Token, Backoff 2/4/8/16 s und Timeout wie bei Reonic. GET, PATCH, Batch-Read und der Verknüpfungs-PUT (idempotent) werden bei 429 und 5xx wiederholt; **anlegende POSTs** (Aufgabe, Notiz, Datei-Upload) nur bei 429 (`ExponentialBackoffHandler.RateLimitOnly`) – bei 429 legt HubSpot nichts an, ein 5xx geht an den Job-Retry.
- **Geschrieben wird nur mit `HubSpot__WriteEnabled=true`** (Default `false`, Memory „HubSpot nur lesend“). Ohne den Schalter schreibt kein Sink etwas, es entsteht nur ein Debug-Log je übersprungenem Write. Lesen ist davon unabhängig.

## Lesen

| Was | Klasse | Requests |
|---|---|---|
| CRM-Daten der NAB-Datenkarte | `HubSpotCrmProjectSource` + `Mapping/DealMapper` | `GET crm/v3/objects/{type}/{id}?properties=…` (alle konfigurierten Namen) |
| EVU-Vollmacht | `Documents/VollmachtFinder` | `GET crm/v4/objects/{type}/{id}/associations/notes` bzw. `/emails` → `POST crm/v3/objects/{notes,emails}/batch/read` (`hs_attachment_ids`, `hs_timestamp`) → `GET files/v3/files/{id}` |
| Struktur (Admin) | `HubSpotStructureReader` | `GET crm/v3/pipelines/{type}`, `GET crm/v3/properties/{type}` – nur Metadaten |
| MaStR-Login | `HubSpotMastrCredentialStore.GetAsync` | `GET crm/v3/objects/{type}/{id}?properties=<MastrBenutzername>,<MastrPasswort>` |

## Schreiben

Alle Sinks in `HubSpotServices.cs`; Verknüpfung mit dem Deal über den vordefinierten Verknüpfungstyp (`HUBSPOT_DEFINED`, Notiz → Deal 214, Aufgabe → Deal 216). Für Objekttypen ohne bekannten Typ (Projekte, Custom Objects) wird nach dem Anlegen die Standard-Verknüpfung gesetzt (`PUT crm/v4/objects/{from}/{id}/associations/default/{to}/{toId}`).

| Sink | Wann | Request | Inhalt |
|---|---|---|---|
| `IHumanTaskSink` → `HubSpotTaskSink` | fachliches Problem, Freigabe, Frist überschritten, Blockade | `GET` Deal (`hubspot_owner_id`), `POST crm/v3/objects/tasks` | Betreff, Text, Status `NOT_STARTED`, Typ `TODO`, fällig jetzt. Inhaber = Deal-Inhaber, sonst `HubSpot:DefaultTaskOwnerId`, sonst unzugewiesen |
| `IAuditSink` → `HubSpotAuditSink` | jedes Audit-Ereignis mit Stufe ≥ `HubSpot:AuditLevel` | `POST crm/v3/objects/notes` | „PV Autopilot · {Typ}: {Nachricht}“ (ohne personenbezogene Daten) |
| `IDocumentArchive` → `HubSpotDocumentArchive` | Antragsunterlagen (je Stufe), NAB-Datenkarte nach der Aggregation | je Dokument `POST files/v3/files` (multipart, `access: PRIVATE`, Ordner `/pv-autopilot/<dealId>`), dann `POST crm/v3/objects/notes` mit `hs_attachment_ids` | Notiz listet die Dateinamen (nur in HubSpot, nie im Log) |
| `ICrmStatusSink` → `HubSpotStatusSink` | nach jedem Zustandswechsel, am Ende jedes Laufs (falls der aktuelle Stand im Lauf noch nicht erfolgreich geschrieben wurde), beim Blockieren, nach Resume | `PATCH crm/v3/objects/{type}/{id}` | siehe „Status-Properties“ |
| `ICrmOrderSink` → `HubSpotOrderSink` | Auftragsübernahme nach der Unterschrift in Reonic ([reonic-hubspot-auftrag.md](reonic-hubspot-auftrag.md)) | `GET` Deal, `GET crm/v4/objects/0-3/{id}/associations/line_items`, je Position `POST crm/v3/objects/line_items` (Verknüpfung Artikel → Deal 20), `PATCH` Deal (`Auftrag*`), Datei + Notiz über `HubSpotDocumentArchive` | Artikel mit Name, Menge, Bruttopreis, SKU, Langtext; immer am Deal `0-3` (ID aus `integrations.hubspotDealId`), unabhängig von `ObjectTypeId`. Aufgaben und PDF dafür über die Überladungen `HubSpotTaskSink.CreateTaskAsync(objectTypeId, …)` bzw. `HubSpotDocumentArchive.StoreAsync(objectTypeId, …)` |
| `IMastrCredentialStore.DeleteAsync` | nach der MaStR-Registrierung | – | **löscht nicht**, nur Info-Log (siehe „MaStR-Passwort“) |

Die NAB-Datenkarte legt `AggregateDataCardStep` nach erfolgreicher Aggregation als `nab-datenkarte-r<Revision>.json` (JSON, Enums als Text) über `IDocumentArchive` ab, mit stabilem Ablage-Schlüssel `datacard:<prozess>:r<Revision>` (`GeneratedDocument.ArchiveKey`): je Vorgang und Revision genau eine Datei, auch wenn ein Wiederholungslauf eine inhaltlich geänderte Karte erzeugt. `DocumentRef.Reference` (signierte URLs, Datei-IDs) steht **nicht** in der abgelegten Datei, nur Art, Quelle und Dateiname; die Prozess-DB speichert die Karte vollständig. Konzept: Datenkarte in der Prozess-DB und in HubSpot.

### Status-Properties

| Schlüssel (`HubSpot:Properties`) | Wert | Regel |
|---|---|---|
| `Status` | Zustandsname (`ProcessState`, z. B. `NetzanmeldungSubmitted`) | immer |
| `Fehlertext` | `BlockReason` bei `Blocked`, sonst leer | immer (leert den Text nach Resume); `BlockReasons` lässt nur sichere Texte durch |
| `Netzbetreiber` | Netzbetreiber-Code | nur wenn ermittelt |
| `MastrNummerAnlage`, `MastrNummerSpeicher` | MaStR-Nummern | nur wenn vorhanden; ein leerer Stand überschreibt keinen von Hand gepflegten Wert |

Leerer Property-Name = Feld wird nicht geschrieben. Ist keiner gesetzt, entfällt der PATCH. Idempotent, da immer der aktuelle Stand geschrieben wird.

**Dauerhaft abgelehnter PATCH** (4xx außer 429, z. B. falscher Property-Name, fehlender Optionswert der Status-Property, Deal gelöscht): Wiederholen hilft nicht. Der Sink loggt Error (Prozess-ID, Zustand, Statuscode), schickt **einmal je Vorgang und Zustand** einen Alarm an den Betreiber (Ledger-Schlüssel `alert:status:<prozess>:<zustand>`) und kehrt normal zurück. Transiente Fehler (429, 5xx, Netzwerk) werfen weiter; der Aufrufer loggt Warning, der nächste Lauf holt nach.

### Audit-Stufen

`AuditEvent.Level` setzt der Kern (`AuditLevels.ForStateChange`, `ProcessEngine`, `ProcessFailureHandler`, Admin-Endpunkt). Filter: `Verbose` < `Info` < `Error`; eine Notiz entsteht, wenn Stufe ≥ `HubSpot:AuditLevel`. Serilog bekommt immer alle Ereignisse.

| Stufe | Ereignisse |
|---|---|
| `Error` | `blocked` (Blockade nach allen Retries), Zustandswechsel nach `Blocked` |
| `Info` | `human-action` (Aufgabe angelegt), `submission-resolved` (unklarer Versand geklärt), Zustandswechsel nach: Einreichung vorbereitet (`*Prepared`), Freigabe wartet (`*AwaitingReview`), eingereicht (`NetzanmeldungSubmitted`, `InbetriebnahmeSubmitted`, `ZaehlerwechselRequested`), `RueckfrageOpen`, `EinspeisezusageReceived`, `InbetriebnahmeReady`, `ZaehlerwechselCompleted`, `MastrRegistered`, `Completed`, `Cancelled` |
| `Verbose` | Zustandswechsel nach `Created` (neue Revision), `DataCardAggregated`, `NetzbetreiberResolved` |

## Idempotenz

Aufgaben, Notizen und Dateien laufen über `HubSpotWriteOnce` und die Tabelle `crm_writes` (`ICrmWriteLedger` in Core, `EfCrmWriteLedger` in Infrastructure; Spalten `Key` (PK, ≤ 200), `ExternalId` (HubSpot-ID), `CreatedAt`; Migration `CrmWrites`).

| Write | Schlüssel |
|---|---|
| Aufgabe | `task:<taskKey>` (der Task-Schlüssel des Kerns, z. B. `blocked:<prozess>:<zeitpunkt>`) |
| Audit-Notiz | `note:<prozess>:<typ>:<anzahl zustandswechsel>:<hash nachricht>` – ein Retry oder erneuter Lauf mit demselben Ereignis im selben Zustand ergibt dieselbe Notiz |
| Datei mit `ArchiveKey` (Datenkarte) | `file:<dealId>:<ArchiveKey>`, also `file:<dealId>:datacard:<prozess>:r<Revision>` – eine Datei je Schlüssel, unabhängig vom Inhalt |
| Datei ohne `ArchiveKey` (Antragsunterlagen) | `file:<dealId>:<hash dateiname>:<sha-256 inhalt>` – gleicher Name und Inhalt = keine zweite Datei |
| Alarm „Status abgelehnt“ | `alert:status:<prozess>:<zustand>` – ein Alarm je Vorgang und Zustand |
| Ablage-Notiz | `file-note:<dealId>:<hash der datei-schlüssel>` – dieselben Dateien = keine zweite Notiz |
| Artikel (Auftrag) | `lineitem:<dealId>:<signatur>:<index>` – je Position der unterschriebenen Variante genau ein Artikel |
| Auftrag übertragen | `order:<dealId>` → Signatur-ID; eine andere Signatur am selben Deal schreibt nichts (nur Aufgabe) |
| Auftrags-PDF | Ablage-Schlüssel `order-pdf:<signatur>` → `file:<dealId>:order-pdf:<signatur>` |
| Alarme Auftrag | `alert:order:<projekt>:not-found`, `alert:order:<projekt>:<signatur>:no-deal`, `…:deal-not-found` |

Hashes (SHA-256, gekürzt auf 16 Hex-Zeichen) statt Klartext, weil Dateinamen und Nachrichten nicht in Schlüssel und Log gehören. Dateien und Ablage-Notiz hängen an der Deal-ID statt an der Prozess-ID, weil `IDocumentArchive` nur die Deal-ID bekommt; Deal und Vorgang sind 1:1 (eindeutiger Index auf `processes.CrmProjectId`).

Ablauf: Ledger prüfen → Treffer: nichts anlegen (Debug-Log) → sonst in HubSpot anlegen → **sofort** festhalten (ohne Abbruch-Token). `RecordAsync` schreibt per `INSERT … ON CONFLICT DO NOTHING` direkt in die Tabelle und speichert keine anderen, halb fertigen Änderungen des DbContexts mit.

**Verbleibende Lücken (bewusst, Dublette statt verlorenem Write):**
- Absturz oder DB-Ausfall genau zwischen erfolgreichem HubSpot-Request und Ledger-Eintrag → der Retry legt das Objekt ein zweites Mal an.
- HubSpot legt an, antwortet aber mit 5xx → der Backoff wiederholt den anlegenden `POST` nicht, der Fehler geht an den Job; der Job-Retry findet keinen Ledger-Eintrag und legt ein zweites Mal an. (Bei 429 legt HubSpot nichts an, dort wiederholt der Backoff gefahrlos.)
- Der Notiz-Schlüssel hängt an `AnmeldeProcess.Transitions.Count`; die Historie wird deshalb nie gekürzt.
- Objekttypen ohne vordefinierten Verknüpfungstyp: Abbruch zwischen Anlegen und Verknüpfen → unverknüpftes Objekt, der Retry legt ein neues an.

Der Status-PATCH braucht kein Ledger (schreibt immer den aktuellen Stand).

## Fehlerbehandlung

- **Fachlich** (Deal nicht gefunden, Felder fehlen/ungültig, keine Vollmacht): `CrmProjectData.Problems` → Aggregator → HubSpot-Aufgabe. Kein Retry.
- **Technisch** (401/403/5xx nach Backoff, Netzwerk, beim Schreiben auch 4xx): `HttpRequestException` mit Pfad und Status, nie Body → Hangfire-Retry → danach „blockiert“ + Alarm. Lesen: 404 auf den Deal ist fachlich; beim Schreiben ist jeder Fehlerstatus technisch – Ausnahme Status-PATCH mit 4xx außer 429 (dauerhaft, Error-Log + einmaliger Alarm, siehe „Status-Properties“).
- MaStR-Login: fehlt eine der beiden Properties (am Deal oder in der Konfiguration) → `null` → Aufgabe „MaStR-Login fehlt“ (Kern).

**Alarm zuerst, HubSpot best effort** (Entscheidung Martin, 06.11.):
- **Blockieren** (`ProcessFailureHandler.BlockAsync`): Vorgang speichern → Alarm-Mail an Martin → Audit-Notiz → Status → Aufgabe. Jeder dieser Schritte ist einzeln abgefangen und wird nur geloggt (Write-Typ, Prozess-ID, Exception-Typ; Alarm auf Error, HubSpot-Writes auf Warning). Der Vorgang bleibt blockiert, die übrigen Schritte laufen weiter. Ist HubSpot die Ursache der Blockade, geht die Mail trotzdem raus.
- **Status** (`ICrmStatusSink`): best effort in `ProcessEngine` (nach jedem Zustandswechsel und am Ende jedes Laufs) und beim Resume. Ein Fehler wird geloggt, der Lauf geht weiter. Weil am Ende jedes Laufs der aktuelle Stand geschrieben wird, falls er im Lauf noch nicht erfolgreich ankam, holt der nächste Lauf einen gescheiterten PATCH nach (idempotent). Endzustände (`Completed`, `Cancelled`) laufen nicht mehr und werden nicht nachgeholt.
- **Audit-Notizen** bleiben nicht best effort: Scheitert die Notiz zu einem Zustandswechsel nach dem Speichern, bricht der Lauf ab (Retry), und der Retry sieht keinen Wechsel mehr – die Notiz fehlt dann in HubSpot. **Akzeptiert**; das technische Log (Serilog) enthält das Ereignis vollständig.
- **Aufgaben** außerhalb der Blockade (fachliche Probleme, Freigaben, Fristen) bleiben technisch: ein Fehler löst den Retry aus, das Ledger verhindert Dubletten.

## Logging

Strukturierte Templates. Information: jeder Write (Typ, Deal-ID, Schlüssel), Lesen Start/Ende mit Anzahl. Debug: übersprungene Writes (`WriteEnabled=false`, Ledger-Treffer, Audit-Stufe zu niedrig). Warning/Error: Fehler. Nie: Namen, Adressen, IBAN, E-Mail, Telefon, Zählernummern, Freitexte, Dateinamen, Notiz- und Aufgabeninhalte, Passwörter, Tokens, Response-Bodies. `DocumentRef.FileName` und `.Reference` sowie `HubSpotFile.Name` sind `[PersonalData]` und werden beim Destrukturieren maskiert.

## Webhook

`POST /webhooks/hubspot` (Signatur v3). Angenommen werden Property-Änderungen am konfigurierten Objekttyp: `deal.propertyChange` (klassisches Deal-Abo, Objekttyp `0-3`), `object.propertyChange` mit passendem `objectTypeId` und Ereignisse ohne `subscriptionType`. Andere Abos (z. B. `contact.propertyChange`, `deal.creation`) werden ignoriert. Start und Storno hängen an `dealstage` mit den Stage-IDs der Deal-Pipeline.

## Mapping-Regeln

- Betreiber-Felder leer → Betreiber = Kunde. Abweichender Betreiber: Vor- und Nachname aus den Betreiber-Feldern; E-Mail, Telefon und IBAN bleiben die des Kunden.
- **Betreiberadresse** (`CrmProjectData.Betreiberadresse` → `NabDataCard.Betreiberadresse`) = immer die Kundenadresse, auch bei abweichendem Betreiber (Entscheidung Martin, 06.11.; keine eigenen Betreiber-Adressfelder). Kein Pflichtfeld: fehlt die Kundenadresse bei abweichendem Anlagenort (oder ist sie dann nicht zerlegbar), meldet der Adapter nichts und der Aggregator nimmt die Anlagenadresse.
- Anlagenort leer → Kundenadresse. Getrennte Adressfelder haben Vorrang vor der einzeiligen Adresse; nicht zerlegbar → fachliches Problem. Endet das Straßenfeld auf eine Hausnummer und ist zugleich das Hausnummernfeld gesetzt → fachliches Problem (nur Feldnamen). Ein Land am Ende der einzeiligen Adresse („Deutschland“, „Germany“, „DE“) wird abgeschnitten, auch ohne Komma.
- Datum/Zeit: reine Datumswerte (`yyyy-MM-dd`, Epoch-ms auf Mitternacht UTC) gelten als Kalendertag, wie sie sind. Werte mit Uhrzeit (ISO 8601 mit Zeit, Epoch-ms ungleich Mitternacht UTC) werden in `HubSpot:TimeZone` (Default `Europe/Berlin`) umgerechnet, dann zählt der Kalendertag. Gilt für die Termine am Deal und für Datums-Trigger.
- Installationstermin = der spätere von Termin Elektroinstallation und Termin Dachmontage.
- Einspeiseart über `HubSpot:EinspeiseartValues`, leer = Teileinspeisung, unbekannter Wert = Problem. NH-Sicherung leer = 63 A.
- Errichter: Deal-Override (`ErrichterFirma` gesetzt) vor Sektion `Errichter` der Instanz; beides leer → fehlendes Feld.
- EVU-Vollmacht: neuester PDF-Anhang an Notizen/E-Mails des Deals, dessen Name `HubSpot:VollmachtFileNamePattern` enthält. Download erst beim Versand.

## MaStR-Passwort

Beschluss Termin 3 (26.10.): Passwort nach erfolgreicher Registrierung löschen. **Bewusste Abweichung** auf Martins Wunsch: Das Passwort bleibt am Deal, `DeleteAsync` loggt nur. Bestätigung durch Michael am 09.11. (Trello-Karte). Wird das Löschen doch beschlossen, setzt `DeleteAsync` die Passwort-Property per `PATCH` auf leer (nur mit `WriteEnabled`).

## Offen

- Interne Property-Namen und Stage-IDs fehlen bis zum Token (Struktur-Endpunkt); bis dahin leer in `instance.json`.
- Scopes in Michaels Anleitung: Deals, Notizen, E-Mails, Aufgaben, Dateien statt Projekte/Kontakte; für die Auftragsübernahme zusätzlich `crm.objects.line_items.read` / `.write`.
- **Endzustände holen keinen gescheiterten Status nach:** Scheitert der PATCH beim Wechsel nach `Completed` oder `Cancelled` (transient), läuft der Vorgang nicht mehr; HubSpot zeigt den vorigen Status, bis jemand eingreift (nur Warning im Log).
- **Kein erneuter Alarm nach gescheitertem Alarm:** Scheitert die Alarm-Mail beim Blockieren, wird das nur geloggt (Error); eine zweite Retry-Kette alarmiert nicht erneut, weil der Vorgang schon blockiert ist. Erinnerung nur über den Watchdog (Frist `Blocked`, 1 Tag), und dessen Aufgabe braucht ein erreichbares HubSpot.
- **Umlaute im Multipart-Dateinamen** (z. B. „Zählerantrag.pdf“) gegen echtes HubSpot prüfen: .NET schreibt zusätzlich `filename*` (RFC 5987); ob die Files API den Namen korrekt übernimmt, ist ungetestet.
- Download der Vollmacht und Einbindung ins Antragspaket kommt mit der PDF-Erzeugung.
- Abnahme der Abweichung „MaStR-Passwort nicht löschen“ durch Michael (09.11.).
