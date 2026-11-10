# Termin 4 – 09.11.2026 (Review mit Michael)

Teilnehmer: Michael Roquet (reinsolar), Martin Miazek · Dauer ca. 1:20 h

## Beschlüsse
- **Netze Duisburg: Einreichung immer über das Portal.** Damit ist der Versandkanal entschieden, Mail entfällt.
- **MaStR-Passwort bleibt am Deal.** Kunden werden gebeten, ihr Passwort nach Abschluss zu ändern. Das bestätigt die Abweichung vom Beschluss vom 26.10.
- **MaStR-Login** kommt künftig über ein HubSpot-Formular in eine Property am Deal. Das Formular baut Michael noch.
- **MaStR direkt nach der Inbetriebnahme**, nicht erst nach dem Zählerwechsel. Die Registrierungsbestätigung wird mit der Fertigmeldung an den Netzbetreiber geschickt, weil sie für die Auszahlung der Einspeisevergütung nötig ist. Voraussetzung ist das Inbetriebnahmedatum, also das Ende der Montage.
- **25-kW-Grenze Netze Duisburg** bezieht sich auf **kWp**. Wir gehen erstmal nach Trial and Error vor.
- **HubSpot-Objekt:** vorerst der **Deal**. Später will Michael bei „Auftrag erhalten“ aus dem Deal ein Projekt (eigene Projektpipeline, verknüpft mit dem Deal) erzeugen. Der Autopilot liest dann über das Projekt den Deal.
- **Angebot und Unterschrift in Reonic:** Planung, Angebot und Unterschrift des Kunden laufen in Reonic. Der unterschriebene Auftrag ist in Reonic danach gesperrt.
- **Neuer vorgelagerter Schritt:** Nach der Unterschrift in Reonic überträgt der Autopilot die Auftragsdaten (Komponenten) nach HubSpot. Das ersetzt die bisherige, unzuverlässige Übertragung per KI. Trigger für die Netzanmeldung bleibt die Pipeline-Phase in HubSpot.
- **Datenblätter in SharePoint.** Ob Datenblätter zusätzlich an der Reonic-Komponente liegen, überlegt Michael (je Feld nur eine Datei möglich).
- **Schaltplan** erzeugt Reonic automatisch im unterzeichneten Auftrag.
- **Hauptzählernummer** im Reonic-Projektfeld „Zählernummer“. Weitere Zähler (Wechsel, Abmeldung) über die Checkliste.
- **Postfach anschlussplanung@reinsolar.de** (Exchange, Shared Mailbox) wird das Postfach für Netzbetreiber-Korrespondenz. Der Installateur soll künftig darüber arbeiten statt über das eigene Postfach. Weiterleitungsregeln je Netzbetreiber sind nur die Notlösung.

## Änderungen an der Anlage vor der Fertigmeldung
- Kommt selten vor. Wechselrichter ändern sich nie.
- Relevant sind nur **Modulanzahl, Speichergröße, Wallbox**.
- Erfassung in der Reonic-Checkliste **Baustellendokumentation** (nach der Montage). Es wird nur die tatsächlich installierte **Anzahl Module** erfasst, die kWp rechnet der Autopilot über den Modultyp aus.
- Bestätigung vor dem Versand: HubSpot-Aufgabe plus 2–3 Properties am Deal („keine Änderung“ / „geändert“). Michael wünscht sich eher eine Eingabemaske („Das wird gemeldet – stimmt das?“). HubSpot-App-Karten sind eine Option, für Reonic gibt es keine.
- **Umsetzung später**, kurz vor dem Go-live Duisburg.

## Prozess bei reinsolar (Stand heute)
- **Reonic:**
  - Anfragen → Angebote (Vorabplanung, Vor-Ort-Termin, Detailplanung = finales Angebot) → Auftrag erhalten → Auftragsbestätigung (über sevDesk) → Installationen.
  - Leads aus Reonic erzeugen automatisch einen Deal in HubSpot („Angebot für …“).
  - Leads aus Anruf oder Mail legt Michael in HubSpot an und importiert sie in Reonic. Dabei entsteht ein zweiter Deal, den Michael zusammenführt; der Reonic-Deal ist Master.
