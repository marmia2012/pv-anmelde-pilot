# 03 – Schnittstellen: Rechercheergebnisse (26.10.2026)

## Netze Duisburg
- Keine öffentliche API. Einreichung < 30 kWp über das „PV-Anmeldeportal“: https://portal.netze-duisburg.de/sap/bc/ui5_ui5/sap/zumcui5_cg_m/index.html#/Logon (SAP UI5; deckt PV ≤ 30 kWp, Einspeise-/NVP-Anfragen, Wallbox ≤ 11 kW, Steckersolar).
- Ansatz: Browser-Automatisierung (Playwright for .NET). Zu prüfen: UI5-Apps sprechen intern mit OData-Services → ggf. direkte OData-Calls mit Session statt Klickstrecke (robuster).
- Beobachtung im Portal (26.10.): Anschlussobjekt über Zählernummer, Adresse auch manuell; Anschlussnehmer → Anlagenbetreiber übernehmbar; Anlagenerrichter = Elektroinstallateur; danach trotzdem alle Formulare als Upload. Vereinfachte Anmeldung nur ohne Speicher (irrelevant). Netzanschlussänderung ist ein eigener Antrag.
- Unterlagen < 25 kW: Lageplan (Flurstück, Grenzen, Aufstellort), Übersichtsschaltplan, E.1, E.2 + Messkonzept-Auswahlblatt, E.3 (Speicher), E.6 (NA-Schutz-Zertifikat), E.7 (Auszug Prüfbericht NA-Schutz), Zählerwechselantrag + Installateurausweis (nur wenn nicht bei ND eingetragen).
- Ablauf: Anmeldung → NVP → Einspeisezusage → (Angebote) → Zähler-/Messantrag + E.8 → Zählerwechsel/Netzanschluss terminiert ND mit Errichter → MaStR nach IBN.
- Eigene Seiten für > 25 bis 135 kW und ≥ 135 kW; Installateurportal https://installateur.netze-duisburg.de/
- Quellen: https://www.netze-duisburg.de/pv-anlagen/erzeugungs-und-speicheranlagen-unter-25-kw, https://www.netze-duisburg.de/portal-login

## MaStR
- **Entscheidung: Playwright im Webportal mit Kundenlogin.**
- Hintergrund: Der SOAP-Webdienst (WSDL https://www.marktstammdatenregister.de/MaStRAPI/wsdl/mastr.wsdl) hat Schreib-Operationen (SetRegistrierungSolar, SetRegistrierungStromspeicher, SetRegistrierungAnlagenbetreiber, SetMarktakteursuebertragungAnbieten, …). Er braucht aber einen Webdienst-Benutzer + Key je Marktakteur. Für Endkunden unrealistisch, über reinsolar wäre eine Vollmacht je Kunde nötig → verworfen.
- Testsystem: test.marktstammdatenregister.de (Test-User im Test-Webportal).
- Registrierung erst nach Inbetriebnahme.

## Reonic
- REST API v3, Doku https://api.reonic.com/rest/v3/docs. Keys unter Einstellungen > API/Developers (Read-only, Read+Write, Lead-only); eigener Auth-Header.
- Ressourcen: Projekte, Aktivitäten/Status/Tags/Notizen/Dateien, Kalender, Katalog (Komponenten, Hersteller), Checklist-Templates. Webhooks separat konfigurierbar.
- Checklisten: `GET /checklists/{projectId}` und `POST /projects/{projectId}/checklist/export` (PDF). **Offen:** Kommen die ausgefüllten Werte strukturiert? → mit dem Read-only-Key testen.
- Rate-Limit pro Workspace, exponentielles Backoff (2/4/8/16 s), Reads teils gecacht.

## HubSpot
- Plan Professional; Webhooks auf Deal-/Projekt-Status. Objekte: Deals (mit Artikeln/Line Items), Projekte (referenzieren den Deal), Kontakte.

## Microsoft Graph
- Postfach anschlussplanung@reinsolar.de (Exchange/M365). Eingehende Antworten der Netzbetreiber (Einspeisezusage-PDFs, Rückfragen) lesen und klassifizieren.

## Railway
- Region EU West Metal Amsterdam (europe-west4-drams3a); Volumes folgen der Region des Service.
- Preise: Pro 20 $/Seat/Monat + Verbrauch (RAM ~10 $/GB-Monat, vCPU ~20 $/vCPU-Monat, Volume 0,16 $/GB-Monat, Egress 0,05 $/GB).
- Logs (App, Build, HTTP) liegen unabhängig von der Region in US West → keine personenbezogenen Daten loggen.
- Postgres-Backups: Volume-Snapshots + PITR per WAL-Archiv; zusätzlich `pg_dump` offsite.
- Buckets laufen auf Tigris (Subprocessor).

## Netzbetreiber-Ermittlung
- Primär: PLZ-Tabelle für die 8 Netzbetreiber in der Prozess-DB.
- Optional: VNB Digital GraphQL-Gateway https://www.vnbdigital.de/gateway/graphql (inoffiziell, Suche über Koordinaten/PLZ) als Adapter mit Fallback.

## Lageplan
- Geobasis NRW WMS ALKIS: https://www.wms.nrw.de/geobasis/wms_nw_alkis (Layer u. a. Flurstuecke), Lizenz dl-de/zero-2-0 → automatischer Kartenausschnitt mit Flurstück, nur NRW. TIM-online-API noch zu prüfen.
