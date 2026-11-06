# Prozesssteuerung: der komplette Ablauf eines Vorgangs

Stand 04.11.2026. Interne Doku. Beschreibt, wie der Kern einen Vorgang von der Vollmacht bis zur MaStR-Registrierung führt.

## Grundprinzip: Ereignisse halten Fakten fest, Schritte entscheiden

- Ein **Vorgang** (`AnmeldeProcess`) gehört zu genau einem HubSpot-Projekt und hat genau einen Zustand (`ProcessState`).
- **Ereignisse** von außen (HubSpot-Webhook, später Postfach, Admin-Endpunkt) ändern **nie direkt den Zustand**. Sie halten nur einen **Fakt** am Vorgang fest, zum Beispiel „Einspeisezusage am 12.11. erhalten“ oder „Inbetriebnahme am 20.01.“. Danach wird ein Lauf eingeplant.
- **Schritte** (`IProcessStep`) laufen im Hangfire-Job. Sie lesen Zustand und Fakten und entscheiden über den nächsten Zustand.

Dadurch ist die Reihenfolge der Ereignisse egal. Ein Ereignis, das zu früh kommt (z. B. Inbetriebnahmedatum vor der Zusage), geht nicht verloren, sondern wird genutzt, sobald der Vorgang dort ankommt.

Doppelte Ereignisse sind harmlos, mit Einschränkungen bei Freigabe und Rückfrage:
- **Daten-Fakten** (Zusage, Inbetriebnahme, Zählerwechsel, Storno) sind idempotent; ein späteres Ereignis korrigiert ein früheres.
- **Freigabe** gilt nur, wenn das Ereignis nicht älter ist als der Eintritt in den Freigabe-Zustand (`ProcessTrigger.OccurredAt`, bei HubSpot das `occurredAt` des Webhooks, beim Admin-Endpunkt „jetzt“). Ein verspätet oder erneut zugestelltes altes Freigabe-Ereignis gibt also keine neue Revision und keine fortgesetzte Stufe frei.
- **Rückfrage**: Ist schon eine offen, ändert ein weiteres Rückfrage-Ereignis nichts – auch eine bereits erfasste Erledigung bleibt bestehen. Ist sie geschlossen (`RueckfrageGeschlossenAm`), zählt ein Rückfrage-Ereignis nur, wenn es danach eingetreten ist; ein altes, erneut zugestelltes Ereignis öffnet sie nicht wieder. Ohne `OccurredAt` gilt ein Ereignis als aktuell.

## Zustände

```
Created ─► DataCardAggregated ─► NetzbetreiberResolved
   ▲
   │ Neueinreichung nach Rückfrage (Revision + 1)
   │
Netzanmeldung:  [NetzbetreiberResolved] ─► NetzanmeldungPrepared ─► NetzanmeldungAwaitingReview ─► NetzanmeldungSubmitted
                                                                                                   │        ▲
                                                                                                   ▼        │ direkt beantwortet
                                                                                             RueckfrageOpen ┘
                                                                                                   │
                                                                     NetzanmeldungSubmitted / RueckfrageOpen ─► EinspeisezusageReceived

Inbetriebnahme: EinspeisezusageReceived ─(Fakt Inbetriebnahmedatum, Datum erreicht)─► InbetriebnahmeReady
                [InbetriebnahmeReady] ─► InbetriebnahmePrepared ─► InbetriebnahmeAwaitingReview ─► InbetriebnahmeSubmitted

Zählerwechsel:  [InbetriebnahmeSubmitted] ─► ZaehlerwechselPrepared ─► ZaehlerwechselAwaitingReview ─► ZaehlerwechselRequested
                (Netze Duisburg: Zählerantrag geht mit dem E.8 raus → direkt InbetriebnahmeSubmitted ─► ZaehlerwechselRequested)
                ZaehlerwechselRequested ─(Fakt Zählerwechseldatum)─► ZaehlerwechselCompleted

MaStR:          [ZaehlerwechselCompleted] ─► MastrPrepared ─► MastrAwaitingReview ─► MastrRegistered ─► Completed

Jederzeit:      ─► Blocked   (technischer Fehler nach allen Retries, unklarer Versand)
                ─► Cancelled (Projekt storniert)
```

### Einreichungsstufen

Jede externe Einreichung durchläuft dieselben vier Zustände. Das beschreibt `SubmissionStages`:

