# Reonic-Adapter: interne Checkliste und Datenabruf

Stand: 03.11.2026 · intern (nicht für reinsolar). Code: `src/Sources/PvAutopilot.Sources.Reonic/`.
Das Dokument für Michael ist [reonic-checkliste.md](reonic-checkliste.md). Der Adapter setzt dessen **Empfehlung** um, nicht die vollständige 71-Felder-Tabelle. Bestätigt Michael die Empfehlung nicht, muss der Adapter angepasst werden (siehe „Offen“).

## Umgesetzte Variante (keine Doppelstrukturen)

| Daten | Quelle | Konzept |
|---|---|---|
| Module, WR, Speicher, Wallbox inkl. Hersteller, Modell, Anzahl | Hauptvariante (`primaryOfferVariantId`) → `systems.*.lineItems` + ausgewählte `optionalBundles` | 4.1 |
| Wp, AC-Leistung, Phasen, Kapazität, Ladeleistung | Katalog, **angebotene** Komponentenversion (`componentVersionId`) | 4.1 |
| Datenblätter | Katalog `datasheetUrl` | 4.1 / 4.7 |
| Hauptzählernummer | Projektfeld `meterNumber` | 4.3 |
| Kaskade, Zähler 1–4, Neuerrichtung/Erweiterung, Messkonzept, Fotos, Schaltplan | Checkliste Phase `offer` (Konzept #2–33, #67) | Abschnitt 2 |
| Formularwerte aus Datenblättern (kVA, Kopplung, nutzbare kWh, Inselnetz …) | noch nicht umgesetzt (Produktstamm + KI) | 4.7 |

Damit entfallen in der Checkliste #1, #34–66 und #68–71. Die Checkliste ist in `Checklist/NetzanmeldungChecklist.cs` beschrieben (Keys, Titel-Fallback, Auswahlwerte).

## Ablauf `ReonicPlanningDataSource`

1. `GET /residentialProjects/{id}` → `meterNumber`, `primaryOfferVariantId`
2. `GET /residentialProjects/{id}/variants/{primaryOfferVariantId}`
3. je eindeutiger Komponentenversion **relevanter Typen** `GET /components/{componentId}/versions/{versionId}` (Module, WR, Mikro-WR, All-in-one, Speicher, Wallbox; Montage, Kabel, Optimierer usw. werden nicht geladen)
4. `GET /checklists/{id}?stage=<Reonic:ChecklistStage>`

Alle Requests mit `X-Authorization`. `Reonic-Cache-Control: no-cache` nur für Projekt, Variante und Checkliste; Komponentenversionen sind unveränderlich und dürfen aus dem Reonic-Cache kommen (schont das „uncached“-Limit). Backoff bei 429/5xx: 2/4/8/16 s, längeres `Retry-After` gewinnt, höchstens 60 s je Wartezeit; das HttpClient-Timeout liegt über der Summe. Fehlt `Reonic__ApiKey` oder endet `BaseUrl` nicht auf `/`, startet der Host nicht.

## Unterschriebener Auftrag (`ReonicSignedOrderSource`)

Für die Auftragsübernahme nach HubSpot ([reonic-hubspot-auftrag.md](reonic-hubspot-auftrag.md)), rein lesend:

1. `GET /residentialProjects/{id}` → zusätzlich `integrations.hubspotDealId`
2. `GET /residentialProjects/{id}/signatureRequests` → gegebene Signatur, sonst die jüngste mit `status = signed`
3. `GET /residentialProjects/{id}/variants/{signedVariantId}` → **alle** Positionen (alle Systeme + gewählte Zusatzpakete) mit `articleNumber`, `description`, `visibleToCustomer`, `unitSalesPrice` (brutto je Stück). Einkaufspreise stehen nicht im DTO.
4. Katalog der relevanten Typen über `Mapping/ComponentLoader` (dieselbe Logik wie bei der Hauptvariante, aus `ReonicPlanningDataSource` herausgezogen) → `ComponentMapper`

**Download `signedPdfUrl`** über `Api/ReonicFileDownloader`: eigener benannter HttpClient (`reonic-signed-files`) **ohne** `X-Authorization` und ohne Basis-URL – der API-Key darf nicht an den Host der signierten URL gehen. Nur `https`, höchstens 20 MB; 403/404/410, zu groß oder keine https-URL → `null` (fachlich). `signedPdfUrl` ist `[PersonalData]` und erscheint nie in Log oder Fehlermeldungen.

**Webhook** `POST /webhooks/reonic` (`Webhooks/ReonicWebhookValidator`, `ReonicWebhookParser`): HMAC-SHA256 mit `Reonic__WebhookSecret` über `${timestamp}.${rawBody}`, ±5 min. Ausgewertet wird nur `residentialOffer_signed`; Widerruf wird ignoriert.

## Fehlerbehandlung

- **Fachlich** (Daten fehlen oder sind unplausibel): `PlanningData.Problems`, daraus wird über den Aggregator ein HubSpot-Task. Kein Retry. Beispiele: keine Hauptvariante, Katalogwert fehlt, bedingtes Pflichtfeld leer, unbekannter Auswahlwert, Feld doppelt, mehrere Speicher.
- **404** auf Projekt, Variante, Komponente oder Checkliste gilt als fachlich (siehe oben).
- **Technisch** (401/403/5xx nach Backoff, Netzwerk): `HttpRequestException` → Hangfire-Retry → danach „blockiert“ + Alarm. Die Message enthält nur Pfad und Status, nie den Antwort-Body.
- Problemtexte enthalten Feldnamen und Produktnamen, keine Werte wie Zählernummern oder Freitexte.

## Mapping-Regeln

- Wechselrichter: `inverter`, `microinverter`, `allInOneEnergyStorageSystem` (Letzteres zusätzlich als Speicher). Optimierer und Zubehör werden ignoriert.
- Gleiche Komponenten aus mehreren Positionen werden zusammengefasst (Anzahl addiert).
- Speicher-Kapazität = `capacity` (Wh) × Menge / 1000. Das ist die **Nennkapazität**; die nutzbare für das MaStR folgt aus dem Datenblatt.
- Wallboxen behalten ihre Anzahl; gleiche Modelle werden zusammengefasst.
- Mehr als ein Speicher- oder Wallbox-Typ → fachliches Problem (Datenkarte kennt nur einen).
- `quantityUnit` ≠ `item` → fachliches Problem, nur bei relevanten Typen.
- Checkliste: Key aus `[key: …]` in der Beschreibung (Groß-/Kleinschreibung egal), sonst Titel-Fallback. Ein ausdrücklicher Key hat Vorrang vor einem Titel-Treffer in einem anderen Block. `markedIrrelevant` = leer. Zähler werden nur bis „Anzahl Zähler“ gelesen; Zählernummer ist bei „Neusetzung“ optional.
- Messkonzept wird in der kanonischen Schreibweise gespeichert. Anhänge außer Bild/PDF sind ein fachliches Problem.
- `DocumentRef.Reference` (signierte URLs auf Kundenfotos) ist als personenbezogen markiert und wird im Log maskiert.

## Offen

- Abnahme der Empfehlung durch Michael (09.11.). Falls die Planung in Reonic die Komponenten nicht vollständig enthält: Alternative 4.2 (Auswahlliste + Produktstamm) → zusätzliches Mapping der Felder #34–66.
- Gültigkeit der Checklisten-Anhang-URLs ist nicht dokumentiert → Anhänge beim Aggregieren herunterladen und selbst ablegen (Storage fehlt noch).
- Test gegen echte Daten mit dem Read-only-Key (Requests in `reonic-checkliste-vorschlag.md`, Abschnitt 6.3).