- **Reonic-Checklisten:** Die Beratungs-Checkliste ist relevant für die Netzanmeldung, die Baustellendokumentation für die Fertigmeldung.
- **HubSpot:**
  - Die Sales-Pipeline läuft.
  - Die Umsetzung läuft heute noch als zweite Deal-Pipeline, die Projektpipeline gibt es nur als Test.
  - Die Fertigmeldung wird ausgelöst beim Schieben von „Montage“ nach „Schlussrechnung und Fertigmeldung“.
- **Fertigmeldung im Portal Netze Duisburg:** fragt nach Änderungen, Fotos, Zählernummer und MaStR-Registrierungsbestätigung.

## Zugänge
- **HubSpot:** Martin hat jetzt Super-Admin mit Basislizenz und sieht damit Sales-Workspace, Pipelines und Datensätze.
- **Reonic:** Login über Michaels Konto mit Anmeldecode (Lizenzen nur jahresweise). Martin meldet sich vorher per WhatsApp.
- **OpenAI-Plattform:** Die Einladung von Martin ist im Termin fehlgeschlagen. Michael versucht es erneut.
- **Portal Netze Duisburg:** Zugang liegt Martin vor.

## Offene Fragen
- Wie bilden wir Änderungen nach der Unterschrift sauber ab (Eingabemaske in HubSpot, Checkliste Baustellendokumentation)? Klärung vor dem Go-live Duisburg.
- Netzform Hausanschluss / Symmetrieprüfung (E.8): weiter offen.
- Welche Herstellerdokumente (Konformitätserklärung, NA-Schutz-Zertifikat) braucht es je Produkt, und wo liegen sie?
- Go-live Duisburg 04.12. ist mit Martins Urlaub (bis 30.11.) und dem nächsten Termin (Dezember) neu zu planen.

## To-dos
**Michael**
- Postfach anschlussplanung@reinsolar.de für Martin bzw. den Autopiloten freigeben und den Installateur darauf umstellen.
- Martin erneut in die OpenAI-Plattform einladen.
- Unterschrift als Bild schicken.
- Reonic-Checkliste prüfen und anpassen (nur die Tabelle mit den Datenpunkten), inkl. Feld für Änderungen in der Baustellendokumentation.
- Reonic-Projektfeld „Zählernummer“ künftig pflegen.
- HubSpot-Formular für den MaStR-Login bauen.
- HubSpot-Projektpipeline aufsetzen, Aufträge übernehmen, NAB-Datenkarte aufräumen.
- Ablage der Datenblätter festlegen (SharePoint-Ordner, ggf. Reonic-Komponente).
- Für den nächsten Termin vorbereiten: welche Infos woher, Pipeline, Datenkarten.

**Martin**
- Protokoll und Reonic-Checkliste als Word-Datei an Michael.
- MaStR-Strecke noch vor dem Urlaub starten.
- Prozess skizzieren: Reonic (Unterschrift) → HubSpot → Netzanmeldung → Fertigmeldung inkl. MaStR.
- Code anpassen: Portal als Versandkanal Duisburg, 25-kW-Grenze auf kWp, MaStR vor bzw. mit der Fertigmeldung, Komponenten aus der unterschriebenen Reonic-Variante.
- Neues Feature: Reonic → HubSpot nach Unterschrift.
- Bestätigungsschritt „Änderungen an der Anlage“ vor dem Go-live einplanen.

## Nächster Termin
Martin ist ab 13.11. im Urlaub und ab 01.12. zurück; im Chat erreichbar. Nächster Termin in der zweiten Dezemberwoche, ab dem 08.12., 10:00 Uhr, 1,5 h (Einladung verschickt).
