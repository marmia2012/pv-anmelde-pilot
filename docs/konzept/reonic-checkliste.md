# Reonic-Checkliste „Netzanmeldung“

Stand: 03.11.2026 · Von: Martin · Für: Michael (reinsolar)
Status: **Entwurf, wir stimmen ihn am 09.11. ab.**

Hallo Michael,

der PV-Autopilot holt alle Daten für die Netzanmeldung aus Reonic, auch die Komponenten. Hier steht, wie wir das in Reonic abbilden wollen und was du dafür vorbereiten müsstest. Die offenen Fragen an dich stehen in Abschnitt 5.

---

## 0. Kurzfassung

### Was wir vorschlagen

1. **Komponenten aus deiner Reonic-Planung (Vorschlag 4.1).** Module, Wechselrichter, Speicher und Wallbox liest der Autopilot aus der Planungsvariante, die du als **Hauptvariante** markierst. Technische Daten und Datenblätter kommen aus dem **Reonic-Katalog**. Du tippst nichts doppelt ab.
2. **Hauptzählernummer im Reonic-Projektfeld „Zählernummer“ (Vorschlag 4.3)** statt in der Checkliste.
3. **Zusätzliche Formularwerte aus den Datenblättern (Vorschlag 4.7)**, z. B. Scheinleistung, Kopplung AC/DC und nutzbare Kapazität. Der Autopilot liest sie einmal je Produkt aus, ein Mensch bestätigt.
4. **In die Checkliste kommt nur, was vor Ort festgestellt wird:** Fotos, Zähler, Kaskade, Messkonzept, Neuerrichtung/Erweiterung und der Schaltplan. Das sind **33 statt 71 Felder**, im Normalfall bleiben davon viele leer (Zähler 2–4).

Falls deine Planung in Reonic die Komponenten nicht vollständig enthält, gibt es eine **Alternative (Vorschlag 4.2):** Hersteller und Modell per Auswahlliste in der Checkliste.

### Was du in Reonic tun musst

1. Den Block **„Netzanmeldung“** in der Checkliste der Phase „Beratung“ anlegen, mit den Feldern, die in Abschnitt 2 in der Spalte „Bei Empfehlung“ mit **bleibt** markiert sind.
2. Je Projekt die gültige Planungsvariante als **Hauptvariante** markieren.
3. Im **Katalog** für alle verwendeten Produkte die technischen Werte und das Datenblatt pflegen: Wp, AC-Leistung, Phasen, Kapazität, Ladeleistung.
4. Am Projekt das Feld **„Zählernummer“** mit der Hauptzählernummer füllen.

---

## 1. Regeln für den Aufbau

Reonic-Checklisten kennen nur Auswahl, Freitext, Checkbox, Bild/PDF und Unterschrift. Zahlen-, Datums- und Ja/Nein-Felder fehlen, wiederholbare Gruppen ebenfalls. Daraus folgen diese Regeln:

| Regel | Umsetzung |
|---|---|
| Ja/Nein | Typ **Auswahl** mit genau den Werten `Ja` / `Nein` |
| Zahlen | Typ **Freitext, einzeilig**, mit der Einheit im Titel, z. B. „Leistung pro Modul (Wp)“. Der Autopilot versteht „450“, „450 Wp“ und „8,7“. |
| Listen (mehrere Zähler, WR) | **Feste Positionen** (Zähler 1–4, WR 1–3) plus ein **Steuerfeld „Anzahl …“**. Der Autopilot liest nur so viele Positionen, wie das Steuerfeld angibt. Leere Positionen stören nicht. |
| Bedingungen („nur wenn Speicher = Ja“) | Prüft der Autopilot, nicht Reonic. Nicht zutreffende Felder bleiben leer oder werden als „irrelevant“ markiert. |
| Pflichtfelder | In Reonic **nur** die Felder als Pflicht setzen, die in der Tabelle mit **Pflicht** markiert sind. Sie gelten in jedem Projekt. Bedingte Felder („Pflicht, wenn …“) **nicht** als Pflicht setzen. Sonst verlangt Reonic beim Markieren als irrelevant jedes Mal eine Begründung. Diese Felder prüft der Autopilot; fehlt etwas, bekommt ihr einen HubSpot-Task. |
| Technischer Schlüssel | Jedes Feld bekommt in der **Beschreibung** als letzte Zeile `[key: …]`, z. B. `[key: na.kaskade]`. Darüber findet der Autopilot das Feld, auch wenn du den Titel später änderst. Bei Bild-Feldern gehört der Schlüssel an jede einzelne Foto-Anforderung. |
| Auswahlwerte | Bitte wortgleich anlegen. Wenn du Auswahlwerte ändern willst, sag mir vorher Bescheid. Der Autopilot erkennt die Auswahl am Text. |

**Ort in Reonic:** eigener Block **„Netzanmeldung“** in der Checkliste der Phase **„Beratung“**, mit den Segmenten aus Abschnitt 2. Ob diese Phase bei euch genutzt wird, ist Frage 1 in Abschnitt 5.

