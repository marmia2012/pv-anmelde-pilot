# Reonic: Datenquellen und Checklisten-Vorschlag für die Netzanmeldung (Netze Duisburg)

Stand: 03.11.2026 · Grundlage: Reonic REST API v3 in Version **3.22.0** (OpenAPI-Spezifikation vom 02.11.2026), Reonic-Hilfecenter, Kontextdateien `docs/context/03-…` und `04-…`.
Status: **Entwurf zur Abstimmung mit Michael (Termin 09.11.).** Ohne API-Key erstellt, also nur aus Doku und Schema abgeleitet. Alles, was nur mit Key prüfbar ist, steht in Abschnitt 6.

> **Überholt in Teilen (Stand 03.11.):** Dieses Dokument ist die Recherche. Es nimmt an, dass Angebote in Reonic entstehen, und holt die Komponenten aus der unterschriebenen Variante. Entschieden ist inzwischen: Alle Daten kommen aus Reonic, die Komponenten über die Checkliste. Der aktuelle Checklisten-Aufbau steht in [reonic-checkliste.md](reonic-checkliste.md). Abschnitte 2.3 und 3 hier gelten nicht mehr. Abschnitte 1, 4.1 (Varianten/Katalog als Option), 5 und 6.3 bleiben gültig.

Legende für die Belastbarkeit:
- **[Schema]** steht so in der OpenAPI-Spezifikation (`https://api.reonic.com/rest/v3/openapi`).
- **[Doku]** steht so im Hilfecenter (docs.reonic.com).
- **[nicht verifiziert]** ist Annahme oder Ableitung und erst mit API-Key bzw. im Reonic-Portal prüfbar.
- **(Vorschlag)** ist ein Feld, das in Martins Ausgangsliste nicht vorkommt, für die Formulare aber nötig erscheint.

---

## 1. Kurzfassung: Was Reonic kann

### 1.1 Wichtigste Erkenntnisse

1. **Ausgefüllte Checklisten-Werte kommen strukturiert per API.** Das klärt die offene Frage aus `05-status`. `GET /checklists/{projectId}?stage=…` liefert `blocks → segments → items`, und jedes Item enthält seinen Wert: Freitext als `text`, Auswahl als `items[].checked`, Fotos/PDFs als `attachments[].url`, Unterschrift als `image`. Dazu kommen `required`, `markedIrrelevant` und die Abschluss-Zeitstempel je Segment. [Schema] Die PDF-Export-Route plus KI-Auslesen brauchen wir nur noch als Rückfallebene.
2. **Items haben keine stabilen IDs.** Blöcke und Segmente tragen eine `id` (UUID) und einen `versionHash`. Die einzelnen Items haben nur `type`, `title` und `description`. [Schema] → Wir mappen über Block-/Segmentname plus Item-Titel. Deshalb schlagen wir **technische Schlüssel in der Item-Beschreibung** vor (Abschnitt 4.1).
3. **Es gibt keinen Zahlen-, Datums- oder Boolean-Feldtyp.** Verfügbare Item-Typen laut Schema sind `checkbox` (Liste ankreuzbarer Punkte, also Mehrfachauswahl), `radio` (Einfachauswahl), `freetext`, `image` (Fotos/PDF/Video), `signature`, die reinen Anzeige-Typen `title` und `description` sowie die Referenz-Typen `plannedLayout`, `datasheets`, `componentList` und `circuitPlan`. [Schema] → Booleans bilden wir als `radio` Ja/Nein ab, Zahlen als `freetext` mit Validierung im Code.
4. **Keine wiederholbaren Gruppen.** Bedingungslogik ist in der Konfigurations-Doku nicht beschrieben. Die Anwender-Doku erwähnt zwar Items, die durch Bedingungslogik ausgeblendet werden [Doku], das Schema kennt aber kein Bedingungsfeld. [Schema] → Zähler 1–4 werden als **vier feste Segmente** angelegt. Bedingungen werden **im Code ausgewertet**, nicht in Reonic. Nicht zutreffende Items markiert der Monteur in Reonic als „irrelevant“. Diese Funktion gibt es, und sie kommt per API mit (`markedIrrelevant`, `markedIrrelevantReason`).
5. **Komponenten gehören nicht in die Checkliste.** Module, Wechselrichter, Speicher und Wallbox stehen samt Mengen in der Stückliste der **unterschriebenen Angebotsvariante**: `signatureRequests → signedVariantId → variants/{id} → systems.*.lineItems[]` mit `componentType`, `brand`, `name` und `quantity`. Die technischen Daten liefert der **Katalog** (`/components/{id}/versions/{versionId}`): Modul-`wattPeak`, WR-`nominalOutputPower` und `numberOfPhases`, Speicher-`capacity` (Wh) und `maxDischargingPower`, Wallbox-`maxChargingPower` sowie `datasheetUrl`. [Schema] → Die kWp-Summe lässt sich vollständig berechnen. Die offene Frage „WR als Liste in der Checkliste oder Artikel der AB?“ beantworten wir mit: **Artikel der unterschriebenen Variante in Reonic.**
6. **Der Projekt-Datensatz hat ein Feld `meterNumber`.** Beschreibung: „Electricity meter number at the customer's property“, per API les- und schreibbar. [Schema] → Kandidat für die **Hauptzählernummer**. Zu klären ist, ob reinsolar es heute befüllt und wo es in der Oberfläche steht [nicht verifiziert].
7. **Die Bestandsanlage ist teilweise strukturiert vorhanden:** `existingSystems.solar` (Baujahr, Leistung in Wp, Neigung, Ausrichtung) sowie `batteryStorage` und `evCharger`. Diese Werte gibt der Kunde in der Anfragephase an. [Schema] Sie taugen nur als Plausibilisierung, nicht als belastbare Quelle.
8. **Für den Schaltplan gibt es eine eigene Funktion („Circuit plan“).** Sie erzeugt einen Einlinien-Übersichtsschaltplan aus der Planung mit Strings, WR, Speicher (AC/DC), Wallbox, Hausanschluss und Zählern. Der Plan ist als **PDF/JPEG exportierbar** und lässt sich in die Projektdateien speichern. [Doku] Über die API kommt der Plan **nicht** als Datei aus der Checkliste: Das Item `circuitPlan` hat nur einen `title`. [Schema] → Der Schaltplan muss als PDF in die Projektdateien oder in ein Bild/PDF-Item der Checkliste.
9. **Netzanmeldung bietet Reonic als kostenpflichtigen Service** an (DE/IT/BR, Abrechnung je Fall). Die Doku-Seite dazu ist inzwischen offline (404). In der API gibt es dazu nur Aktivitätstypen (`gridRegistrationDeCreated/Updated`) und ein Nutzerrecht `applyForGridRegistration`, **keine Datenfelder**: keine Netzbetreiber-, Messkonzept- oder Zählerdaten. [Schema] → Für unsere Felder gibt es nichts zum Wiederverwenden.
10. **GET-Antworten werden bis zu 1 h gecacht.** Checklisten-Reads im Prozess müssen deshalb immer mit `Reonic-Cache-Control: no-cache` laufen, sonst arbeiten wir mit veralteten Werten. [Schema/Doku]