| Stufe | Bereit (Ready) | Vorbereitet (DryRun endet hier) | Wartet auf Freigabe | Eingereicht |
|---|---|---|---|---|
| Netzanmeldung | NetzbetreiberResolved | NetzanmeldungPrepared | NetzanmeldungAwaitingReview | NetzanmeldungSubmitted |
| Inbetriebnahme (E.8) | InbetriebnahmeReady | InbetriebnahmePrepared | InbetriebnahmeAwaitingReview | InbetriebnahmeSubmitted |
| Zählerwechsel | InbetriebnahmeSubmitted | ZaehlerwechselPrepared | ZaehlerwechselAwaitingReview | ZaehlerwechselRequested |
| MaStR | ZaehlerwechselCompleted | MastrPrepared | MastrAwaitingReview | MastrRegistered |

- Jede Einreichung läuft über `SubmissionGate`: Modus DryRun/Review/Live, Versand-Ledger und nie doppelt versenden.
- Braucht ein Netzbetreiber eine Einreichung nicht separat, springt die Stufe direkt von „Bereit“ auf „Eingereicht“. Das gibt `INetzbetreiberAdapter.Einreichungen` vor. Bei Netze Duisburg betrifft das den Zählerantrag, der mit dem E.8 rausgeht.
- Die Freigabe im Modus Review gilt **je Stufe**. Jede Stufe hat dafür eine eigene HubSpot-Property, damit eine stehengebliebene Freigabe der Netzanmeldung nicht auch das E.8 freigibt.
- **Freigabe-Property zurücksetzen:** Wir haben in HubSpot nur Lesezugriff und setzen die Property nach der Freigabe nicht zurück. HubSpot meldet eine Property nur bei einer Änderung. Steht die Checkbox einer Stufe noch von einer früheren Freigabe (z. B. vorige Revision nach Rückfrage, oder nach Block/Resume) auf „true“, muss ein Mensch sie **erst zurücksetzen und dann neu setzen**. Das steht auch im Freigabe-Task. Für die Rückfrage-Erledigung gilt dasselbe.
- **Empfehlung:** Freigabe und Rückfrage-Erledigung später als Datum-/Zeitstempel-Property statt Checkbox bzw. Auswahl abbilden (z. B. „Netzanmeldung freigegeben am“, „Rückfrage direkt beantwortet am“). Dann ist jedes Setzen eine Änderung und löst ohne Zurücksetzen aus. Die `TriggerRules` können das schon: `Values` leer = jeder nicht-leere Wert löst aus, Leeren der Property löst nichts aus. Bei der Erledigung braucht es dann je Erledigungsart eine eigene Property, weil `Resolution` an der Regel hängt.
- **Freigabe nach Block/Resume:** Eine Blockade lässt die Freigabe verfallen. Nach dem Fortsetzen in einen Freigabe-Zustand entsteht ein **neuer** Freigabe-Task (der Eintrittszeitpunkt steht im Task-Schlüssel), und ein Mensch muss erneut freigeben.
- **DryRun**: Der Vorgang bleibt in „Vorbereitet“ stehen. Wechselt die Instanz danach auf Review oder Live, plant der Watchdog vorbereitete Vorgänge neu ein und sie laufen weiter; im DryRun selbst nicht (ein erneuter Lauf dort wäre ohnehin harmlos: kein Versand, keine erneute Ablage, Ledger-Eintrag bleibt). Für Demos auf Staging lässt `Submission:ContinueAfterDryRun = true` ihn weiterlaufen, als wäre versendet worden. Die Folgeereignisse lassen sich dann über den Admin-Endpunkt simulieren. In Produktion bleibt der Schalter immer aus; in der Umgebung `Production` bricht der Start ab, wenn er gesetzt ist.
- **Unklarer Versand**: Bricht ein Versand ab, bleibt der Ledger-Eintrag auf `Sending`, der Vorgang wird blockiert und es entsteht ein Task. Ein Resume allein blockiert sofort wieder. Nach manueller Prüfung meldet ein Mensch das Ergebnis über `POST /admin/processes/{id}/submissions/{kind}/resolve`: `Sent` (angekommen → `Sent`, weiter ohne erneuten Versand) oder `NotSent` (→ `Failed`, wird erneut versendet). Das geht nur bei blockiertem Vorgang und unter dem Prozess-Lock (sonst 409): Liefe gerade ein Versand, würde `NotSent` ihn sonst doppelt auslösen. Danach wird der Vorgang fortgesetzt; bei `NotSent` im Modus Review erst nach einer neuen Freigabe. Ein tatsächlicher Versand geht im Ledger nie verloren: `MarkSent` setzt `Sent` bei einem Konkurrenzkonflikt ohne Versionsprüfung durch.

