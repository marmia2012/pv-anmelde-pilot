# 01 – Projekt, Scope, Zeitplan

## Ziel
Für reinsolar GmbH eine Automatisierung bauen, die mit Daten aus Reonic und HubSpot bei 8 Netzbetreibern Folgendes automatisch erledigt:
- Einspeiseanfrage / Netzanmeldung
- Fertigmeldung / Inbetriebnahme (E.8)
- Zählerwechsel
- Eintrag im Marktstammdatenregister (MaStR)

## Rollen
- **Martin Miazek** – Entwicklung, Konzept, Betrieb in der Stabilisierungsphase
- **Michael Roquet (reinsolar)** – fachlicher Ansprechpartner, Zugänge, HubSpot-/Reonic-Struktur auf Kundenseite. Montag ist sein Homeoffice-Tag.

## Netzbetreiber (Priorität)
Top 3 laut Auswertung 2025/26 (69 von 92 Anlagen):
1. **Netze Duisburg** – 37 Anlagen, komplexester Fall → wird zuerst umgesetzt
2. **RheinNetz/ENNI** – 18
3. **Westnetz** – 14

Danach die Netzbetreiber 4–8.

## Zeitplan (bestätigt 26.10.2026)
| Meilenstein | Datum |
|---|---|
| Bau Netze Duisburg + MaStR, Dry-Run auf Staging | bis 15.11.2026 |
| Martin im Urlaub | 15.11. – Ende Nov. 2026 |
| Netze Duisburg live (erst mit Prüfschritt, dann einige Projekte, dann alle) | 04.12.2026 |
| RheinNetz/ENNI + Westnetz gebaut | bis 15.12.2026 |
| RheinNetz/ENNI + Westnetz live | Anfang Jan. 2027 |
| Restliche Netzbetreiber (4–8) | Jan. 2027 |

Danach 6 Wochen Stabilisierung (Reaktionszeit 24 h).

## Nicht im Scope
- ERP-Anbindung (war nur ein Beispiel für eine spätere Erweiterung)
- Eigene Benutzeroberfläche – UI ist ausschließlich HubSpot

## Code-Eigentum
- Das Repo liegt bei Martin. reinsolar erhält den Code nach Go-live auf Wunsch.
- Martin darf den Code für weitere Solarteure wiederverwenden → Kern generisch halten, Kundenspezifisches in Konfiguration.

## Wichtige Links (nur Referenz)
- Technisches Konzept (Doc): https://claude.ai/artifact/DPaErUQ1PAbwQQH2r4r5DD
- Miro „Dataflow Reinsolar“: https://miro.com/app/board/uXjVEdpxfmA=/
- Trello-Board „Projektmanagement“, Label `reinsolar`