### 1.2 API-Eckdaten (v3)

| Thema | Inhalt | Quelle |
|---|---|---|
| Basis-URL | `https://api.reonic.com/rest/v3/…` | [Schema] |
| Auth | Header `X-Authorization: rnc_v3_…`. v2-Keys funktionieren nicht, v2 ist seit 01.10. abgeschaltet | [Schema] |
| Scopes | u. a. `projects:read`, `checklists:read`, `components:read`, `files:read`, `templates:read`; `*:read` deckt alles ab. Write schließt Read nicht ein | [Schema] |
| Rate-Limits | pro Workspace, Fenster 1 min: `cached` 600, `uncached` 120, `write` 60 (POST); dazu Burst 20/s. 429 mit `Retry-After` | [Schema] |
| Caching | GET bis 1 h gecacht; `Reonic-Cache-Control: no-cache` erzwingt frischen Read (zählt gegen `uncached`) | [Schema] |
| Pagination | `{ data, pagination: { next } }`, `itemsPerPage` max 200 | [Schema] |
| Datei-URLs | Download-URLs (Dateien, Katalog-Datenblätter, Angebots-PDFs) gelten **24 h** → kurz vor dem Download neu holen | [Schema] |
| Webhooks | HMAC-signiert (`X-Reonic-Signature`, Idempotenz über `X-Reonic-Event-Id`), Antwort < 5 s, Retries bis 2 Tage. Relevante Events: `projectChecklist_segmentCompletionChanged`, `projectChecklist_pdfGenerated`, `residentialOffer_signed`, `project_kanbanColumnChanged`, `file_created` | [Schema] |
| Beta | Checklisten-Read und Checklist-Templates sind **BETA** und können sich ändern | [Schema] |

Hinweis zum Backoff aus `CLAUDE.md`: 2/4/8/16 s passt. Zusätzlich sollte aber `Retry-After` beachtet werden (Maximum aus beiden Werten).

### 1.3 Checklisten-Modell im Detail

- **Je Projekt eine Checkliste pro Lebenszyklus-Phase:** `request` (Qualifizierung), `offer` (Beratung/Vor-Ort-Termin), `installation` (Baustellendokumentation) und seit 02.11.2026 `finished`. Ohne `stage`-Parameter liefert die API die Checkliste der **aktuellen** Phase. → Wir übergeben `stage` immer explizit. [Schema]
- **Struktur:** Checkliste → Blöcke (`name`, `position`, Farbe) → Segmente (`name`, `completedAt`, `completedById`) → Items. [Schema]
- **Item-Typen und Werte** [Schema]:

| Typ | Bedeutung im Editor [Doku] | Wert in der API |
|---|---|---|
| `checkbox` | „Checklist“: mehrere ankreuzbare Punkte | `items[]: { text, checked, required, markedIrrelevant }` |
| `radio` | „Selection“: Einfachauswahl (Radio oder Dropdown) | `items[]: { text, checked }` + `required` |
| `freetext` | „Free text“: ein- oder mehrzeilig | `text`, `rows`, `required` |
| `image` | „Images“: Foto-Anforderung, optional Beispielbild; erlaubt Bild, PDF, beides, Video oder alles | `items[]: { title, required, maxAttachments, allowedAttachmentType, attachments[]: { type, url } }` |
| `signature` | Unterschrift (Nutzer/Kunde/Andere) | `image`, `signingPersonType`, `signingPersonName`, `signedAt` |
| `title`, `description` | reine Anzeige | – |
| `datasheets`, `componentList`, `plannedLayout`, `circuitPlan` | Referenzinhalte aus der unterschriebenen Variante (für den Monteur) | nur `title`, **keine Inhalte** |

- **Pflichtfelder:** Laut Doku muss ein Pflichtelement gefüllt sein, bevor die Checkliste als abgeschlossen markiert werden kann. Ein Pflichtelement darf nur mit Begründung als „irrelevant“ markiert werden. [Doku]
- **Abschluss je Segment** („Mark segment complete“) mit Webhook `projectChecklist_segmentCompletionChanged`. [Doku/Schema] → Daraus lässt sich ein sauberes Signal „Netzanmeldungs-Daten vollständig“ ableiten.
- **Vorlagen-Versionierung:** Bestehende Projekte behalten ihre Vorlagenversion. Ein Update ist pro Projekt manuell möglich („Update checklists“, Daten werden zusammengeführt). [Doku] → Unser Mapping muss mehrere Vorlagenversionen parallel vertragen.
- **Checklisten-Vorlagen per API:** `/checklistTemplates` ist laut Schema-Beschreibung tatsächlich ein „reusable bundle of line items“, also ein Komponenten-Paket, und **nicht** die Frage-Struktur. Items können per v3 nicht bearbeitet werden. [Schema] → Die Checkliste legt Michael **manuell im Portal** an (Einstellungen > Checklisten). Per API ist das nicht möglich.
- **KI-Vorlagenerstellung:** Nur für Admins und nur nach Freischaltung durch den Support. Aus PDF, Bild oder CSV wird ein Vorlagenblock erzeugt. [Doku] → Michael könnte diese Datei bzw. eine CSV daraus als Vorlage verwenden, muss das Ergebnis aber nacharbeiten.

### 1.4 Weitere relevante Reonic-Bausteine