## Ereignisse (`ProcessTriggerKind`)

| Ereignis | Quelle heute | Quelle später | Fakt am Vorgang |
|---|---|---|---|
| Start | HubSpot-Stage „Vollmacht zurück“ | | legt den Vorgang an |
| ReviewApproved (je Stufe) | HubSpot-Property je Stufe | | Freigabe der Stufe (nur wenn sie gerade auf Freigabe wartet) |
| EinspeisezusageReceived | HubSpot-Property (Datum) oder Admin | Postfach + KI-Klassifizierung | Datum, ggf. Vorgangsnummer des Netzbetreibers |
| RueckfrageReceived | HubSpot-Property oder Admin | Postfach + KI | Rückfrage offen seit |
| RueckfrageResolved | HubSpot-Property: „direkt beantwortet“ oder „neu einreichen“ | | Erledigungsart |
| InbetriebnahmeReported | HubSpot-Property Inbetriebnahmedatum | | Inbetriebnahmedatum |
| ZaehlerwechselCompleted | HubSpot-Property (Datum) oder Admin | Postfach | Zählerwechseldatum |
| MastrCredentialsProvided | HubSpot-Property (Webformular) | | keiner; nur ein neuer Lauf |
| Cancelled | HubSpot-Stage „storniert“ | | Storno angefordert |

Welche Property welches Ereignis auslöst, ist pro Instanz konfiguriert (`HubSpot:TriggerRules`).

## Rückfrage

1. Fakt „Rückfrage“: der Vorgang geht nach `RueckfrageOpen`, und es entsteht ein HubSpot-Task.
2. Ein Mensch erledigt die Rückfrage:
   - **Direkt beantwortet** (per Mail oder Telefon): der Vorgang geht zurück nach `NetzanmeldungSubmitted` und wartet weiter auf die Zusage.
   - **Neu einreichen**: Revision + 1, der Vorgang geht nach `Created`. Die Datenkarte wird neu aggregiert, damit korrigierte Daten aus HubSpot/Reonic greifen. Die Neueinreichung hat einen eigenen Idempotenz-Schlüssel (`…:Netzanmeldung:r1`).
3. Kommt die Einspeisezusage, während die Rückfrage offen ist, gewinnt die Zusage.
4. Beim Schließen (direkt beantwortet, Neueinreichung oder Zusage) merkt sich der Vorgang den Zeitpunkt (`RueckfrageGeschlossenAm`). Eine neue Rückfrage zählt nur, wenn ihr Ereignis danach eingetreten ist. Für eine zweite Rückfrage über dieselbe HubSpot-Checkbox gilt: erst zurücksetzen, dann neu setzen.

## Inbetriebnahme (E.8)

Das E.8 meldet eine erfolgte Inbetriebnahme. Der Vorgang geht deshalb erst von `EinspeisezusageReceived` weiter, wenn das Inbetriebnahmedatum bekannt **und erreicht** ist (Datum in deutscher Zeit, Europe/Berlin). Ein vorab gemeldetes Datum in der Zukunft wartet; der Watchdog plant den Vorgang an diesem Tag neu ein (bei werktäglichem Lauf ein Wochenend-Datum am folgenden Montag).

## Storno

Fakt „Storno“. Beim nächsten Lauf geht der Vorgang nach `Cancelled`, auch aus `Blocked`. Wurde schon etwas beim Netzbetreiber eingereicht, entsteht ein Task „Antrag beim Netzbetreiber zurückziehen“. Das erledigt ein Mensch. Maßgeblich ist das Versand-Ledger: Einträge mit Status `Sent` oder `Sending` (unklar) zählen, DryRun-Einträge nicht.

## MaStR-Login

Der Kundenlogin wird nur in Review/Live gebraucht. Fehlt er, entsteht ein Task „MaStR-Login fehlt“ und der Vorgang wartet in `ZaehlerwechselCompleted`, bis `MastrCredentialsProvided` einen neuen Lauf auslöst. Im DryRun wird der Login weder gelesen noch verlangt, damit die Demo auf Staging durchläuft. Nach der Registrierung wird das Passwort gelöscht, auch im Wiederholungslauf. Die MaStR-Nummern stehen im Ledger als `anlage=SEE…;speicher=SSE…` und werden daraus wiederhergestellt.

