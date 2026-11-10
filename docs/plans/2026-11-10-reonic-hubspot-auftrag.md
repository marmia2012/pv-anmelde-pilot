# Plan: Auftragsdaten Reonic → HubSpot nach der Unterschrift

Stand: 10.11.2026 · Branch `feature/reonic-hubspot-order`

## Ziel

Sobald ein Kunde ein Angebot in Reonic unterschreibt, überträgt der Autopilot die Auftragsdaten der unterschriebenen Variante an den HubSpot-Deal: alle Positionen als Artikel (Line Items) mit Bruttopreis, zusammengefasste Komponenten-Properties, das Unterschriftsdatum und das unterschriebene Auftrags-PDF. Das ersetzt die bisherige Übertragung per KI (Beschluss Termin 4, 09.11.).

## Festgelegt (Klärung mit Martin, 10.11.)

| Thema | Entscheidung |
|---|---|
| Trigger | Reonic-Webhook `residentialOffer_signed` (HMAC-signiert). Dazu ein Admin-Endpunkt zum manuellen Anstoßen und Nachholen je Projekt |
| Ziel in HubSpot | **Beides:** Artikel (Line Items) am Deal **und** zusammengefasste Properties am Deal |
| Artikel | **Alle** Positionen der unterschriebenen Variante (alle Systeme + gewählte Zusatzpakete), auch nicht kundensichtbare, **mit Bruttopreis** je Stück (`unitSalesPrice.gross`). Einkaufspreise werden nie gelesen oder übertragen |
| Artikelart | Freie Artikel ohne Bezug zur HubSpot-Produktbibliothek |
| Zusätzlich | Unterschriebenes Auftrags-PDF (`signedPdfUrl`) als Datei + Notiz am Deal · abgeleitete Werte (kWp gesamt, WR-Leistung gesamt, Speicher ja/nein, Wallbox ja/nein) · Unterschriftsdatum |
| Fremde Artikel | Artikel am Deal, die nicht vom Autopiloten stammen, bleiben unverändert; gibt es welche, bekommt der Deal-Inhaber **eine** Aufgabe zur Prüfung. Die eigenen Artikel werden trotzdem angelegt |
| Mehrfach-Unterschrift | Ein Angebot kann nur einmal unterschrieben werden (Martin). Es wird **nie ersetzt oder gelöscht**. Je Unterschrift genau eine Übertragung. Kommt wider Erwarten eine zweite, andere Unterschrift zum selben Deal: nichts schreiben, nur Aufgabe |
| Deal finden | **Nur** `integrations.hubspotDealId` am Reonic-Projekt. Fehlt sie oder gibt es den Deal nicht → einmalige Alarm-Mail an Martin (ohne Deal keine Aufgabe möglich) |
| Widerruf | Webhook `residentialOffer_signatureWithdrawn` wird **ignoriert** (nur Info-Log) |
| Netzanmeldung | `ReonicPlanningDataSource` bleibt bei der Hauptvariante (`primaryOfferVariantId`). Die Umstellung auf die unterschriebene Variante ist ein eigenes To-do |
| Prüfung | Unit-Tests mit Fakes + Admin-Endpunkt mit Vorschau (bei `WriteEnabled=false` schreibt er nichts, lesend gegen echtes Reonic möglich) |
| Schreiben | Nur mit `HubSpot__WriteEnabled=true` (Default `false`). Reonic bleibt rein lesend |

## Bezug zu Konzepten und Beschlüssen