- **Angebotsvarianten/Stückliste** (`/residentialProjects/{id}/variants`): Positionen je System (`solar`, `batteryStorage`, `evCharger`, `heating`, `additional`) sowie optionale Pakete (`optionalBundles`, nur zu zählen, wenn `selected`). `componentVersionId` verweist auf den **angebotenen Stand** der Komponente. [Schema]
- **Unterschriften** (`/residentialProjects/{id}/signatureRequests`): `status`, `signedVariantId` und `signedPdfUrl` (unterschriebene AB als PDF). [Schema]
- **Katalog** (`/components`): Komponententypen `module`, `inverter`, `microinverter`, `optimizer`, `batteryStorage`, `allInOneEnergyStorageSystem`, `evCharger`, Zubehör usw. Zu jeder Komponente gibt es `datasheetUrl`, `warrantyUrl` und `instructionsUrl` (PDF). Der Hersteller ist nur der Freitext `brand`; eine eigene Hersteller-Ressource gibt es in v3 nicht. [Schema]
- **Dateien** (`/files`, `/fileFolders`): je Projekt mit Ordnern und dem Filter `createdByCustomer=true` für Kunden-Uploads. [Schema]
- **Kundenportal und Dateianfragen:** Über eine Dateianfrage („File request“) lädt der Kunde Fotos oder PDFs hoch, etwa ausdrücklich ein „Foto des Zählers“. Die Uploads landen in den Projektdateien, markiert als Kunden-Upload. **Textantworten sind nicht möglich**, und die Checkliste füllt der Kunde nicht aus. [Doku]
- **Benutzerdefinierte Felder (Custom Fields)** an Projekten gibt es in der v3-API **nicht**. Weder Schema noch Doku nennen sie. [Schema] → Was nicht im Standardmodell steht, muss in die Checkliste (oder nach HubSpot).
- **Integrationen:** Am Projekt stehen `integrations.hubspotDealId` u. a. Damit gibt es eine direkte Verknüpfung Reonic ↔ HubSpot-Deal. [Schema]
- **Messkonzept, Netzbetreiber, Zählerschrank-Daten und Speicherkopplung** sind als Datenfelder **nirgends** vorhanden. [Schema] Die Speicherkopplung erscheint nur grafisch im Schaltplan. [Doku]

---

## 2. Feldtabelle: beste Quelle je Feld

Quellen: **Checkliste** = Reonic-Projektcheckliste (Phase `offer`, falls nicht anders angegeben) · **Variante** = Stückliste der unterschriebenen Angebotsvariante · **Katalog** = Reonic-Komponente · **Projektfeld** = Standardfeld am Reonic-Projekt · **Datei** = Reonic-Projektdatei · **Produktstamm** = eigene Produktdaten (SharePoint bzw. Prozess-DB, je Katalog-Komponente) · **HubSpot**.

### 2.1 Bestand / Zähler

| Feld | Empfohlene Quelle | Feldtyp | Pflicht? | Begründung / Hinweis |
|---|---|---|---|---|
| Schaltplan (Übersichtsschaltplan) | **Datei**, und zwar Export aus dem Reonic-Schaltplan (PDF) als Upload ins Bild/PDF-Item der Checkliste | image (`allowedAttachmentType` = Pdf bzw. Both) | ja | Der Reonic-Schaltplan ist per API nicht als Datei abrufbar (das `circuitPlan`-Item hat keinen Inhalt). Über das Checklisten-Item ist die Zuordnung eindeutig. Ablage im Dateiordner „Netzanmeldung“ wäre eine Alternative, ist aber fehleranfälliger (Namenskonvention). |
| Foto Hauptstromzähler | Checkliste (Monteur), alternativ Kunde per Dateianfrage | image (Bild) | ja | Zählernummer muss lesbar sein. |
| Foto Zählerschrank | Checkliste, alternativ Kunde | image (Bild, mehrere erlaubt) | ja | Gesamtansicht geöffnet und geschlossen. |
| Foto Hausanschlusskasten (HAK) | Checkliste, alternativ Kunde | image (Bild) | ja | Mit sichtbaren Sicherungseinsätzen (Prüfung NH-Sicherung, Default 63 A in HubSpot). |
| Hauptzählernummer | **Projektfeld `meterNumber`** | Text | ja | Gibt es bereits und ist per API les- und schreibbar. Nur falls reinsolar das Feld nicht nutzen will: Freitext in der Checkliste. Nicht doppelt pflegen. Das Netze-Duisburg-Portal sucht das Anschlussobjekt über die Zählernummer. |
| Kaskadenschaltung | Checkliste | radio Ja/Nein | ja | Kein Reonic-Standardfeld. Radio statt Checkbox, damit „nicht beantwortet“ von „Nein“ unterscheidbar bleibt. |
| Abzumeldende Zähler | **abgeleitet** aus Zähler 1–4 (Typ = „Ausbau/Abmeldung“) | – | – | Kein eigenes Feld, um Widersprüche zu vermeiden. |
| Zähler 1–4: Typ Zählerwechsel | Checkliste, Segment „Zähler n“ | radio | ja, wenn Zähler n relevant | Auswahlwerte siehe 3.3. |
| Zähler 1–4: Zählernummer | Checkliste | freetext (einzeilig) | ja, wenn relevant | Kein Zahlentyp; Validierung im Code (Format, Leerzeichen entfernen). |
| Zähler 1–4: Lage der Verbrauchsstelle | Checkliste | radio + freetext „Bezeichnung“ | ja, wenn relevant | z. B. Wohnung, Allgemeinstrom, Wärmepumpe. Freitext für „Wohnung EG links“. |
| Zähler 1–4: Einbauort Zähler | Checkliste | radio | ja, wenn relevant | Keller, HAR, Flur … |
| Zähler 1–4: 3-Punkt / eHZ | Checkliste | radio | ja, wenn relevant | Befestigungsart des Zählerplatzes. |
| Foto je Zähler (Vorschlag) | Checkliste | image | empfohlen | Belegt die Zählernummer und dient der Prüfung per KI/OCR. |
| Zählerplatz TAB-konform / Umbau erforderlich (Vorschlag) | Checkliste | radio | empfohlen | Für den Zählerwechselantrag und die Frage „Netzanschlussänderung ist eigener Antrag“ relevant. |
| Netzform / Hausanschluss einphasig/dreiphasig (Vorschlag) | Checkliste | radio | optional | Für die Symmetrie-Prüfung (E.8) bei einphasigen WR/Speichern. |

### 2.2 Neue Anlage

