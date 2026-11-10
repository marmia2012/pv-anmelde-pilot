# Auftragsübernahme Reonic → HubSpot nach der Unterschrift

Stand: 10.11.2026 · intern. Plan: [2026-11-10-reonic-hubspot-auftrag.md](../plans/2026-11-10-reonic-hubspot-auftrag.md). Beschluss: Termin 4 (09.11.), ersetzt die bisherige Übertragung per KI. Einstellungen: [konfiguration.md](../konfiguration.md#reonic-reonic).

Unterschreibt ein Kunde ein Angebot in Reonic, überträgt der Autopilot die unterschriebene Variante an den HubSpot-Deal: alle Positionen als Artikel (Line Items) mit Bruttopreis, zusammengefasste `Auftrag*`-Properties, Unterschriftsdatum und das unterschriebene PDF. Eigener Job **außerhalb der Anmelde-State-Machine**; er startet keinen Vorgang (Trigger der Netzanmeldung bleibt die HubSpot-Pipeline).

## Bausteine

| Baustein | Klasse | Projekt |
|---|---|---|
| Modell, Interfaces | `SignedOrder`, `OrderPosition`, `OrderSummary`, `ISignedOrderSource`, `ICrmOrderSink`, `IOrderTransferScheduler`, `OrderPublishResult`, `OrderTransferResult`, `OrderSkipReasons` | Core `Orders/` |
| Ablauf | `OrderTransferService` | Core |
| Lesen | `ReonicSignedOrderSource`, `Mapping/ComponentLoader`, `Api/ReonicFileDownloader` | Reonic |
| Webhook | `Webhooks/ReonicWebhookValidator`, `Webhooks/ReonicWebhookParser`, `Endpoints/ReonicWebhookEndpoints` | Reonic, Host |
| Schreiben | `HubSpotOrderSink`, `Mapping/OrderPropertyMapper` | HubSpot |
| Job | `OrderTransferJob`, `HangfireOrderTransferScheduler` | Infrastructure |
| Admin | `POST /admin/reonic/projects/{projectId}/order-sync` | Host |

## Ablauf

1. **Webhook** `POST /webhooks/reonic`: Rohbody lesen → Signatur prüfen → `residentialOffer_signed` → Job einreihen → sofort 200. Alternativ der Admin-Endpunkt (synchron).
2. **Reonic lesen** (`ReonicSignedOrderSource`, nur GET):
   `GET /residentialProjects/{id}` (`integrations.hubspotDealId`) → `GET …/signatureRequests` (die gegebene, sonst die jüngste mit `status = signed`) → `GET …/variants/{signedVariantId}` → alle Positionen (alle Systeme + `optionalBundles` mit `selected`, Reihenfolge der Variante) → Katalog der relevanten Komponenten (`ComponentLoader`, wie bei der Netzanmeldung) → `ComponentMapper` für die Zusammenfassung.
3. **Prüfen** (`OrderTransferService`): Projekt fehlt → Alarm. Keine Deal-ID → Alarm. Signatur fehlt/nicht `signed` → Aufgabe. Keine Variante/Positionen → Aufgabe, nichts übertragen.
4. **PDF laden** über `signedPdfUrl` (eigener HttpClient ohne API-Key, nur https, ≤ 20 MB). Nicht abrufbar → Problem.
5. **HubSpot schreiben** (`HubSpotOrderSink`, immer am Deal `0-3`): Deal lesen → zweite Unterschrift? → fremde Artikel zählen → Artikel anlegen → Properties `PATCH` → PDF + Notiz → Ledger `order:<dealId>`.
6. **Probleme** → genau eine Aufgabe am Deal (`order-problems:<signatur>`), Text listet die Probleme (Produktnamen, keine Werte).

Die NAB-Datenkarte liest Komponenten weiter aus der Reonic-**Hauptvariante** (`primaryOfferVariantId`); die Artikel in HubSpot sind Anzeige. Die Umstellung der Netzanmeldung auf die unterschriebene Variante ist ein eigenes To-do.

## Mapping

**Artikel** (`POST crm/v3/objects/line_items`, Verknüpfung `HUBSPOT_DEFINED` 20 = Artikel → Deal, im selben Request). Freie Artikel ohne Produktbibliothek, alle Positionen auch nicht kundensichtbare:

| HubSpot | Reonic |
|---|---|
| `name` | „{brand} {name}“ |
| `quantity` | `quantity` |
| `price` | `unitSalesPrice.gross` (Bruttopreis je Stück); fehlt er → Artikel ohne Preis + Problem |
| `hs_sku` | `articleNumber` (wenn vorhanden) |
| `description` | `description` (Langtext, wenn vorhanden) |
| `hs_position_on_quote` | Index in der Variante (0-basiert) |

Einkaufspreise (`unitPurchasePrice`, `totalPurchasePrice`) stehen nicht im DTO und werden nie gelesen. Der Variantenpreis gesamt ist veraltet und wird nicht genutzt; Rabatte und zahlungsoptionsabhängige Preise bildet der Artikel nicht ab.

**Properties** am Deal (`HubSpot:Properties`, leerer Name = nicht geschrieben). Ja/Nein immer; Texte und Zahlen nur, wenn vorhanden (ein fehlender Wert überschreibt nichts):

| Schlüssel | Wert | Format |
|---|---|---|
| `AuftragUnterschriftsdatum` | `signedAt` | Kalendertag in `HubSpot:TimeZone`, `yyyy-MM-dd` |
| `AuftragKwp` | Σ Wp × Anzahl / 1000 | Zahl, Punkt, ≤ 3 Nachkommastellen |
| `AuftragModule` | „40 × Hersteller Modell (450 Wp)“ | mehrere mit „; “ |
| `AuftragWechselrichter` | „1 × Hersteller Modell (10 kW)“ | mehrere mit „; “ |
| `AuftragWrLeistungKw` | Σ AC-Nennleistung × Anzahl / 1000 | Zahl |
| `AuftragSpeicherVorhanden` / `AuftragWallboxVorhanden` | ja/nein | `true` / `false` |
| `AuftragSpeicher`, `AuftragSpeicherKwh` | „Hersteller Modell (10.24 kWh)“, Nennkapazität | Text, Zahl |
| `AuftragWallbox`, `AuftragWallboxKw` | „1 × Hersteller Modell (11 kW)“, Ladeleistung je Stück | Text, Zahl |

Zusammenfassung nach den Regeln des Reonic-Adapters (gleiche Komponenten addiert, All-in-one zählt als WR + Speicher, nur der erste Speicher-/Wallbox-Typ).

**PDF:** `IDocumentArchive` (Datei privat in `/pv-autopilot/<dealId>` + Notiz am Deal), Dateiname „Auftrag unterschrieben &lt;yyyy-MM-dd&gt;.pdf“ ohne Kundennamen, Ablage-Schlüssel `order-pdf:<signatur>`.

## Idempotenz

| Write / Alarm | Schlüssel (`crm_writes`) |
|---|---|
| Artikel | `lineitem:<dealId>:<signatur>:<index>` (über `HubSpotWriteOnce`) |
| PDF | `file:<dealId>:order-pdf:<signatur>`, Notiz `file-note:<dealId>:<hash>` |
| Übertragung abgeschlossen | `order:<dealId>` → ExternalId = Signatur-ID |
| Aufgabe Probleme | `task:order-problems:<signatur>` |
| Aufgabe zweite Unterschrift | `task:order-second-signature:<signatur>` |
| Aufgabe fremde Artikel | `task:order-foreign-items:<signatur>` |
| Aufgabe nicht unterschrieben | `task:order-not-signed:<projekt>:<signatur\|none>` |
| Alarm Projekt fehlt | `alert:order:<projekt>:not-found` |
| Alarm keine Deal-ID | `alert:order:<projekt>:<signatur\|none>:no-deal` |
| Alarm Deal existiert nicht | `alert:order:<projekt>:<signatur>:deal-not-found` |

- Ein Angebot wird nur einmal unterschrieben und **nie ersetzt oder gelöscht**. Gleiche Signatur-ID erneut (Reonic-Wiederholung, Retry, Admin) → Ledger-Treffer, nichts doppelt; der Properties-`PATCH` ist idempotent.
- **Zweite, andere Unterschrift** am selben Deal (`order:<dealId>` ≠ Signatur): nichts schreiben, nur Aufgabe.
- **Fremde Artikel** = verknüpfte Line Items minus die eigenen dieser Signatur (Ledger). Mehr als 0 → eine Aufgabe; die eigenen werden trotzdem angelegt, fremde bleiben unverändert.
- Nebenläufigkeit: je Reonic-Projekt ein Lauf (`IProcessRunLock` mit `OrderTransferJob.LockId` = Guid aus `order:<projekt>`); belegt → Job in 1 min neu, Admin-Endpunkt 409.
- Verbleibende Lücken wie im [HubSpot-Adapter](hubspot-adapter.md#idempotenz): Absturz zwischen Anlegen und Ledger, 5xx nach dem Anlegen → Dublette möglich.

## Fehlerfälle

| Fall | Behandlung |
|---|---|
| Reonic-Projekt nicht gefunden (404) | einmaliger Alarm, Ende |
| `integrations.hubspotDealId` leer | einmaliger Alarm (ohne Deal keine Aufgabe), Ende |
| HubSpot-Deal existiert nicht (404) | einmaliger Alarm, kein Retry |
| Signatur nicht gefunden / Status ≠ `signed` | Aufgabe am Deal, nichts übertragen |
| keine `signedVariantId`, Variante 404, keine Positionen | Aufgabe, nichts übertragen |
| Position ohne Bruttopreis, kein bzw. nicht abrufbares PDF, Katalogprobleme | Übertragung läuft, eine Aufgabe mit allen Problemen |
| technisch (5xx nach Backoff, Netzwerk, 401/403, Schreibfehler) | Exception → Hangfire-Retry (30 s/2/10/30 min); letzter Versuch → Alarm-Mail (Projekt-ID, Signatur-ID, Exception-Typ), Job endet ohne Fehler |
| Webhook `residentialOffer_signatureWithdrawn` | ignoriert (Info-Log) |

Ohne `HubSpot__WriteEnabled=true` schreibt nichts nach HubSpot (auch keine Aufgaben); Ergebnis `Written=false`, `SkipReason=WriteEnabled=false` mit Vorschau (Anzahl Artikel, Property-Namen, PDF ja/nein, fremde Artikel). Gelesen werden Deal und Artikel-Verknüpfungen. Alarme gehen unabhängig davon (Betreiber, nicht HubSpot).

## Webhook

`POST /webhooks/reonic`, Header `X-Reonic-Event`, `X-Reonic-Event-Id`, `X-Reonic-Timestamp` (Unix-Sekunden), `X-Reonic-Signature` = `sha256=<hex>` = HMAC-SHA256 mit `Reonic:WebhookSecret` über `${timestamp}.${rawBody}` (Rohbytes), Vergleich in konstanter Zeit, Zeitstempel höchstens ±5 min.

| Fall | Antwort |
|---|---|
| Secret leer, Header fehlt, Signatur falsch, Zeitstempel zu alt | 401 |
| ungültiges JSON | 400 |
| `residentialOffer_signed` ohne `projectId`/`signatureRequestId` | 400 |
| `residentialOffer_signed` | Job eingereiht, 200 |
| `residentialOffer_signatureWithdrawn`, andere Typen (inkl. `test`) | 200 ohne Job |

Ohne Secret startet der Host trotzdem; das Start-Log warnt einmal „Reonic-Webhook deaktiviert“. Reonic erwartet eine Antwort < 5 s und wiederholt bis zu 2 Tage.

## Admin-Endpunkt

`POST /admin/reonic/projects/{projectId}/order-sync?signatureRequestId=` (Header `X-Admin-Key`): führt die Übernahme synchron aus und antwortet mit `OrderTransferResult` (Projekt-, Signatur-, Deal-ID, `written`, `skipReason`, `lineItemCount`, `propertyNames`, `pdfStored`, `foreignLineItems`, `problems`). Ohne `signatureRequestId` die jüngste unterschriebene. Technischer Fehler → 502 mit Pfad und Status. Mit `WriteEnabled=false` reine Vorschau, lesend gegen echtes Reonic möglich.

## Logging

Information: Start/Ende je Schritt mit Projekt-, Signatur-, Deal-ID und Anzahlen; Debug: übersprungene Writes; Warning: Übersprungenes/Unvollständiges; Error: letzter Retry gescheitert. Nie: Namen, Preise, Artikeltexte, Dateinamen, URLs (`signedPdfUrl` ist `[PersonalData]`), Bodies.

## Einrichtung

- **Reonic:** Webhook-URL `https://<host>/webhooks/reonic`, Event `residentialOffer_signed` (optional `residentialOffer_signatureWithdrawn`), Secret als `Reonic__WebhookSecret` in Railway. Der API-Key braucht Leserechte auf Projekte inkl. Signaturanfragen.
- **HubSpot:** Scopes zusätzlich `crm.objects.line_items.read` und `crm.objects.line_items.write`. `Auftrag*`-Properties am Deal anlegen (Michael; Typen Datum, Zahl, Text, Checkbox) und in `instance.json` eintragen.
- **Bisherige KI-Übertragung abschalten** (Michael), sonst doppelte Artikel; die Aufgabe „fremde Artikel“ macht Dubletten sichtbar.

## Offen

- `integrations.hubspotDealId` nach dem Zusammenführen doppelter Deals gegen echte Daten prüfen (Reonic-Deal ist Master).
- Paginierung von `signatureRequests` ist nicht berücksichtigt (eine Seite angenommen).
- Gültigkeit von `signedPdfUrl` ist nicht dokumentiert → Download direkt im Job.
- Netzanmeldung auf die unterschriebene Variante umstellen (eigenes To-do).