- **Termin 4 (09.11.):** neuer vorgelagerter Schritt „nach der Unterschrift in Reonic überträgt der Autopilot die Auftragsdaten nach HubSpot“; Trigger der Netzanmeldung bleibt die HubSpot-Pipeline → die Übertragung ist ein **eigener Job außerhalb der Anmelde-State-Machine**, sie startet keinen Vorgang.
- **02-architektur:** Adapter je Datenquelle, Kundenspezifisches in Konfiguration → neue Interfaces in Core (`ISignedOrderSource`, `ICrmOrderSink`), Reonic- bzw. HubSpot-Implementierung im jeweiligen Adapter, Property-Namen in `HubSpot:Properties`.
- **hubspot-adapter.md:** Schreiben nur über Sinks mit `WriteEnabled`, Idempotenz über `crm_writes` (`HubSpotWriteOnce`), anlegende POSTs nur bei 429 wiederholen, Fehler fachlich → Aufgabe, technisch → Retry + Alarm. Wird beibehalten und um Line Items ergänzt.
- **reonic-adapter.md:** Backoff 2/4/8/16 s, 404 = fachlich, keine Bodies im Log. Wird beibehalten.
- **reonic-checkliste-vorschlag.md, Abschnitt 4.1/6:** Webhook-Signatur (`X-Reonic-Signature`, `X-Reonic-Timestamp`), `signatureRequests`, `integrations.hubspotDealId`. Empfehlung dort: „Reonic einzige Komponentenquelle, HubSpot nur zur Anzeige“ → passt: die NAB-Datenkarte liest die Komponenten weiter aus Reonic, die Artikel in HubSpot sind Anzeige.
- **Abweichung:** keine. Hinweis: Die bisherige KI-Übertragung muss Michael abschalten, sonst entstehen doppelte Artikel (offener Punkt).

## Reonic-API (aus der OpenAPI-Spezifikation, Stand 10.11.)

- `GET /residentialProjects/{id}` → zusätzlich `integrations.hubspotDealId`
- `GET /residentialProjects/{id}/signatureRequests` → `data[]`: `id`, `status` (`awaitingSignature`, `signed`, `expired`, `withdrawn`, `revokedByCustomer`), `signedVariantId`, `signedPdfUrl`, `signedAt`
- `GET /residentialProjects/{id}/variants/{variantId}` → `systems.{solar,batteryStorage,evCharger,heating,additional}.lineItems[]` und `optionalBundles[]` (`selected`); je Position `componentType`, `name`, `brand`, `articleNumber`, `description`, `quantity`, `visibleToCustomer`, `unitSalesPrice {net, gross, vat, vatRate}`. Der Variantenpreis gesamt ist veraltet und wird nicht genutzt.
- Webhook-Body `residentialOffer_signed`: `{ version: 1, type, occurredAt, data: { projectId, signatureRequestId, signedVariantId, signedPaymentOptionId } }`. Header `X-Reonic-Event`, `X-Reonic-Event-Id`, `X-Reonic-Timestamp` (Unix-Sekunden), `X-Reonic-Signature` = `sha256=<hex>` über `${timestamp}.${rawBody}`; Zeitstempel älter als 5 Minuten ablehnen; Antwort < 5 s, Reonic wiederholt bis zu 2 Tage.

## Schritte

### 1. Core: Auftragsmodell und Interfaces

Dateien: `src/PvAutopilot.Core/Orders/SignedOrder.cs`, `OrderInterfaces.cs`

- `SignedOrder` (record): `PlanningProjectId`, `SignatureRequestId`, `CrmProjectId` (nullable, aus der Quelle), `SignedAt`, `VariantId`, `Positions` (`OrderPosition`: `ComponentType`, `Brand`, `Name`, `ArticleNumber?`, `Description?`, `Quantity`, `UnitPriceGross`, `VatRate?`), `Summary` (`Module`, `Wechselrichter`, `Speicher?`, `Wallbox?` mit den vorhandenen DataCard-Typen, abgeleitet `LeistungKwp`, `WechselrichterLeistungKw`), `SignedPdf` (`DocumentRef?`), `Problems`.
- `ISignedOrderSource`: `GetSignedOrderAsync(planningProjectId, signatureRequestId?, ct)` → `SignedOrder?` (`null` = Projekt nicht gefunden); `DownloadAsync(DocumentRef, ct)` → `byte[]?`.
- `ICrmOrderSink`: `PublishAsync(SignedOrder, byte[]? signedPdf, ct)` → `OrderPublishResult` (`Written`, `SkipReason`, `LineItemCount`, `PropertyNames`, `PdfStored`, `ForeignLineItems`, ohne Werte).
- `DocumentKind` um `Auftrag` erweitern.

### 2. Reonic: unterschriebenen Auftrag lesen