| Feld | Empfohlene Quelle | Feldtyp | Pflicht? | Begründung / Hinweis |
|---|---|---|---|---|
| Datenblätter | **Katalog** `datasheetUrl` je Komponente der Variante; Fallback SharePoint | PDF (URL 24 h gültig) | ja | Liegt nur vor, wenn reinsolar Datenblätter im Katalog hinterlegt [nicht verifiziert]. **Einheitenzertifikat und NA-Schutz-Zertifikat** haben in Reonic kein Feld → SharePoint/Produktstamm, wie in `04-…` vorgesehen. |
| Neuerrichtung / Erweiterung | **HubSpot** („Bestandsanlage vorhanden“), plausibilisiert mit Reonic `existingSystems.solar` | abgeleitet | ja | Gibt es in HubSpot bereits → nicht zusätzlich in die Checkliste. |
| Daten Bestandsanlage bei Erweiterung (Vorschlag) | Checkliste, Segment „Bestandsanlage“ (nur bei Erweiterung) | freetext + image | ja bei Erweiterung | Leistung kWp, WR, Inbetriebnahmejahr, MaStR-Nummer (SEE…), Foto Typenschild. In HubSpot steht nur Freitext. |
| Module: Hersteller, Modell, Wp/Modul, Anzahl | **Variante** (`componentType=module`: `brand`, `name`, `quantity`) + **Katalog** (`attributes.wattPeak`) | strukturiert | ja | Gehört nicht in die Checkliste. Gesamtleistung kWp = Σ(`wattPeak` × `quantity`) / 1000. Pflege von `wattPeak` im Katalog ist Voraussetzung. |
| Wechselrichter: Name, Hersteller, Modell, W-Leistung, Anzahl | **Variante** (`inverter`, `microinverter`, `allInOneEnergyStorageSystem`) + **Katalog** (`nominalOutputPower` in W, `numberOfPhases`) | strukturiert, Liste | ja | Gehört nicht in die Checkliste. Liste entsteht automatisch (eine Position je WR-Typ, Anzahl = `quantity`). Optimierer separat (`optimizer`), für E.2 nicht als Erzeugungseinheit zählen. |
| WR-Scheinleistung S<sub>max</sub> in kVA (Vorschlag) | **Produktstamm** (je Katalog-Komponente) | Zahl | ja | Fehlt im Reonic-Katalog (dort nur Wirkleistung `nominalOutputPower`). Für E.2/E.8 nötig. Je Produkt einmal pflegen, nicht je Projekt. |
| WR-Bemessungsstrom, NA-Schutz integriert, Einheitenzertifikat-Nr. (Vorschlag) | **Produktstamm** | Zahl/Text/Datei | ja | E.3/E.4/E.6. Produkteigenschaft, gehört also nicht in die Checkliste. |
| Phasigkeit WR/Speicher (Vorschlag) | **Katalog** `numberOfPhases` | Zahl | ja | Gibt es. Bei `null` greift der Produktstamm. |
| Batteriespeicher vorhanden | **abgeleitet** aus der Variante (`batteryStorage` oder `allInOneEnergyStorageSystem` vorhanden) | – | – | Kein Checklistenfeld. |
| Speicher: Hersteller, Modell | **Variante** (`brand`, `name`) | strukturiert | ja, wenn Speicher | – |
| Speicherkapazität (z. B. 8,7 kWh) | **Katalog** `capacity` (Wh) → kWh | Zahl | ja, wenn Speicher | Laut Changelog 3.21 ist `capacity` die **Nenn**kapazität, nicht die nutzbare. Das MaStR fragt die **nutzbare** Kapazität ab → nutzbare kWh im **Produktstamm** ergänzen. Bei modularen Speichern zählt `quantity` mit. |
| Speicher: Kopplung AC/DC (Vorschlag) | **Produktstamm** | Auswahl | ja, wenn Speicher | Gibt es im Katalog nicht (nur grafisch im Schaltplan). Bei All-in-One-Geräten in der Regel DC. Steuert E.3 (AC → L1/L2/L3, DC → Drehstrom). |
| Speicher: Bemessungsleistung kW (Vorschlag) | **Katalog** `maxDischargingPower` (W) | Zahl | ja, wenn Speicher | Gibt es (optional befüllt). |
| Ersatzstrom/Notstrom, inselnetzbildend (Vorschlag) | **Checkliste** (projektabhängig: Funktion installiert ja/nein) + Produktstamm (fähig ja/nein) | radio | ja, wenn Speicher | E.3 fragt „Inselnetz bildendes System“ und „allpolige Trennung bei Netzersatzbetrieb“. Ob die Funktion verbaut wird, entscheidet sich je Projekt. |
| Wallbox vorhanden | **abgeleitet** aus der Variante (`evCharger`); Bestand aus `existingSystems.evCharger` | – | – | Kein Checklistenfeld. |
| Wallbox: Leistung, Modell, Hersteller | **Variante** + **Katalog** `maxChargingPower` (W) | strukturiert | ja, wenn Wallbox | > 4,2 kW löst den Mantelbogen aus (`04-…`). |
| Messkonzept | **Checkliste** | radio | ja | Steht nirgends in Reonic. Neutrale Auswahlwerte; Abbildung auf die netzbetreiberspezifischen MK-Nummern in der **Konfiguration je Netzbetreiber** (siehe 3.3). |
| Einspeiseart Voll/Teil | **HubSpot** (bereits vorhanden, Default Teileinspeisung) | – | – | Nicht doppeln. Muss zum Messkonzept passen → Plausibilitätsprüfung im Code. |
| Abweichung von der Auftragsbestätigung (Vorschlag) | **Checkliste, Phase `installation`** | radio + freetext | ja | Wird beim Bau ein anderer WR/Speicher verbaut, stimmen Variante und Realität nicht mehr überein (wichtig für E.8 und MaStR). |
| Inbetriebnahmedatum (Vorschlag) | **Checkliste, Phase `installation`** | freetext (TT.MM.JJJJ) | ja | Für E.8 und MaStR. Alternativ aus dem HubSpot-Termin; das tatsächliche Datum kennt aber der Monteur. |

### 2.3 Wichtigster Punkt: Was **nicht** in die Checkliste gehört

- **Alle Komponentendaten**: Module, Wechselrichter, Speicher, Wallbox mit Hersteller, Modell, Leistung, Kapazität und Anzahl. Sie kommen aus der unterschriebenen Variante plus Katalog. Dazu gehören auch die abgeleiteten Booleans „Speicher vorhanden“ und „Wallbox vorhanden“ sowie die Gesamtleistung in kWp.
- **Hauptzählernummer**: Projektfeld `meterNumber`, sofern reinsolar es nutzt.
- **Datenblätter**: Katalog-Datenblätter bzw. SharePoint.
- **Neuerrichtung/Erweiterung und Einspeiseart**: kommen bereits aus HubSpot.
- **Abzumeldende Zähler**: werden aus Zähler 1–4 abgeleitet.
- **Produkteigenschaften** (Kopplung, S<sub>max</sub> in kVA, nutzbare Kapazität, NA-Schutz, Zertifikate): Sie sind je Produkt gleich und gehören in einen Produktstamm, nicht in jedes Projekt.

Damit schrumpft die Checkliste auf das, was nur vor Ort feststellbar ist: Zählerschrank, Zähler, Messkonzept, Schaltplan, Ersatzstrom und Abweichungen.

