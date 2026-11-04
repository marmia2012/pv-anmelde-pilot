# 04 – Datenfluss und Feldmapping

Quelle: Miro-Board „Dataflow Reinsolar“ (Stand 17.10.2026) + Kickoff 07.10.2026.

## 1. Datenquellen

### HubSpot
- Ansprechpartner reinsolar
- Kunde: Anrede, Vorname, Nachname, E-Mail, Telefon, IBAN, Kundenadresse
- Anlage: Termin Elektroinstallation/Dachmontage (der spätere gewinnt), Name, Vorname, Adresse
- Anlagenbetreiber: Name, Vorname, Adresse
- Anlagenerrichter: Firma, Ort, Eintragungsnummer beim NB, Netzbetreiber
- Unterlagen: EVU-Vollmacht (PDF), Lageplan (aus TIM-online), NH-Sicherung = 63 A (Default)
- Netzanschluss/Bestand: Bestandsanlage vorhanden; Infos zur Bestandsanlage; Einspeiseart: Volleinspeisung / Teileinspeisung (= Default)
- Reonic-Projekt-ID
- MaStR-Kundenlogin (über Webformular auf der reinsolar-Website)

### Reonic
- Schaltplan, Foto Hauptstromzähler, Foto Zählerschrank, Foto Hausanschlusskasten (HAK), Hauptzählernummer, Kaskadenschaltung (bool), abzumeldende Zähler
- Neue Anlage: Datenblätter, Neuerrichtung/Erweiterung
- Module: Hersteller, Modell, Wattleistung pro Modul, Anzahl → Gesamtleistung kWp berechnen
- Wechselrichter: Name, Hersteller, Modell, W-Leistung, Anzahl (Liste? oder über Artikel der Auftragsbestätigung – offen)
- Batteriespeicher (bool), Hersteller, Modell, Kapazität (z. B. 8,7 kWh)
- Wallbox (bool), Leistung, Modell, Hersteller
- Messkonzept

### SharePoint
- Datenblätter, Einheitenzertifikat, NA-Schutz-Zertifikat (je Produkt)

## 2. Hauptprozess
1. **Trigger:** HubSpot-Projekt-Pipeline „Termine an Kunde gesendet & unterschriebene Vollmacht zurück“
2. **Starte Anmeldeprozess**
3. **NAB-Datenkarte aggregieren** aus HubSpot, Reonic, SharePoint
4. **NAB-Karte speichern** in Prozess-DB + HubSpot
5. **Netzbetreiber ermitteln** (PLZ-Tabelle; optional VNB Digital)
6. **Netzbetreiber speichern** in Prozess-DB + HubSpot
7. **Verzweigung je Netzbetreiber** → Netze Duisburg | RheinNetz/ENNI | Westnetz | …

## 3. Zweig Netze Duisburg
`< 25 kW?` → Ja → **Antrag stellen (Mail oder Portal – offen)** → `Rückfrage?` → Nein → **Einspeisezusage per Mail → in HubSpot ablegen** → **Inbetriebnahme (E.8)** → **Zählerwechsel** → **MaStR**

Noch nicht modelliert: `< 25 kW = Nein` und `Rückfrage = Ja`.

### Antrag – Feldmapping
- Unterlagen: Lageplan, Schaltplan (Reonic), Antragstellung-PDF, Datenblatt, EVU-Vollmacht
- **E.2 Erzeugungsanlage:** Erzeugungseinheiten = Wechselrichter; Inselbetrieb = Ja; motorischer Anlauf = Nein; Lieferung ins Netz = Ja
- **E.4 Einheitenzertifikat:** Datenblätter; Blindleistung = nicht vorhanden
- **E.6 NA-Schutz:** vom Hersteller
- **Messkonzept:** offen
- **E.3 Speicher:** AC/DC gekoppelt (Datenblatt); AC → Wechselstrom L1/L2/L3, DC → Drehstrom; allpolig = Ja; NA-Schutz = Ja; WR-Bemessungsstrom (Datenblatt); Anschlusskonzept = 8; Übersichtsschaltplan = Schaltplan; Primärenergieträger = Sonne; Speicher bezieht nicht vom Netz = Ja; Einheitenzertifikate; Einspeisemanagement: ferngesteuert nein, 60, ja
- **Errichter:** Nummer, Netzbetreiber, Errichterunterschrift (Bild – liefert Michael)
- **Wallbox > 4,2 kW Ladeleistung → Mantelbogen:** separater Zähler = Ja; MK = 2; Fachplaner = Michael; Unterschrift Michael
- Inselnetz bei Speichern: E.3 fragt „Inselnetz bildendes System“ und „allpolige Trennung bei Netzersatzbetrieb“ → Ja/Nein je Produkt
- Blindleistung, E.7 Prüfbericht NA-Schutz, Inselnetz: per Trial and Error in der Umsetzung klären

### Inbetriebnahme – E.8 Inbetriebsetzungsprotokoll
Scheinleistung aus Anfrage; installierte kWp; Übereinstimmung E.2/E.3 = Ja; Abrechnungsmessung = Ja; Einheitenzertifikat = Ja; Leistungsüberwachung = Nein; Zertifikat NA-Schutz = Ja; integrierter NA-Schutz = Ja; Drosselung = Ja; Energieflussrichtungssensor = Ja.
Ab Abschnitt Symmetrie (offen, Martin klärt): Symmetriebedingung (dreiphasiger WR oder Phasenaufteilung), Blindleistungsverfahren Q(U)/cos φ(P) nach VNB-Vorgabe, TF-Sperren falls in der Zusage gefordert, Unterschriften.

### Zählerwechsel
Datenbedarf noch offen.