Dateien: `Api/ReonicDtos.cs`, `Api/ReonicApiClient.cs`, neu `ReonicSignedOrderSource.cs`, neu `Mapping/ComponentLoader.cs` (aus `ReonicPlanningDataSource` herausgezogen), `DependencyInjection.cs`

- DTOs: `ReonicProject.Integrations.HubspotDealId`; `ReonicSignatureRequest`; `ReonicLineItem` um `ArticleNumber`, `Description`, `VisibleToCustomer`, `UnitSalesPrice` erweitern. **Keine** Einkaufspreis-Felder im DTO.
- Client: `GetSignatureRequestsAsync`. Download des `signedPdfUrl` über einen **eigenen HttpClient ohne `X-Authorization`** (signierte URL, der API-Key darf nicht an Dritt-Hosts gehen), nur `https`, Größenlimit 20 MB.
- `ComponentLoader`: lädt je eindeutiger Komponentenversion der relevanten Typen den Katalog (bestehende Logik, Verhalten von `ReonicPlanningDataSource` unverändert).
- `ReonicSignedOrderSource`: Projekt → Signatur (gegebene ID, sonst die jüngste mit `status = signed`) → Variante `signedVariantId` → alle Positionen (alle Systeme + `optionalBundles` mit `selected`) → `ComponentMapper` für die Zusammenfassung.
- Fachliche Probleme (`Problems`, ohne Werte): Signatur nicht gefunden, Status nicht `signed`, keine `signedVariantId`, Variante nicht gefunden, Position ohne Bruttopreis, kein `signedPdfUrl`, Katalogprobleme aus `ComponentMapper`.
- Logging: Information Start/Ende (Projekt-ID, Signatur-ID, Anzahl Positionen, Anzahl Probleme); keine Namen, Preise, URLs.

### 3. Reonic-Webhook (heikel: Sicherheit)

Dateien: neu `src/Sources/PvAutopilot.Sources.Reonic/Webhooks/ReonicWebhookValidator.cs`, `ReonicWebhookParser.cs`, `ReonicOptions.cs`, neu `src/PvAutopilot.Host/Endpoints/ReonicWebhookEndpoints.cs`, `Program.cs`

- `POST /webhooks/reonic`: Rohbody lesen → Signatur prüfen (HMAC-SHA256 über `${timestamp}.${rawBody}`, Vergleich in konstanter Zeit, Zeitstempel ±5 min gegen `TimeProvider`) → bei Fehler 401.
- Ohne `Reonic:WebhookSecret` lehnt der Endpunkt alles mit 401 ab; der Start loggt einmal Warning („Reonic-Webhook deaktiviert“). Kein Startabbruch, weil das Feature optional ist.
- Parser: `residentialOffer_signed` → `(projectId, signatureRequestId)` → Hangfire-Job einreihen, Antwort 200 sofort. `residentialOffer_signatureWithdrawn` → Info-Log „nicht ausgewertet“, 200. Andere Typen (inkl. `test`) → Debug-Log, 200. Ungültiges JSON → 400.
- Logging: Event-Typ, Event-ID, Projekt-ID, Signatur-ID; nie Body.

### 4. Übertragung als Job (heikel: Idempotenz, Nebenläufigkeit)

Dateien: neu `src/PvAutopilot.Core/Orders/OrderTransferService.cs`, neu `src/PvAutopilot.Infrastructure/Jobs/OrderTransferJob.cs`, DI in Core/Infrastructure

- `OrderTransferService.TransferAsync(planningProjectId, signatureRequestId?, ct)`:
  1. Auftrag lesen; `null` → einmaliger Alarm (`alert:order:<projekt>:not-found`), Ende.
  2. `CrmProjectId` leer → einmaliger Alarm (`alert:order:<projekt>:<signatur>:no-deal`), Ende.
  3. Fehlt die Signatur oder ist sie nicht `signed` → einmaliger Alarm bzw. Aufgabe am Deal (falls Deal bekannt), Ende.
  4. PDF herunterladen (falls vorhanden).
  5. `ICrmOrderSink.PublishAsync`.
  6. Weitere fachliche Probleme → **eine** Aufgabe am Deal (`order-problems:<signatur>`), Text listet die Probleme (Produktnamen, keine Werte).