---

## 3. Checklisten-Entwurf für Michael

### 3.1 Grundentscheidungen

- **Phase:** Die Netzanmeldungsdaten werden beim **Vor-Ort-Termin** erhoben, also in der Checkliste der Phase **„Beratung“ (`offer`)**. Begründung: Der Trigger („Termine an Kunde gesendet & Vollmacht zurück“) liegt vor der Montage. Die Baustellendokumentation (`installation`) wäre zu spät. Die E.8-relevanten Punkte kommen als kleiner zweiter Block in die **Baustellendokumentation**. → *Offene Frage: Füllt reinsolar die Beratungs-Checkliste heute beim Vor-Ort-Termin aus?*
- **Ein eigener Block „Netzanmeldung“**, damit der Code ihn eindeutig findet. Darin Segmente je Thema, und jedes Segment wird einzeln abgeschlossen.
- **Ja/Nein immer als Auswahl (radio)** mit den Werten `Ja` und `Nein`, nie als einzelne Checkbox.
- **Zahlen als Freitext, einzeilig**, mit Einheit im Titel, z. B. „Leistung Bestandsanlage (kWp)“.
- **Bedingungen:** Wir verlassen uns nicht auf Reonic-Logik. Die Regel lautet: *„Segment bzw. Feld trifft nicht zu → über das Drei-Punkte-Menü als irrelevant markieren.“* Der Code ignoriert abhängige Felder ohnehin, wenn das steuernde Feld „Nein“ ist. Falls Reonic eine Sichtbarkeitslogik anbietet (im Portal prüfen), kann Michael sie zusätzlich nutzen. Das Mapping funktioniert in beiden Fällen.
- **Zähler 1–4: vier feste Segmente** („NA – Zähler 1“ … „NA – Zähler 4“). Wiederholbare Gruppen gibt es nicht. Ein Steuerfeld „Anzahl betroffener Zähler“ legt fest, welche Segmente ausgewertet werden. Nicht benötigte Segmente als irrelevant markieren. Weil Pflichtfelder bei „irrelevant“ eine Begründung brauchen, sollte die Pflicht in den Zähler-Segmenten **nicht** in Reonic gesetzt werden. Die Prüfung macht der Code.

### 3.2 Schlüssel-Konvention (für stabiles API-Mapping)

- Jedes Item mit Wert bekommt in der **Beschreibung** als letzte Zeile einen Schlüssel: `[key: na.zaehler1.typ]`. Die Beschreibung kommt per API mit (`description`) und ist für Monteure nur ein kleiner Hilfetext.
- Bei Bild-Items sitzen Titel und Beschreibung am Unter-Item (`image.items[].title/description`), der Schlüssel gehört also an jede einzelne Foto-Anforderung.
- Auswahlwerte werden **wortgleich** übernommen (der Code mappt über den normalisierten Text). Werden Optionen umbenannt, muss Michael Martin Bescheid geben.
- Fallback-Mapping im Code: Blockname → Segmentname → normalisierter Item-Titel. Das Mapping (Schlüssel → Feld der NAB-Datenkarte) liegt in der **Instanz-Konfiguration**, nicht im Kerncode.
- Konvention: `na.` = Netzanmeldung (Phase offer), `ibn.` = Inbetriebnahme (Phase installation); klein geschrieben, ASCII, Punkt als Trenner.

### 3.3 Block „Netzanmeldung“ (Checkliste Phase „Beratung“)

**Segment „NA – Hausanschluss & Zählerschrank“**

| # | Titel (so anlegen) | Typ | Auswahlwerte / Einstellungen | Pflicht | Schlüssel |
|---|---|---|---|---|---|
| 1 | Hauptzählernummer | Freitext, einzeilig | *nur anlegen, falls das Projektfeld „Zählernummer“ nicht genutzt wird* | ja | `na.hauptzaehler.nummer` |
| 2 | Fotos Zähleranlage | Bilder (3 Unter-Items) | a) „Foto Hauptstromzähler (Nummer lesbar)“, Bild, max. 2 · b) „Foto Zählerschrank (geöffnet, gesamt)“, Bild, mehrere · c) „Foto Hausanschlusskasten (HAK) mit Sicherungen“, Bild, max. 2 | ja (a–c) | `na.foto.hauptzaehler`, `na.foto.zaehlerschrank`, `na.foto.hak` |
| 3 | Kaskadenschaltung | Auswahl | Ja / Nein | ja | `na.kaskade` |
| 4 | Anzahl betroffener Zähler (Wechsel, Ausbau, Neusetzung) | Auswahl | 0 / 1 / 2 / 3 / 4 | ja | `na.zaehler.anzahl` |
| 5 | Zählerplatz TAB-konform? (Vorschlag) | Auswahl | Ja / Nein – Umbau Zählerschrank nötig / Unklar | ja | `na.zaehlerplatz.tabkonform` |
| 6 | Hausanschluss (Vorschlag) | Auswahl | Dreiphasig / Einphasig / Unbekannt | nein | `na.hausanschluss.phasen` |

**Segment „NA – Messkonzept“**

| # | Titel | Typ | Auswahlwerte | Pflicht | Schlüssel |
|---|---|---|---|---|---|
| 1 | Messkonzept | Auswahl | „Überschusseinspeisung – Zweirichtungszähler“ / „Volleinspeisung – Erzeugungszähler“ / „Kaskade – Zweirichtungszähler + Erzeugungszähler“ / „Überschuss + separater Zähler steuerbare Verbrauchseinrichtung (Wallbox/WP, § 14a)“ / „Sonstiges (siehe Bemerkung)“ | ja | `na.messkonzept` |
| 2 | Bemerkung Messkonzept | Freitext, mehrzeilig | – | nein | `na.messkonzept.bemerkung` |

*Hinweis:* Die Werte sind bewusst netzbetreiberneutral. Die Zuordnung zur Nummer im Messkonzept-Auswahlblatt von Netze Duisburg (und später RheinNetz/Westnetz) steht in der Netzbetreiber-Konfiguration. **Die Liste muss Michael gegen das Duisburg-Auswahlblatt prüfen und ergänzen** (offene Frage).

**Segmente „NA – Zähler 1“ bis „NA – Zähler 4“** (identisch, `n` = 1…4)