## Fristen und Selbstheilung (Watchdog)

Ein täglicher Hangfire-Job (`ProcessWatchdog`):
- **Überfällig:** Wartet ein Vorgang länger als die konfigurierte Frist in einem Zustand, entsteht ein HubSpot-Task. Der Task-Schlüssel enthält Zustand und Eintrittszeitpunkt: je Aufenthalt in einem Zustand ein Task; wer den Zustand später erneut erreicht (z. B. nach Resume), bekommt einen neuen.

  | Zustand | Frist (Default) | Grund |
  |---|---|---|
  | NetzanmeldungSubmitted | 30 Tage | Netzbetreiber muss bei ≤ 30 kW innerhalb eines Monats antworten |
  | RueckfrageOpen | 5 Tage | |
  | …AwaitingReview | 2 Tage | Freigabe vergessen |
  | EinspeisezusageReceived | 180 Tage | Inbetriebnahmedatum fehlt in HubSpot |
  | ZaehlerwechselRequested | 42 Tage | |
  | ZaehlerwechselCompleted | 7 Tage | MaStR-Login fehlt; Registrierungsfrist 1 Monat nach Inbetriebnahme |
  | Blocked | 1 Tag | Erinnerung zusätzlich zu Sofort-Task und Alarm-Mail beim Blockieren |

- **Hängengeblieben:** Steht ein Vorgang länger als eine Stunde in einem Zustand, den ein Schritt sofort weiterführen könnte (z. B. `Created`, `InbetriebnahmeReady`), wird er erneut eingeplant. Das fängt verlorene Jobs ab. Dasselbe gilt für einen angeforderten, aber noch nicht umgesetzten Storno (auch aus `Blocked`). „Vorbereitet“ zählt dazu, aber nur außerhalb von DryRun (siehe Einreichungsstufen).
- **Inbetriebnahmedatum erreicht:** Vorgänge in `EinspeisezusageReceived`, deren Inbetriebnahmedatum heute oder früher ist, werden neu eingeplant.

## Blockade

Nach dem letzten fehlgeschlagenen Retry blockiert `ProcessFailureHandler` den Vorgang in einem frischen DI-Scope (der DbContext des gescheiterten Laufs kann unbrauchbar sein). Der Blockadegrund geht nach HubSpot und in die Alarm-Mail und enthält deshalb nur sichere Texte: Meldungen eigener Exceptions (`AutomationException`, `SubmissionConflictException`, `InvalidProcessTransitionException`), bei `HttpRequestException` nur unser Format „… HTTP {code}“ bzw. den Statuscode, bei Timeouts einen festen Text, sonst nur den Typnamen. Die volle Exception steht nur im technischen Log. Task-Schlüssel `blocked:…` enthält den Zeitpunkt der Blockade, eine erneute Blockade nach Resume erzeugt einen neuen Task.

## HubSpot-Spiegel

Nach jedem Zustandswechsel schreibt `ICrmStatusSink` den Stand an das HubSpot-Projekt: Status, Netzbetreiber, Blockadegrund, MaStR-Nummern. Das ist die einzige UI. **Aktuell nur ein Log-Platzhalter. Bis Martin es freigibt, wird nicht nach HubSpot geschrieben.**

## Offene Punkte

- **Reihenfolge Zählerwechsel ↔ MaStR.** Laut Konzept kommt MaStR nach dem Zählerwechsel. Die MaStR-Frist (1 Monat ab Inbetriebnahme) kann aber ablaufen, wenn der Zählerwechsel lange dauert. Der Watchdog warnt. Mit Michael klären, ob MaStR direkt nach der Inbetriebnahme laufen soll.
- **Woher kommen Einspeisezusage, Rückfrage und Zählerwechsel?** Bis das Postfach (Graph + KI) angebunden ist, über HubSpot-Properties, die Michael setzt, oder über den Admin-Endpunkt.
- **Vorgaben aus der Einspeisezusage für das E.8** (Blindleistungsverfahren, TF-Sperren) sind noch nicht modelliert.
- **Anlagen ≥ 25 kW**: weiterhin Task „nicht automatisiert“.