- `OrderTransferJob`: `[AutomaticRetry(Attempts = 4, DelaysInSeconds = 30/120/600/1800)]`; je Reonic-Projekt höchstens ein Lauf gleichzeitig über das vorhandene `IProcessRunLock` mit einer aus `order:<projekt>` abgeleiteten Guid; belegt → in 1 min neu einplanen (Muster `ProcessJob`). Letzter Versuch scheitert → Alarm-Mail an Martin (Projekt-ID, Signatur-ID, Exception-Typ), Job endet ohne Fehler.
- Deal nicht gefunden (404 in HubSpot) → fachlich: einmaliger Alarm, kein Retry.

### 5. HubSpot: Artikel, Properties, PDF (heikel: Idempotenz)

Dateien: `Api/HubSpotApiClient.cs` (Verknüpfung `line_items` → Deal = 20), neu `HubSpotOrderSink.cs`, neu `Mapping/OrderPropertyMapper.cs`, `HubSpotPropertyKeys.cs`, `DependencyInjection.cs`

- `WriteEnabled=false` → nichts schreiben, Debug-Log, Ergebnis mit `Written=false`, `SkipReason="WriteEnabled=false"` und der Vorschau (Anzahl Artikel, konfigurierte Property-Namen, PDF ja/nein). Fremde Artikel werden auch in der Vorschau gezählt (nur lesend).
- **Zweite Unterschrift:** Ledger `order:<dealId>` hält die Signatur-ID der ersten Übertragung. Andere Signatur-ID → nichts schreiben, Aufgabe `order-second-signature:<signatur>`. Gleiche ID → normal weiter (Retry/Wiederaufnahme).
- **Fremde Artikel:** verknüpfte Line Items des Deals lesen, eigene (Ledger `lineitem:<dealId>:<signatur>:<index>`) abziehen; Rest > 0 → einmalige Aufgabe `order-foreign-items:<signatur>`.
- **Artikel:** je Position über `HubSpotWriteOnce`, Schlüssel `lineitem:<dealId>:<signatur>:<index>` (Index = Reihenfolge in der Variante). Properties: `name` = „{Hersteller} {Name}“, `quantity`, `price` = Bruttopreis je Stück, `hs_sku` = Artikelnummer, `description` = Reonic-Langtext, `hs_position_on_quote` = Index. POST nur bei 429 wiederholt.
- **Properties** (`PATCH`, idempotent, leerer Name = nicht geschrieben): `AuftragUnterschriftsdatum` (Kalendertag in `HubSpot:TimeZone`, `yyyy-MM-dd`), `AuftragKwp`, `AuftragModule`, `AuftragWechselrichter`, `AuftragWrLeistungKw`, `AuftragSpeicherVorhanden`, `AuftragSpeicher`, `AuftragSpeicherKwh`, `AuftragWallboxVorhanden`, `AuftragWallbox`, `AuftragWallboxKw`. Texte z. B. „40 × Hersteller Modell (450 Wp)“, mehrere mit „; “; Zahlen invariant mit Punkt; Booleans `true`/`false`.
- **PDF:** über `IDocumentArchive.StoreAsync` mit `ArchiveKey = order-pdf:<signatur>`, Dateiname „Auftrag unterschrieben <yyyy-MM-dd>.pdf“ (ohne Kundennamen).
- Zum Schluss Ledger `order:<dealId>` = Signatur-ID.
- Logging: Information je Schritt (Deal-ID, Signatur-ID, Anzahl); nie Namen, Preise, Texte, Dateinamen.

### 6. Admin-Endpunkt

Datei: `src/PvAutopilot.Host/Endpoints/AdminEndpoints.cs`

- `POST /admin/reonic/projects/{projectId}/order-sync?signatureRequestId=` (Header `X-Admin-Key`) führt `OrderTransferService` synchron aus und gibt das Ergebnis zurück: Projekt-ID, Signatur-ID, Deal-ID, `Written`, `SkipReason`, Anzahl Artikel, Property-Namen, PDF ja/nein, Anzahl fremder Artikel, Probleme (Produktnamen erlaubt, keine Werte). Technischer Fehler → 502 mit Pfad und Status.

### 7. Konfiguration und Doku