| # | Titel | Typ | Auswahlwerte | Pflicht (im Code) | Schlüssel |
|---|---|---|---|---|---|
| 1 | Zähler n: Art des Vorgangs | Auswahl | „Tausch gegen Zweirichtungszähler“ / „Ausbau / Abmeldung (Zusammenlegung)“ / „Neusetzung zusätzlicher Zähler“ / „Umbau auf Kaskade (Erzeugungszähler)“ / „Kein Wechsel (nur Angabe Bestand)“ | ja | `na.zaehler{n}.typ` |
| 2 | Zähler n: Zählernummer | Freitext, einzeilig | bei Neusetzung leer lassen | ja außer bei Neusetzung | `na.zaehler{n}.nummer` |
| 3 | Zähler n: Verbrauchsstelle | Auswahl | Wohnung / Allgemeinstrom (Haus) / Wärmepumpe / Wallbox / Gewerbe / Sonstiges | ja | `na.zaehler{n}.verbrauchsstelle` |
| 4 | Zähler n: Bezeichnung Verbrauchsstelle | Freitext, einzeilig | z. B. „Wohnung EG links“ | nein | `na.zaehler{n}.verbrauchsstelle.text` |
| 5 | Zähler n: Einbauort | Auswahl | Keller / Hausanschlussraum / Flur / Wohnung / Garage / Außen (Zählersäule) / Sonstiges | ja | `na.zaehler{n}.einbauort` |
| 6 | Zähler n: Befestigung | Auswahl | 3-Punkt-Befestigung / eHZ (Stecktechnik/BKE) / Unbekannt | ja | `na.zaehler{n}.befestigung` |
| 7 | Zähler n: Foto (Nummer lesbar) (Vorschlag) | Bild | max. 2 | empfohlen | `na.zaehler{n}.foto` |

*Ist der Hauptzähler gleichzeitig Zähler 1?* Vorschlag: **ja**, Zähler 1 ist immer der Hauptzähler, sobald an ihm etwas passiert (in der Regel Tausch gegen Zweirichtungszähler). Für Michael zu bestätigen.

**Segment „NA – Speicher & Anlage“**

| # | Titel | Typ | Auswahlwerte | Pflicht | Schlüssel |
|---|---|---|---|---|---|
| 1 | Ersatzstrom-/Notstromfunktion wird installiert (Vorschlag) | Auswahl | Nein / Notstrom (Steckdose am WR/Speicher) / Ersatzstrom ganzes Haus (inselnetzbildend, allpolige Trennung) / Kein Speicher | ja | `na.speicher.ersatzstrom` |
| 2 | Wirkleistungsbegrenzung (Vorschlag) | Auswahl | Keine / 60 % / 50 % / Nach Vorgabe Netzbetreiber | nein (Default aus Konfiguration) | `na.wirkleistungsbegrenzung` |
| 3 | Hinweise für die Netzanmeldung | Freitext, mehrzeilig | – | nein | `na.hinweise` |

*Speicher, Wallbox und Komponenten werden **nicht** abgefragt.* Sie kommen aus der Variante. Optional kann Michael als Kontrollanzeige für den Berater das Element **„Komponentenliste“** einfügen.

**Segment „NA – Bestandsanlage“** (nur bei Erweiterung, sonst als irrelevant markieren; Vorschlag)

| # | Titel | Typ | Pflicht | Schlüssel |
|---|---|---|---|---|
| 1 | Leistung Bestandsanlage (kWp) | Freitext | ja bei Erweiterung | `na.bestand.kwp` |
| 2 | Wechselrichter Bestandsanlage (Hersteller/Modell) | Freitext | ja bei Erweiterung | `na.bestand.wr` |
| 3 | Inbetriebnahmejahr Bestandsanlage | Freitext | ja bei Erweiterung | `na.bestand.ibn_jahr` |
| 4 | MaStR-Nummer Bestandsanlage (SEE…), falls bekannt | Freitext | nein | `na.bestand.mastr_nr` |
| 5 | Foto Typenschild WR Bestandsanlage | Bild | empfohlen | `na.bestand.foto_typenschild` |

**Segment „NA – Unterlagen“**

| # | Titel | Typ | Einstellungen | Pflicht | Schlüssel |
|---|---|---|---|---|---|
| 1 | Übersichtsschaltplan (PDF aus Reonic-Schaltplan) | Bild | erlaubte Typen: PDF (oder „Both“), max. 1 | ja | `na.schaltplan` |
| 2 | Schaltplan (Anzeige) | Schaltplan-Element (`circuitPlan`) | nur Referenz | – | – |

*Ablauf Schaltplan:* Schaltplan im Angebot unter Planung > Schaltplan prüfen bzw. ergänzen (Zähler, Speicherkopplung), als PDF exportieren und in Item 1 hochladen. → Offene Frage, ob der Reonic-Schaltplan für Netze Duisburg als „Übersichtsschaltplan“ ausreicht.

### 3.4 Block „Inbetriebnahme“ (Baustellendokumentation, Phase `installation`; Vorschlag)

| # | Titel | Typ | Auswahlwerte | Pflicht | Schlüssel |
|---|---|---|---|---|---|
| 1 | Datum Inbetriebnahme (TT.MM.JJJJ) | Freitext | – | ja | `ibn.datum` |
| 2 | Komponenten wie in der Auftragsbestätigung verbaut? | Auswahl | Ja / Nein – Abweichung siehe unten | ja | `ibn.abweichung` |
| 3 | Abweichende Komponenten (Hersteller, Modell, Anzahl) | Freitext, mehrzeilig | – | ja bei Abweichung | `ibn.abweichung.text` |
| 4 | Fotos Typenschilder | Bilder | a) Wechselrichter · b) Speicher · c) Wallbox | empfohlen | `ibn.foto.typenschild_wr` / `_speicher` / `_wallbox` |
| 5 | Phasenaufteilung bei einphasigen Geräten (für E.8 Symmetrie) | Freitext | z. B. „WR L1, Speicher L2“ | ja bei einphasigen Geräten | `ibn.phasen` |

Die E.8-Abschnitte ab „Symmetrie“ sind noch offen (`05-status`). Diesen Block deshalb erst nach der Klärung final anlegen.

---

## 4. Technische Hinweise für die Umsetzung (Reonic-Adapter)

### 4.1 Mapping

- Adapter `ReonicDataSource` liest:
  1. `GET /residentialProjects/{id}` (`meterNumber`, `existingSystems`, `integrations.hubspotDealId`, `stage`, Adresse)
  2. `GET /residentialProjects/{id}/signatureRequests` → Request mit `status = signed` und dem jüngsten `signedAt` → `signedVariantId` (bei `revokedByCustomer` blockieren)
  3. `GET /residentialProjects/{id}/variants/{signedVariantId}` → `lineItems` aus `solar`, `batteryStorage`, `evCharger` sowie aus `optionalBundles` mit `selected = true`
  4. je Position `GET /components/{componentId}/versions/{componentVersionId}` (angebotener Stand, nicht der aktuelle Katalogstand) → `attributes`, `datasheetUrl`
  5. `GET /checklists/{id}?stage=offer` (und für E.8 `?stage=installation`), **immer mit `Reonic-Cache-Control: no-cache`**