---

## 2. Die Checkliste (vollständiger Aufbau)

Die Tabelle zeigt **alle 71 Felder**, damit du siehst, wie alles ohne die Empfehlung aussähe. Die Spalte **„Bei Empfehlung“** zeigt, welche Felder du mit der Empfehlung aus Abschnitt 0 wirklich anlegen musst (**bleibt**) und welche wegfallen (**entfällt**).

**Feldtypen** (Bezeichnung im Reonic-Editor in Klammern):
- **Auswahl** (Selection): Einfachauswahl. Bei mehr als drei Werten als Dropdown anzeigen.
- **Freitext** (Free text): einzeilig, sofern nicht anders angegeben. „Freitext (Zahl)“ heißt: nur eine Zahl eintragen, Komma erlaubt.
- **Bild** (Images, erlaubter Typ „Bild“): eine Foto-Anforderung. Mehrere Anforderungen können in einem Bild-Feld zusammengefasst werden (siehe Anmerkungen).
- **PDF** (Images, erlaubter Typ „PDF“): wie Bild, aber für PDF-Dateien.

| # | Bei Empfehlung | Segment | Feldname (Titel in Reonic) | Key | Feldtyp | Mögliche Werte | Anmerkungen |
|---|---|---|---|---|---|---|---|
| 1 | entfällt | NA – Zähleranlage | Hauptzählernummer | `na.hauptzaehler.nummer` | Freitext | – | **Pflicht.** Nummer wie auf dem Zähler, ohne Leerzeichen. Bei Empfehlung steht die Nummer im Reonic-Projektfeld „Zählernummer“ (4.3). |
| 2 | bleibt | NA – Zähleranlage | Foto Hauptstromzähler (Nummer lesbar) | `na.foto.hauptzaehler` | Bild | max. 2 Bilder | **Pflicht.** Anforderung a im Bild-Feld „Fotos Zähleranlage“. |
| 3 | bleibt | NA – Zähleranlage | Foto Zählerschrank (geöffnet, komplett) | `na.foto.zaehlerschrank` | Bild | mehrere Bilder | **Pflicht.** Anforderung b im Bild-Feld „Fotos Zähleranlage“. |
| 4 | bleibt | NA – Zähleranlage | Foto Hausanschlusskasten (HAK) mit Sicherungen | `na.foto.hak` | Bild | max. 2 Bilder | **Pflicht.** Anforderung c im Bild-Feld „Fotos Zähleranlage“. Sicherungseinsätze lesbar (Prüfung NH-Sicherung, Standard 63 A). |
| 5 | bleibt | NA – Zähleranlage | Kaskadenschaltung | `na.kaskade` | Auswahl | Ja / Nein | **Pflicht.** |
| 6 | bleibt | NA – Zähleranlage | Anzahl Zähler mit Wechsel, Abmeldung oder Neusetzung | `na.zaehler.anzahl` | Auswahl | 0 / 1 / 2 / 3 / 4 | **Pflicht.** Steuerfeld: Der Autopilot liest nur Zähler 1 bis zu dieser Anzahl. |
| 7 | bleibt | NA – Zähler 1 | Zähler 1: Typ Zählerwechsel | `na.zaehler1.typ` | Auswahl | Tausch gegen Zweirichtungszähler / Ausbau – Zähler abmelden / Neusetzung zusätzlicher Zähler / Umbau auf Kaskade (Erzeugungszähler) / Kein Wechsel (nur Bestand) | Pflicht, wenn Anzahl ≥ 1. Zähler 1 ist der Hauptzähler. Aus „Ausbau – Zähler abmelden“ ergibt sich die Liste der abzumeldenden Zähler. Werte gegen den Duisburger Zählerantrag prüfen (Frage 5). |
| 8 | bleibt | NA – Zähler 1 | Zähler 1: Zählernummer | `na.zaehler1.nummer` | Freitext | – | Pflicht, wenn Anzahl ≥ 1, außer bei „Neusetzung“ (dann leer lassen). |
| 9 | bleibt | NA – Zähler 1 | Zähler 1: Lage der Verbrauchsstelle | `na.zaehler1.verbrauchsstelle` | Auswahl | Wohnung / Allgemeinstrom (Haus) / Wärmepumpe / Wallbox / Gewerbe / Sonstiges | Pflicht, wenn Anzahl ≥ 1. |
| 10 | bleibt | NA – Zähler 1 | Zähler 1: Bezeichnung Verbrauchsstelle | `na.zaehler1.verbrauchsstelle.text` | Freitext | – | Optional, z. B. „Wohnung EG links“. Im Mehrfamilienhaus nötig, um die Zähler zuzuordnen. |
| 11 | bleibt | NA – Zähler 1 | Zähler 1: Einbauort | `na.zaehler1.einbauort` | Auswahl | Keller / Hausanschlussraum / Flur / Wohnung / Garage / Außen (Zählersäule) / Sonstiges | Pflicht, wenn Anzahl ≥ 1. |
| 12 | bleibt | NA – Zähler 1 | Zähler 1: Befestigung | `na.zaehler1.befestigung` | Auswahl | 3-Punkt / eHZ / Unbekannt | Pflicht, wenn Anzahl ≥ 1. |
| 13 | bleibt | NA – Zähler 2 | Zähler 2: Typ Zählerwechsel | `na.zaehler2.typ` | Auswahl | wie #7 | Pflicht, wenn Anzahl ≥ 2. |
| 14 | bleibt | NA – Zähler 2 | Zähler 2: Zählernummer | `na.zaehler2.nummer` | Freitext | – | Pflicht, wenn Anzahl ≥ 2, außer bei „Neusetzung“. |
| 15 | bleibt | NA – Zähler 2 | Zähler 2: Lage der Verbrauchsstelle | `na.zaehler2.verbrauchsstelle` | Auswahl | wie #9 | Pflicht, wenn Anzahl ≥ 2. |
| 16 | bleibt | NA – Zähler 2 | Zähler 2: Bezeichnung Verbrauchsstelle | `na.zaehler2.verbrauchsstelle.text` | Freitext | – | Optional. |
| 17 | bleibt | NA – Zähler 2 | Zähler 2: Einbauort | `na.zaehler2.einbauort` | Auswahl | wie #11 | Pflicht, wenn Anzahl ≥ 2. |
| 18 | bleibt | NA – Zähler 2 | Zähler 2: Befestigung | `na.zaehler2.befestigung` | Auswahl | 3-Punkt / eHZ / Unbekannt | Pflicht, wenn Anzahl ≥ 2. |
| 19 | bleibt | NA – Zähler 3 | Zähler 3: Typ Zählerwechsel | `na.zaehler3.typ` | Auswahl | wie #7 | Pflicht, wenn Anzahl ≥ 3. |
| 20 | bleibt | NA – Zähler 3 | Zähler 3: Zählernummer | `na.zaehler3.nummer` | Freitext | – | Pflicht, wenn Anzahl ≥ 3, außer bei „Neusetzung“. |
| 21 | bleibt | NA – Zähler 3 | Zähler 3: Lage der Verbrauchsstelle | `na.zaehler3.verbrauchsstelle` | Auswahl | wie #9 | Pflicht, wenn Anzahl ≥ 3. |
| 22 | bleibt | NA – Zähler 3 | Zähler 3: Bezeichnung Verbrauchsstelle | `na.zaehler3.verbrauchsstelle.text` | Freitext | – | Optional. |
| 23 | bleibt | NA – Zähler 3 | Zähler 3: Einbauort | `na.zaehler3.einbauort` | Auswahl | wie #11 | Pflicht, wenn Anzahl ≥ 3. |
| 24 | bleibt | NA – Zähler 3 | Zähler 3: Befestigung | `na.zaehler3.befestigung` | Auswahl | 3-Punkt / eHZ / Unbekannt | Pflicht, wenn Anzahl ≥ 3. |
| 25 | bleibt | NA – Zähler 4 | Zähler 4: Typ Zählerwechsel | `na.zaehler4.typ` | Auswahl | wie #7 | Pflicht, wenn Anzahl = 4. |
| 26 | bleibt | NA – Zähler 4 | Zähler 4: Zählernummer | `na.zaehler4.nummer` | Freitext | – | Pflicht, wenn Anzahl = 4, außer bei „Neusetzung“. |
| 27 | bleibt | NA – Zähler 4 | Zähler 4: Lage der Verbrauchsstelle | `na.zaehler4.verbrauchsstelle` | Auswahl | wie #9 | Pflicht, wenn Anzahl = 4. |
| 28 | bleibt | NA – Zähler 4 | Zähler 4: Bezeichnung Verbrauchsstelle | `na.zaehler4.verbrauchsstelle.text` | Freitext | – | Optional. |
| 29 | bleibt | NA – Zähler 4 | Zähler 4: Einbauort | `na.zaehler4.einbauort` | Auswahl | wie #11 | Pflicht, wenn Anzahl = 4. |
| 30 | bleibt | NA – Zähler 4 | Zähler 4: Befestigung | `na.zaehler4.befestigung` | Auswahl | 3-Punkt / eHZ / Unbekannt | Pflicht, wenn Anzahl = 4. |
| 31 | bleibt | NA – Anlage | Neuerrichtung oder Erweiterung | `na.anlage.art` | Auswahl | Neuerrichtung / Erweiterung einer bestehenden Anlage | **Pflicht.** Diese Angabe ist führend (4.8). |
| 32 | bleibt | NA – Anlage | Messkonzept | `na.messkonzept` | Auswahl | Überschusseinspeisung – Zweirichtungszähler / Volleinspeisung – Erzeugungszähler / Kaskade – Zweirichtungs- + Erzeugungszähler / Überschuss + separater Zähler steuerbare Verbrauchseinrichtung (§ 14a) / Sonstiges (siehe Bemerkung) | **Pflicht.** Die Werte gelten für alle Netzbetreiber. Die Zuordnung zur Nummer im Duisburger Auswahlblatt hinterlegt der Autopilot. Bitte die Liste prüfen (Frage 6). Der Autopilot prüft, ob das Messkonzept zur Einspeiseart passt. |
| 33 | bleibt | NA – Anlage | Bemerkung Messkonzept | `na.messkonzept.bemerkung` | Freitext, mehrzeilig | – | Pflicht, wenn Messkonzept = „Sonstiges“. |
| 34 | entfällt | NA – PV-Module | Anzahl verschiedener Modultypen | `na.module.typen` | Auswahl | 1 / 2 | **Pflicht.** Steuerfeld für Modultyp 2. |
| 35 | entfällt | NA – PV-Module | Modultyp 1: Hersteller | `na.modul1.hersteller` | Freitext | – | **Pflicht.** |
| 36 | entfällt | NA – PV-Module | Modultyp 1: Modell | `na.modul1.modell` | Freitext | – | **Pflicht.** Vollständige Typbezeichnung laut Datenblatt. |
| 37 | entfällt | NA – PV-Module | Modultyp 1: Leistung pro Modul (Wp) | `na.modul1.wp` | Freitext (Zahl) | – | **Pflicht.** Nur die Zahl, z. B. 450. |
| 38 | entfällt | NA – PV-Module | Modultyp 1: Anzahl Module | `na.modul1.anzahl` | Freitext (Zahl) | – | **Pflicht.** Ganze Zahl. Die Gesamtleistung in kWp rechnet der Autopilot aus. |
| 39 | entfällt | NA – PV-Module | Modultyp 2: Hersteller | `na.modul2.hersteller` | Freitext | – | Pflicht, wenn Modultypen = 2. |
| 40 | entfällt | NA – PV-Module | Modultyp 2: Modell | `na.modul2.modell` | Freitext | – | Pflicht, wenn Modultypen = 2. |
| 41 | entfällt | NA – PV-Module | Modultyp 2: Leistung pro Modul (Wp) | `na.modul2.wp` | Freitext (Zahl) | – | Pflicht, wenn Modultypen = 2. |
| 42 | entfällt | NA – PV-Module | Modultyp 2: Anzahl Module | `na.modul2.anzahl` | Freitext (Zahl) | – | Pflicht, wenn Modultypen = 2. |
| 43 | entfällt | NA – Wechselrichter | Anzahl verschiedener Wechselrichter-Typen | `na.wr.typen` | Auswahl | 1 / 2 / 3 | **Pflicht.** Steuerfeld. Mehrere **gleiche** WR erfasst „Anzahl“ (#48), nicht ein weiterer Typ. |
| 44 | entfällt | NA – Wechselrichter | WR 1: Hersteller | `na.wr1.hersteller` | Freitext | – | **Pflicht.** |
| 45 | entfällt | NA – Wechselrichter | WR 1: Name (Produktbezeichnung) | `na.wr1.name` | Freitext | – | Optional, z. B. „Sunny Tripower“. |
| 46 | entfällt | NA – Wechselrichter | WR 1: Modell | `na.wr1.modell` | Freitext | – | **Pflicht.** Typbezeichnung laut Typenschild, z. B. „STP 8.0-3AV-40“. |
| 47 | entfällt | NA – Wechselrichter | WR 1: Nennleistung AC (W) | `na.wr1.leistung_w` | Freitext (Zahl) | – | **Pflicht.** In Watt, z. B. 8000 (nicht 8). Hybrid-WR hier erfassen; der Speicher kommt zusätzlich in #59–62. |
| 48 | entfällt | NA – Wechselrichter | WR 1: Anzahl | `na.wr1.anzahl` | Freitext (Zahl) | – | **Pflicht.** Ganze Zahl. |
| 49 | entfällt | NA – Wechselrichter | WR 2: Hersteller | `na.wr2.hersteller` | Freitext | – | Pflicht, wenn WR-Typen ≥ 2. |
| 50 | entfällt | NA – Wechselrichter | WR 2: Name (Produktbezeichnung) | `na.wr2.name` | Freitext | – | Optional. |
| 51 | entfällt | NA – Wechselrichter | WR 2: Modell | `na.wr2.modell` | Freitext | – | Pflicht, wenn WR-Typen ≥ 2. |
| 52 | entfällt | NA – Wechselrichter | WR 2: Nennleistung AC (W) | `na.wr2.leistung_w` | Freitext (Zahl) | – | Pflicht, wenn WR-Typen ≥ 2. In Watt. |
| 53 | entfällt | NA – Wechselrichter | WR 2: Anzahl | `na.wr2.anzahl` | Freitext (Zahl) | – | Pflicht, wenn WR-Typen ≥ 2. |
| 54 | entfällt | NA – Wechselrichter | WR 3: Hersteller | `na.wr3.hersteller` | Freitext | – | Pflicht, wenn WR-Typen = 3. |
| 55 | entfällt | NA – Wechselrichter | WR 3: Name (Produktbezeichnung) | `na.wr3.name` | Freitext | – | Optional. |
| 56 | entfällt | NA – Wechselrichter | WR 3: Modell | `na.wr3.modell` | Freitext | – | Pflicht, wenn WR-Typen = 3. |
| 57 | entfällt | NA – Wechselrichter | WR 3: Nennleistung AC (W) | `na.wr3.leistung_w` | Freitext (Zahl) | – | Pflicht, wenn WR-Typen = 3. In Watt. |
| 58 | entfällt | NA – Wechselrichter | WR 3: Anzahl | `na.wr3.anzahl` | Freitext (Zahl) | – | Pflicht, wenn WR-Typen = 3. |
| 59 | entfällt | NA – Batteriespeicher | Batteriespeicher vorhanden | `na.speicher` | Auswahl | Ja / Nein | **Pflicht.** Steuerfeld für #60–62 und #70. |
| 60 | entfällt | NA – Batteriespeicher | Speicher: Hersteller | `na.speicher.hersteller` | Freitext | – | Pflicht, wenn Speicher = Ja. |
| 61 | entfällt | NA – Batteriespeicher | Speicher: Modell | `na.speicher.modell` | Freitext | – | Pflicht, wenn Speicher = Ja. Bei modularen Speichern das Gesamtsystem, z. B. „HVS 7.7“. |
| 62 | entfällt | NA – Batteriespeicher | Speicher: Speicherkapazität (kWh) | `na.speicher.kwh` | Freitext (Zahl) | – | Pflicht, wenn Speicher = Ja. **Nutzbare** Kapazität laut Datenblatt (das MaStR fragt die nutzbare ab), z. B. 8,7. |
| 63 | entfällt | NA – Wallbox | Wallbox vorhanden | `na.wallbox` | Auswahl | Ja / Nein | **Pflicht.** Steuerfeld für #64–66 und #71. |
| 64 | entfällt | NA – Wallbox | Wallbox: Hersteller | `na.wallbox.hersteller` | Freitext | – | Pflicht, wenn Wallbox = Ja. |
| 65 | entfällt | NA – Wallbox | Wallbox: Modell | `na.wallbox.modell` | Freitext | – | Pflicht, wenn Wallbox = Ja. |
| 66 | entfällt | NA – Wallbox | Wallbox: Ladeleistung (kW) | `na.wallbox.kw` | Freitext (Zahl) | – | Pflicht, wenn Wallbox = Ja. Ladeleistung wie installiert, z. B. 11. Über 4,2 kW fügt der Autopilot dem Duisburg-Antrag den Mantelbogen hinzu. |
| 67 | bleibt | NA – Unterlagen | Übersichtsschaltplan | `na.schaltplan` | PDF | max. 1 Datei | **Pflicht.** Export aus Reonic unter Planung > Schaltplan (4.4). |
| 68 | entfällt | NA – Unterlagen | Datenblatt Module | `na.datenblatt.module` | PDF | mehrere Dateien | **Pflicht.** Je Modultyp ein Datenblatt. Anforderung a im Bild-Feld „Datenblätter“. |
| 69 | entfällt | NA – Unterlagen | Datenblatt Wechselrichter | `na.datenblatt.wr` | PDF | mehrere Dateien | **Pflicht.** Je WR-Typ ein Datenblatt. Anforderung b. Einheiten- und NA-Schutz-Zertifikat kommen weiter aus SharePoint. |
| 70 | entfällt | NA – Unterlagen | Datenblatt Speicher | `na.datenblatt.speicher` | PDF | mehrere Dateien | Pflicht, wenn Speicher = Ja. Anforderung c. |
| 71 | entfällt | NA – Unterlagen | Datenblatt Wallbox | `na.datenblatt.wallbox` | PDF | mehrere Dateien | Pflicht, wenn Wallbox = Ja. Anforderung d. |

**Umfang ohne Empfehlung:** 9 Segmente, 71 Felder, davon 23 in jedem Projekt Pflicht. In einem typischen Projekt (ein Zähler, ein Modultyp, ein WR, Speicher, keine Wallbox) sind rund 35 Felder auszufüllen.

**Umfang mit Empfehlung (4.1 + 4.3):** 6 Segmente (Zähleranlage, Zähler 1–4, Anlage, Unterlagen), **33 Felder** (#2–33 und #67), davon **8** in jedem Projekt Pflicht (#2–6, #31, #32, #67). In einem typischen Projekt mit einem Zähler sind rund 13 Felder auszufüllen.

---

## 3. Abdeckung: Jeder Punkt der abgestimmten Datenliste hat eine Quelle

| Datenpunkt | Mit Empfehlung | Ohne Empfehlung |
|---|---|---|
| Schaltplan | #67 | #67 |
| Foto Hauptstromzähler / Zählerschrank / HAK | #2–4 | #2–4 |
| Hauptzählernummer | Reonic-Projektfeld „Zählernummer“ | #1 |
| Kaskadenschaltung | #5 | #5 |
| Abzumeldende Zähler | ermittelt der Autopilot aus Zählern mit Typ „Ausbau – Zähler abmelden“ (#7, 13, 19, 25) | wie links |
| Typ Zählerwechsel 1–4 | #7, 13, 19, 25 | wie links |
| Zählernummer 1–4 | #8, 14, 20, 26 | wie links |
| Lage der Verbrauchsstelle 1–4 | #9/10, 15/16, 21/22, 27/28 | wie links |
| Einbauort Zähler 1–4 | #11, 17, 23, 29 | wie links |
| 3-Punkt / eHZ 1–4 | #12, 18, 24, 30 | wie links |
| Datenblätter | Reonic-Katalog | #68–71 |
| Neuerrichtung / Erweiterung | #31 | #31 |
| Modul: Hersteller, Modell, Wp pro Modul, Anzahl | Hauptvariante + Katalog | #35–42 |
| Gesamtleistung kWp | rechnet der Autopilot | rechnet der Autopilot |
| WR: Name, Hersteller, Modell, W-Leistung, Anzahl | Hauptvariante + Katalog | #44–58 |
| Batteriespeicher (ja/nein), Hersteller, Modell, Kapazität | Hauptvariante + Katalog | #59–62 |
| Wallbox vorhanden, Leistung, Modell, Hersteller | Hauptvariante + Katalog | #63–66 |
| Messkonzept | #32 (+ #33) | #32 (+ #33) |

---

## 4. Vorschläge im Detail

### 4.1 Komponenten aus der Reonic-Planung lesen (Empfehlung)

Wenn du in Reonic planst, liegt die Planung als **Variante** im Projekt, auch ohne Angebot und Unterschrift. Reonic liefert dazu je Position Hersteller, Modell und Anzahl. Aus dem Katalog kommen Wp, AC-Leistung, Phasen, Speicherkapazität, Ladeleistung und das Datenblatt.
- **Vorteil:** Die Felder #34–66 und #68–71 entfallen (37 Felder). Es gibt keine Tippfehler und keine doppelte Pflege.
- **Voraussetzungen:**
  - Die Planung enthält auch WR, Speicher und Wallbox, nicht nur die Modulbelegung.
  - Die technischen Werte und Datenblätter sind im Katalog gepflegt.
  - Du markierst die gültige Variante als **Hauptvariante**.

### 4.2 Alternative: Auswahlliste für Hersteller und Modell

Nur falls 4.1 nicht passt, weil die Planung in Reonic die Komponenten nicht vollständig enthält. Dann tippt der Monteur Hersteller, Modell und Leistung nicht frei ein, sondern wählt aus eurem Standardsortiment per **Dropdown**, z. B. „SMA Sunny Tripower 8.0“. Für Ausnahmen gibt es „Anderes Modell“ mit den Freitextfeldern aus Abschnitt 2.
- Die technischen Werte stehen einmal je Produkt in einer **Produkttabelle im Autopilot**: Wp, AC-Leistung, Scheinleistung kVA, Phasen, AC/DC-Kopplung, nutzbare kWh, Datenblatt, Einheiten- und NA-Schutz-Zertifikat.
- **Vorteil:** Je WR bzw. Modul ein Feld statt vier bis fünf. Die Werte sind immer gleich geschrieben. Datenblätter müssen nicht pro Projekt hochgeladen werden.

### Neue Produkte

**Mit Empfehlung (4.1):** Du legst das neue Produkt wie gewohnt im **Reonic-Katalog** an, mit Wp bzw. Leistung bzw. Kapazität und dem Datenblatt. Mehr ist nicht zu tun. Der Autopilot liest es aus der Planung.

**Mit der Alternative (4.2):** Die Auswahlwerte der Checkliste lassen sich nur von Hand im Reonic-Editor pflegen. Ein neues Produkt hält trotzdem kein Projekt auf:
1. **Sofort nutzbar:** Ist das Produkt noch nicht in der Liste, wählt der Monteur „Anderes Modell“ und füllt die Freitextfelder aus. Das Projekt läuft normal weiter.
2. **Produkttabelle füllt sich selbst:** Der Autopilot liest die Werte aus dem hochgeladenen Datenblatt (#68–71) und legt einen Entwurf an. Ein Mensch bestätigt ihn per HubSpot-Task.
3. **Liste nachziehen:** Wird ein Produkt zum zweiten Mal als „Anderes Modell“ erfasst, bekommst du einen HubSpot-Task: „Produkt X in die Auswahlliste aufnehmen.“ Neue Projekte bekommen dann die neue Version der Checkliste, laufende Projekte behalten ihre.

### 4.3 Hauptzählernummer im Reonic-Projektfeld (Empfehlung)

Reonic-Projekte haben ein Standardfeld „Zählernummer“. Pflegst du dort die Hauptzählernummer, entfällt #1. Dann gibt es nur eine Stelle für die Nummer statt zwei.

### 4.4 Schaltplan aus dem Reonic-Schaltplan-Editor

Reonic erzeugt aus der Planung einen Übersichtsschaltplan mit Strings, WR, Speicher (AC/DC), Wallbox, Hausanschluss und Zählern. Ihn als PDF zu exportieren und in #67 hochzuladen ist schneller, als ihn separat zu zeichnen. Offen ist, ob er Netze Duisburg reicht (Frage 4).

### 4.5 Abgeschlossenes Segment als Startsignal

Reonic meldet, wenn ein Segment als abgeschlossen markiert wird. Dann prüft der Autopilot die Reonic-Daten sofort und zeigt am HubSpot-Deal, ob sie vollständig sind, schon bevor die Vollmacht zurück ist. Fehlende Angaben fallen so früher auf. Dafür richtest du in Reonic einmalig einen Webhook ein (Einstellungen > API / Developers > Webhooks). Die Adresse dafür schicke ich dir.

### 4.6 Fotos vom Kunden über Reonic-Dateianfragen

Über das Reonic-Kundenportal kannst du Fotos von Zähler, Zählerschrank und HAK vorab beim Kunden anfordern. Texteingaben gehen dort nicht. Die Zählernummer kann der Autopilot aus dem Foto lesen, der Monteur bestätigt sie. Das spart Rückfragen, wenn vor Ort etwas fehlt.

### 4.7 Zusätzliche Formularwerte aus den Datenblättern (Empfehlung)

Netze Duisburg fragt in den Formularen mehr ab, als in der abgestimmten Datenliste steht. **Festgelegt:** Diese Werte kommen aus den **Datenblättern**, nicht aus der Checkliste. Der Autopilot liest sie einmal je Produkt aus, ein Mensch bestätigt. Das Datenblatt kommt bei Empfehlung aus dem Reonic-Katalog, bei der Alternative aus dem Upload in #68–71.

| Wert | Formular | Quelle |
|---|---|---|
| WR: Scheinleistung (kVA), Bemessungsstrom, Phasen | E.2, E.3, E.8 | Datenblatt WR |
| Speicher: AC- oder DC-gekoppelt | E.3 | Datenblatt Speicher |
| Speicher: nutzbare Kapazität | MaStR | Datenblatt Speicher |
| Inselnetz bildendes System / allpolige Trennung bei Netzersatzbetrieb | E.3 | Datenblatt Speicher bzw. WR |
| Blindleistungsverfahren, integrierter NA-Schutz | E.4, E.6, E.8 | Datenblatt bzw. Einheiten-/NA-Schutz-Zertifikat (SharePoint) |

**Nicht im Datenblatt** stehen diese projektbezogenen Angaben. Sie klären wir, bevor wir Erweiterungen und die Inbetriebnahme (E.8) automatisieren:

| Angabe | Formular | Vorschlag |
|---|---|---|
| Daten der Bestandsanlage bei Erweiterung (kWp, WR, Inbetriebnahmejahr, MaStR-Nr.) | E.2, MaStR | eigenes Segment, nur bei Erweiterung |
| Inbetriebnahme: Datum, Abweichungen vom geplanten Stand, Phasenaufteilung | E.8, MaStR | kleiner Block in der Baustellendokumentation |

### 4.8 Neuerrichtung/Erweiterung: Reonic ist führend

**Festgelegt:** Die Angabe wird in Reonic abgefragt (#31) und ist führend. Das HubSpot-Feld „Bestandsanlage vorhanden“ nutzt der Autopilot nur zur Gegenprüfung. Widersprechen sich beide, bekommt ihr einen HubSpot-Task.

---

## 5. Fragen an dich (Termin 09.11.)

1. Wird die **Beratungs-Checkliste** in Reonic genutzt, obwohl das Angebot in HubSpot entsteht? Wer füllt sie aus, und wann? Muss das Reonic-Projekt dafür in die Phase „Beratung“ wechseln?
2. Was genau planst du in Reonic: nur die Modulbelegung oder auch WR, Speicher und Wallbox? Gibt es pro Projekt eine oder mehrere Varianten? (entscheidet über 4.1)
3. Wie groß ist euer Standardsortiment (Module, WR, Speicher, Wallboxen)? Ein Dutzend Produkte oder deutlich mehr? (wichtig für die Alternative 4.2)
4. Pflegst du das Reonic-Feld **Zählernummer** am Projekt? (4.3) Reicht der Reonic-Schaltplan Netze Duisburg als Übersichtsschaltplan? (4.4)
5. Passen die Werte für **Typ Zählerwechsel** zum Zählerantrag von Netze Duisburg? Fehlt etwas, z. B. Wandlermessung?
6. Welche **Messkonzepte** kommen bei euch vor, und welche Nummern haben sie im Duisburger Auswahlblatt?
7. Reichen 4 Zähler? Bei der Alternative 4.2 außerdem: Reichen 2 Modultypen und 3 WR-Typen?
8. Gibt es im Checklisten-Editor eine **Sichtbarkeitsbedingung** („nur zeigen, wenn …“)? Dann würde die Checkliste für den Monteur noch kürzer.
9. Markierst du in Reonic eine Variante als **Hauptvariante**, und ist das immer die, die gebaut wird?
10. Sind im Reonic-**Katalog** für alle verwendeten Produkte Wp, AC-Leistung, Phasen, Kapazität, Ladeleistung und das **Datenblatt** gepflegt?
11. Ist der **API-Key** für den Autopilot ein v3-Key (beginnt mit `rnc_v3_`) mit Leserechten auf Projekte, Checklisten, Komponenten und Dateien?
12. Wo entstehen **Angebot und Auftragsbestätigung**: in Reonic oder in HubSpot? Wie kommen die Artikel an den HubSpot-Deal?

---

## Anhang: Für die Technik – wie der Autopilot die Daten abruft

Der Autopilot liest Reonic nur lesend über die offizielle REST API v3.

| Thema | Wert | Doku |
|---|---|---|
| Basis-URL | `https://api.reonic.com/rest/v3/` | [API-Referenz](https://api.reonic.com/rest/v3/docs) · [OpenAPI-Spezifikation](https://api.reonic.com/rest/v3/openapi) |
| Zugang | API-Key im Header `X-Authorization` | [Me](https://api.reonic.com/rest/v3/docs#tag/me): `GET /me` zeigt Key-Typ und Rechte |
| Aktualität | Header `Reonic-Cache-Control: no-cache`, damit immer der aktuelle Stand kommt | – |
| Projekt | Die Reonic-Projekt-ID steht am HubSpot-Projekt. | – |

**Checkliste (Abschnitt 2)**

| Endpunkt | Doku | Liefert |
|---|---|---|
| `GET /checklists/{projectId}?stage=offer` | [Checklists](https://api.reonic.com/rest/v3/docs#tag/checklists) | die Checkliste der Phase „Beratung“ mit allen Werten, Fotos und PDFs |
| `POST /projects/{projectId}/checklist/export` | [Checklists](https://api.reonic.com/rest/v3/docs#tag/checklists) | die Checkliste als PDF (Notlösung, braucht Schreibrecht) |

**Vorschläge (Abschnitt 4)**

| Vorschlag | Endpunkt | Doku | Liefert |
|---|---|---|---|
| 4.1 Varianten | `GET /residentialProjects/{projectId}/variants` | [Residential Project Offer Variants](https://api.reonic.com/rest/v3/docs#tag/residential-project-offer-variants) | alle Varianten; die mit `isPrimary = true` gilt |
| 4.1 Positionen | `GET /residentialProjects/{projectId}/variants/{variantId}` | [Residential Project Offer Variants](https://api.reonic.com/rest/v3/docs#tag/residential-project-offer-variants) | Komponenten mit Hersteller, Modell und Anzahl |
| 4.1 Technische Daten | `GET /components/{componentId}/versions/{versionId}` | [Components](https://api.reonic.com/rest/v3/docs#tag/components) | Wp, AC-Leistung, Phasen, Kapazität, Ladeleistung, Datenblatt |
| 4.2 Katalog | `GET /components?componentType=module&componentType=inverter&componentType=batteryStorage&componentType=evCharger` | [Components](https://api.reonic.com/rest/v3/docs#tag/components) | Sortiment mit Datenblättern |
| 4.3 Zählernummer | `GET /residentialProjects/{projectId}` | [Residential Projects](https://api.reonic.com/rest/v3/docs#tag/residential-projects) | Feld `meterNumber` |
| 4.4 Schaltplan in den Projektdateien | `GET /files?parentType=residentialProject&parentId={projectId}` · `GET /files/{fileId}` · `GET /fileFolders?parentType=residentialProject&parentId={projectId}` | [Files](https://api.reonic.com/rest/v3/docs#tag/files) · [File Folders](https://api.reonic.com/rest/v3/docs#tag/file-folders) · [Hilfe: Schaltplan](https://docs.reonic.com/docs/en/offers-plan-circuit-plan-base) | nur nötig, wenn der Schaltplan in den Projektdateien statt in #67 liegt |
| 4.5 Segment abgeschlossen | Webhook `projectChecklist_segmentCompletionChanged` | [Webhooks](https://api.reonic.com/rest/v3/docs#tag/webhooks) | Meldung an den Autopilot; Einrichtung im Reonic-Portal |
| 4.6 Kundenfotos | `GET /files?parentType=residentialProject&parentId={projectId}&createdByCustomer=true` | [Files](https://api.reonic.com/rest/v3/docs#tag/files) · [Hilfe: Dateianfragen](https://docs.reonic.com/docs/en/settings-customer-communication-templates-for-file-requests) | nur vom Kunden hochgeladene Dateien |
| 4.8 Gegenprüfung Bestandsanlage | `GET /residentialProjects/{projectId}` | [Residential Projects](https://api.reonic.com/rest/v3/docs#tag/residential-projects) | Feld `existingSystems` (Angabe des Kunden bei der Anfrage) |

Weitere Doku: [Hilfe: Checklisten](https://docs.reonic.com/docs/en/settings-checklists-checklists-overview) · [Hilfe: Beratungs-Checkliste konfigurieren](https://docs.reonic.com/docs/en/settings-checklists-offer-consultation)