- `Reonic:WebhookSecret` (ENV `Reonic__WebhookSecret`, Default leer = Webhook aus) → `docs/konfiguration.md`, `config/instances/reinsolar/.env.example`.
- `HubSpot:Properties` um die `Auftrag*`-Schlüssel (leer in `instance.json`), Tabelle in `docs/konfiguration.md`, Abschnitt 5.
- HubSpot-Scopes: zusätzlich `crm.objects.line_items.read` / `.write`.
- Einrichtung in Reonic: Webhook-URL `https://<host>/webhooks/reonic`, Events `residentialOffer_signed` (+ optional `residentialOffer_signatureWithdrawn`).

## Tests

| Schritt | Abgesichertes Verhalten |
|---|---|
| 2 | Signatur per ID bzw. jüngste `signed`; Status ≠ `signed` / fehlende Variante / fehlender Preis / fehlendes PDF → Problem; alle Systeme + nur gewählte Zusatzpakete; Bruttopreis statt Netto; `hubspotDealId` übernommen; Download ohne `X-Authorization`, nur https; `ReonicPlanningDataSource` unverändert (bestehende Tests grün) |
| 3 | gültige Signatur → 200 + Job eingereiht; falsche Signatur, fehlender Header, alter Zeitstempel, leeres Secret → 401; Widerruf und fremde Typen → 200 ohne Job; ungültiges JSON → 400 |
| 4 | Projekt fehlt / kein Deal → genau ein Alarm auch bei Wiederholung; Probleme → genau eine Aufgabe; letzter Retry → Alarm statt Fehler; belegtes Lock → neu eingeplant |
| 5 | `WriteEnabled=false` → kein schreibender Request, Vorschau korrekt; Artikel mit Name/Menge/Bruttopreis/SKU und Verknüpfung 20; Wiederholung legt keine Artikel doppelt an; zweite Signatur → keine Writes, eine Aufgabe; fremde Artikel → eine Aufgabe, eigene trotzdem angelegt; Properties nur für konfigurierte Namen, Formate (Datum in Europe/Berlin, Punkt als Dezimaltrenner); PDF mit `ArchiveKey`; keine Preise, Namen oder URLs im Log |
| 6 | Admin-Key fehlt → 401; Ergebnis ohne personenbezogene Daten |

## Doku-Änderungen

- Neu `docs/konzept/reonic-hubspot-auftrag.md` (Ablauf, Mapping, Idempotenz-Schlüssel, Fehlerfälle, Einrichtung in Reonic)
- `docs/konzept/hubspot-adapter.md`: Sink `ICrmOrderSink`, Schlüssel `lineitem:*`, `order:*`, `order-pdf:*`, Scopes
- `docs/konzept/reonic-adapter.md`: unterschriebener Auftrag, Webhook, Download ohne API-Key
- `docs/context/02-architektur.md`: Baustein „Auftragsübernahme Reonic → HubSpot“
- `docs/context/05-status-offene-fragen.md`: Stand und offene Punkte
- `docs/konfiguration.md`, `config/instances/reinsolar/instance.json`, `.env.example`

## Annahmen und offene Punkte

- Die `Auftrag*`-Properties legt Michael am Deal an (Typen: Datum, Zahl, Text, Checkbox); bis dahin leer = nicht geschrieben.
- **Bisherige KI-Übertragung** muss Michael abschalten, sonst doppelte Artikel (die Aufgabe „fremde Artikel“ macht es sichtbar).
- `integrations.hubspotDealId` zeigt nach dem Zusammenführen doppelter Deals auf den richtigen Deal (Reonic-Deal ist Master) – gegen echte Daten prüfen.
- Bruttopreis je Stück aus Reonic; Rabatte oder zahlungsoptionsabhängige Preise bildet der Artikel nicht ab.
- Gültigkeit von `signedPdfUrl` ist nicht dokumentiert → Download direkt im Job.
- Später, wenn reinsolar HubSpot-Projekte nutzt: Artikel bleiben am Deal (Architektur: „Artikel bleiben am Deal“); `ObjectTypeId` bezieht sich dann auf das Projekt → hier bewusst fest der Deal (`0-3`) aus `hubspotDealId`.