- Checklisten-Auswertung: Items über `[key: …]` in `description` finden, sonst über Segment + Titel. Je Item `markedIrrelevant` beachten. Segment `completedAt != null` gilt als „vom Monteur freigegeben“.
- Vollständigkeitsprüfung im Code: Fehlt ein Pflichtwert, entsteht ein **HubSpot-Task** (fachlicher Fehler, siehe Architektur), kein Retry.
- Anhänge sofort herunterladen und in der Prozess-DB bzw. im Storage ablegen. URL-Lebensdauer: für Dateien 24 h laut Doku, für Checklisten-Anhänge **nicht dokumentiert**.
- Keine Rohinhalte der Checkliste loggen (Freitexte können Namen und Adressen enthalten). Nur Schlüssel und Status loggen.

### 4.2 Trigger-Ergänzung (optional)

Webhook `projectChecklist_segmentCompletionChanged` für die Segmente „NA – …“: Er kann einen Vollständigkeits-Check anstoßen und am HubSpot-Deal anzeigen, ob die Reonic-Daten bereit sind, noch bevor der HubSpot-Trigger feuert. Der führende Trigger bleibt HubSpot.

### 4.3 Kalkulation

- kWp = Σ über Module (`wattPeak` × `quantity`) / 1000
- WR-Wirkleistung gesamt = Σ (`nominalOutputPower` × `quantity`); S<sub>max</sub> in kVA kommt aus dem Produktstamm
- Speicher kWh (nominal) = Σ (`capacity` × `quantity`) / 1000; die nutzbare Kapazität kommt aus dem Produktstamm
- Plausibilisierung: Ist `quantityUnit` ungleich `item` (z. B. `kWp`), Warnung ausgeben

---

## 5. Kundenformular: Was der Endkunde selbst liefern kann

Reonic bietet dafür **Dateianfragen im Kundenportal** (auch als wiederverwendbare Vorlagen). Der Kunde lädt per Handy Bilder oder PDFs hoch, die Uploads landen in den Projektdateien mit der Markierung „vom Kunden hochgeladen“ (API-Filter `createdByCustomer=true`). **Textfelder sind nicht möglich.** [Doku]

Vorschlag für Vorlagen (Einstellungen > Kundenkommunikation > Vorlagen für Dateianfragen):

| Vorlage | Inhalt / Beschreibung für den Kunden | Typ |
|---|---|---|
| Foto Stromzähler | „Bitte fotografieren Sie jeden Stromzähler so, dass die Zählernummer lesbar ist.“ | Bild, mehrere |
| Foto Zählerschrank | „Zählerschrank geöffnet, komplett im Bild.“ | Bild, mehrere |
| Foto Hausanschlusskasten | „Der graue Kasten, meist im Keller, an dem das Stromkabel ins Haus kommt.“ | Bild |
| Foto Bestandsanlage (nur bei Erweiterung) | „Typenschild des vorhandenen Wechselrichters.“ | Bild |
| Letzte Stromrechnung (optional) | Zählernummer und Marktlokation als Gegenprüfung | PDF/Bild |

Textangaben des Kunden (Zählernummer, Lage der Verbrauchsstelle) lassen sich in Reonic nicht abfragen. Optionen:
- (a) **Zählernummer per KI aus dem Foto auslesen** und dem Monteur zur Bestätigung vorlegen. Das passt zum vorgesehenen KI-Einsatz „Daten auslesen“, ein Mensch bestätigt.
- (b) Das bestehende **Webformular auf der reinsolar-Website** (für den MaStR-Login, geht nach HubSpot) um Zählernummer(n) und Verbrauchsstelle erweitern.

Empfehlung: Fotos per Reonic-Dateianfrage, Zahlen über (a). **Führend bleibt die Checkliste des Beraters/Monteurs.** Kundenfotos sind Vorabinfo und Fallback, wenn der Vor-Ort-Termin entfällt.

Einschränkung: Laut Schema hat eine Datei keinen Verweis auf die Dateianfrage, aus der sie stammt [Schema]. Die Zuordnung „welches Kundenfoto zeigt was“ ist per API deshalb nur über Dateiname/Ordner oder per KI möglich [nicht verifiziert].

---

## 6. Offene Fragen und Tests mit API-Key

### 6.1 Fragen an Michael / reinsolar

1. Wird die **Beratungs-Checkliste (Phase „Beratung“)** heute beim Vor-Ort-Termin ausgefüllt, und von wem (Berater oder Elektriker)? Oder soll der Block in die Qualifizierung bzw. Baustellendokumentation?
2. Pflegt reinsolar das Projektfeld **Zählernummer** (`meterNumber`) in Reonic? Wenn nein: Ist Michael einverstanden, es künftig dort statt in der Checkliste zu pflegen?
3. Sind im Reonic-**Katalog** für alle verwendeten Module, WR, Speicher und Wallboxen die technischen Werte gepflegt (Wp, Ausgangsleistung, Phasen, Kapazität, Ladeleistung)? Sind **Datenblätter** hinterlegt?
4. Wird der **Reonic-Schaltplan** (Planung > Schaltplan) genutzt, und reicht er Netze Duisburg als Übersichtsschaltplan (Zähler, Speicherkopplung)?
5. **Messkonzept-Optionen:** Welche Messkonzepte kommen bei reinsolar vor, und welche Nummern hat das Messkonzept-Auswahlblatt von Netze Duisburg?
6. **Typ Zählerwechsel:** Passen die vorgeschlagenen Werte? Fehlt etwas, etwa „Wandlermessung“?
7. Gilt „Zähler 1 = Hauptzähler“? Reichen 4 Zähler?
8. Unterstützt der Reonic-Checklisten-Editor eine **Sichtbarkeitsbedingung** (Feld nur zeigen, wenn …)? Die Anwender-Doku deutet das an, die Konfigurations-Doku nicht.
9. Werden Komponenten bei der Montage manchmal **abweichend vom Angebot** verbaut? Wird die Variante dann in Reonic angepasst, oder bleibt die AB unverändert?
10. Soll der Endkunde Fotos per **Reonic-Dateianfrage** liefern, und ist das Kundenportal aktiv?
11. Nutzt reinsolar den kostenpflichtigen **Reonic-Netzanmeldungs-Service**? Falls ja, gibt es Überschneidungen oder Konflikte.
12. Ist der vorhandene API-Key ein **v3-Key** (`rnc_v3_…`)? Welche Scopes hat er?

### 6.2 Fragen an Martin (Entscheidung im Code)

- Produktstamm (S<sub>max</sub> in kVA, Kopplung, nutzbare kWh, NA-Schutz, Zertifikate): Tabelle in der Prozess-DB, Schlüssel ist die Reonic-`componentId`, Pflege per Admin-Endpoint/CSV wie bei der PLZ-Tabelle? Oder Ableitung per KI aus dem Datenblatt mit menschlicher Bestätigung?
- Bleiben HubSpot-Line-Items als Quelle nötig, wenn die Variante direkt aus Reonic kommt? Empfehlung: **nein**, Reonic als einzige Komponentenquelle; HubSpot nur zur Anzeige.

### 6.3 Nur mit API-Key testbar

Alle Requests mit `X-Authorization: $REONIC_API_KEY` (Key aus der Umgebungsvariable, nie ins Repo). `{pid}` ist eine Projekt-ID eines **Testprojekts** (keine Kundendaten in Tickets oder Logs übernehmen).

| # | Request | Prüfziel |
|---|---|---|
| 1 | `GET /rest/v3/me` | Key-Typ, `scopes` (reichen `projects`, `checklists`, `components` und `files:read`?) |
| 2 | `GET /rest/v3/residentialProjects?stage=offer&dealState=Won&itemsPerPage=5&sort=-updatedAt` | Zugriff, Pagination, Testprojekt finden |
| 3 | `GET /rest/v3/residentialProjects/{pid}` | Ist `meterNumber` befüllt? Wie gefüllt sind `existingSystems` und `integrations.hubspotDealId`? |
| 4 | `GET /rest/v3/checklists/{pid}?stage=offer` mit Header `Reonic-Cache-Control: no-cache` | Struktur real; kommen `text`, `checked` und `attachments[].url` wie im Schema? Sind Block- und Segment-IDs über Projekte hinweg gleich (aus der Vorlage) oder je Projekt neu? |
| 5 | wie 4 mit `?stage=installation` und `?stage=request` | 404 ohne aktive Checkliste? Inhalte je Phase |
| 6 | Download einer `attachments[].url` aus 4 | Braucht die URL Auth? Wie lange ist sie gültig? Welcher Dateityp? |
| 7 | `GET /rest/v3/residentialProjects/{pid}/signatureRequests` | `signedVariantId` vorhanden, `signedPdfUrl` (AB-PDF) |
| 8 | `GET /rest/v3/residentialProjects/{pid}/variants/{signedVariantId}` | Positionen, `componentType`, `quantity`, `optionalBundles` |
| 9 | `GET /rest/v3/components/{componentId}/versions/{componentVersionId}` | `attributes` befüllt (`wattPeak`, `nominalOutputPower`, `numberOfPhases`, `capacity`)? `datasheetUrl` vorhanden? |
| 10 | `GET /rest/v3/components?componentType=inverter&componentType=batteryStorage&componentType=module` | Abdeckung der technischen Werte im ganzen Katalog (Pflegeaufwand für Michael abschätzen) |
| 11 | `GET /rest/v3/files?parentType=residentialProject&parentId={pid}` und `&createdByCustomer=true`; `GET /rest/v3/fileFolders?parentType=residentialProject&parentId={pid}` | Wo liegen Schaltplan-Export und Kunden-Uploads? Welche Dateinamen? |
| 12 | `GET /rest/v3/checklistTemplates` | Bestätigen, dass das Komponenten-Pakete sind und keine Checklisten-Vorlagen |
| 13 | Antwort-Header `X-RateLimit-*` aus beliebigen Requests | Budget des Workspaces, Verhalten bei `no-cache` |
| 14 | Nach Anlage der neuen Vorlage: Testprojekt mit vollständig ausgefüllter Checkliste → Request 4 | Kommen die `[key: …]`-Schlüssel in `description` an? Kommt `markedIrrelevant` mit? Kommt `completedAt` je Segment? |
| 15 | (braucht `checklists:write`, mit Read-only-Key **nicht** möglich) `POST /rest/v3/projects/{pid}/checklist/export` mit `{ "stage": "offer" }` | PDF-Fallback; Ergebnis über Webhook `projectChecklist_pdfGenerated` bzw. `/files` |

Beispiel (PowerShell, Key aus der Umgebungsvariable):

```powershell
$h = @{ 'X-Authorization' = $env:REONIC_API_KEY; 'Reonic-Cache-Control' = 'no-cache' }
$projectId = '<Testprojekt-UUID>'
Invoke-RestMethod -Headers $h -Uri "https://api.reonic.com/rest/v3/checklists/$($projectId)?stage=offer" | ConvertTo-Json -Depth 20
```

---

## 7. Quellen

- Reonic API v3, Referenz: https://api.reonic.com/rest/v3/docs
- OpenAPI-Spezifikation (maschinenlesbar, inkl. Changelog und Webhooks): https://api.reonic.com/rest/v3/openapi
- LLM-Index der Endpunkte: https://api.reonic.com/rest/v3/llms.txt
- Checklisten-Übersicht: https://docs.reonic.com/docs/en/settings-checklists-checklists-overview
- Beratungs-Checkliste konfigurieren (Bausteine, Pflicht, KI-Vorlagen): https://docs.reonic.com/docs/en/settings-checklists-offer-consultation
- Baustellendokumentation konfigurieren (Vorlagen-Update): https://docs.reonic.com/docs/en/settings-checklists-site-documentation-installation
- Baustellendokumentation ausfüllen (Item-Arten, irrelevant markieren, Segmentabschluss): https://docs.reonic.com/docs/en/installation-checklist-baustellendokumentation
- Schaltplan (Inhalt, Export PDF/JPEG): https://docs.reonic.com/docs/en/offers-plan-circuit-plan-base
- Dateianfragen-Vorlagen: https://docs.reonic.com/docs/en/settings-customer-communication-templates-for-file-requests
- Kundenportal: https://docs.reonic.com/docs/en/settings-customer-communication-customer-portal
- Speicher im Angebot: https://docs.reonic.com/docs/en/offers-plan-battery-add-components
- Reonic-Services (Netzanmeldung als Service je Fall): https://reonic.com/en-gb/services/
- Netzanmeldungs-Service-Doku (inzwischen 404, nur im Suchindex): https://docs.reonic.com/docs/en/services-grid-registration
