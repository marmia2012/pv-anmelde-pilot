---
name: develop-feature
description: Entwickelt ein Feature im PV-Autopilot von der Klärung bis zum Pull Request – Fragen bis alles klar ist, Plan mit Freigabe, eigener Branch, Subagents für Entwicklung, Tests, Testlauf und Review, Mutationsprüfung gegen hohle Tests, PR auf main. Nutzen bei „/develop-feature“, „neues Feature bauen“, „Feature entwickeln“, „lass uns X umsetzen“.
---

# /develop-feature

Führt ein Feature in festen Phasen durch. Keine Phase überspringen. Code wird erst geändert, wenn der Plan Martins ausdrückliches Go hat.

Kommunikation mit Martin auf Deutsch. Code und Identifier auf Englisch, Fachbegriffe deutsch (siehe CLAUDE.md). Commit-Messages und PR-Text auf Deutsch.

## Grundregeln für jede Phase

- **Konzepte und Beschlüsse sind verbindlich.** Vor dem Plan und vor jeder Umsetzung lesen: `CLAUDE.md`, die passenden Dateien in `docs/context/` (immer `02-architektur.md` und `05-status-offene-fragen.md`), die Meeting-Protokolle in `docs/context/meetings/`, die betroffenen Dokumente in `docs/konzept/` und `docs/konfiguration.md`. Widerspricht ein Wunsch einem Beschluss, Martin darauf hinweisen und entscheiden lassen. Nie stillschweigend abweichen. Ändert Martin einen Beschluss, die Änderung im selben Feature in der Doku nachziehen.
- **Harte Regeln aus CLAUDE.md** gelten immer: keine personenbezogenen Daten in Logs, keine Secrets im Repo, Jobs idempotent, nie doppelt einreichen, `DryRun` als Default, neue Konfiguration in `docs/konfiguration.md` (und ggf. `.env.example`) dokumentieren.
- **Memory beachten**, insbesondere Einschränkungen für echte Systeme (z. B. Reonic/HubSpot nur lesend). Diese Einschränkungen jedem Subagent im Prompt mitgeben.
- **Subagents** bekommen einen vollständigen, eigenständigen Auftrag: Plan (Pfad), betroffene Dateien, relevante Konzepte, harte Regeln, Memory-Einschränkungen, erwartetes Ergebnis. Ihr Ergebnis wird geprüft, nicht blind übernommen.

## Phase 0 – Einstieg

1. Arbeitsverzeichnis prüfen (`git status`). Gibt es uncommittete Änderungen, Martin fragen, ob sie vorher committet, gestasht oder ins Feature übernommen werden. Nichts verwerfen.
2. Mit `AskUserQuestion` fragen: **Gibt es schon einen Plan, den wir nutzen?**
   - „Ja, im Gespräch“ → den Plan aus dem Gespräch übernehmen und in Phase 2 als Datei ablegen.
   - „Ja, als Datei“ → Pfad erfragen, lesen, offene Punkte in Phase 1 klären.
   - „Nein, neu erstellen“ → Phase 1.

## Phase 1 – Klären

- Vorher die Grundregeln-Dokumente und den betroffenen Code lesen, damit nur Fragen kommen, die Code und Doku nicht beantworten.
- Fragen mit `AskUserQuestion` stellen, höchstens 4 je Runde, je Frage 2–4 Optionen, empfohlene Option zuerst mit „(Recommended)“. Jede Frage muss für sich verständlich sein.
- So lange fragen, bis klar ist: Ziel und Umfang, Datenquellen/Zielsysteme, fachliche Regeln und Defaults, Fehlerfälle (fachlich → HubSpot-Task, technisch → Retry/blockiert), Konfiguration, was ausdrücklich nicht dazugehört, wie getestet wird.
- Antworten genau lesen: Freitext-Antworten können Optionen verwerfen oder neue Anforderungen enthalten.

## Phase 2 – Plan

1. Plan als Datei ablegen: `docs/plans/<yyyy-mm-dd>-<slug>.md`. Inhalt:
   - **Ziel** (1–2 Sätze) und **Festgelegt** (alle Entscheidungen aus Phase 1)
   - **Bezug zu Konzepten/Beschlüssen** (welche Dokumente, wo bewusst abgewichen wird)
   - **Schritte** in Umsetzungsreihenfolge, je Schritt: Dateien, Verhalten, Logging, Konfiguration
   - **Tests** je Schritt: welches Verhalten abgesichert wird
   - **Doku-Änderungen**
   - **Annahmen und offene Punkte**
2. Plan im Chat kurz zusammenfassen und mit `AskUserQuestion` um **Go** bitten (Optionen: „Go“, „Anpassen“). Bei „Anpassen“ zurück in Phase 1 bzw. Plan überarbeiten.
3. Ohne ausdrückliches Go keine Code-Änderung.

## Phase 3 – Branch

1. `main` aktualisieren (`git fetch`, `git switch main`, `git pull --ff-only`).
2. Neuen Branch mit sprechendem Namen anlegen: `feature/<kurzer-englischer-slug>` (z. B. `feature/hubspot-deal-adapter`). Bei Fehlerbehebung `fix/<slug>`.
3. Plan-Datei als ersten Commit auf dem Branch: `Plan: <Feature>`.

## Phase 4 – Entwicklung (Subagent „Entwicklung“)

Auftrag an einen `general-purpose`-Subagent, je Planschritt oder für zusammenhängende Schritte:

- Sich an bestehende Strukturen halten: vorhandene Projekte, Namespaces, Muster (Adapter hinter Interfaces in Core, Optionen-Klassen mit `SectionName`, `IValidateOptions`/`ValidateOnStart`, DI-Erweiterung `Add<Name>()`, Fehlerklassen fachlich vs. technisch). Vergleichbaren vorhandenen Code als Vorlage nennen (z. B. Reonic-Adapter für neue Datenquellen).
- **Log-Messages einbauen** mit Serilog-/`ILogger`-Templates (strukturierte Platzhalter, keine String-Interpolation): Start/Ende wichtiger Operationen auf `Information`, Details auf `Debug`, übersprungene oder unvollständige Fälle auf `Warning`, Fehler auf `Error`. Nur IDs, Zustände, Feld- und Produktnamen loggen, **nie** Namen, Adressen, IBAN, E-Mail, Telefon, Zählernummern, Freitexte, Tokens oder Antwort-Bodies. Personenbezogene Properties mit `[PersonalData]` markieren.
- Sich strikt an den Plan halten. Muss er abweichen, stoppen und begründen; Martin entscheidet.
- Konfiguration und Doku im selben Schritt nachziehen.
- Ergebnis: Liste geänderter Dateien und kurze Beschreibung je Planschritt.

Danach selbst prüfen: `dotnet build` ohne neue Warnungen, Diff gegen den Plan abgleichen.

## Phase 5 – Tests schreiben (Subagent „Tests“)

Eigener `general-purpose`-Subagent, nicht derselbe wie die Entwicklung:

- Tests in `tests/PvAutopilot.Tests/` passend zur bestehenden Struktur (Ordner je Bereich, vorhandene Fakes/Fixtures wiederverwenden, z. B. `CoreFakes.cs`, Reonic-Fixtures).
- Verhalten testen, nicht Implementierung: je Planschritt die fachlichen Regeln, Defaults, Fehlerfälle, Idempotenz und dass keine personenbezogenen Daten geloggt werden, wo relevant.
- Externe Systeme nur über simulierte HTTP-Antworten bzw. Fakes, nie gegen echte Systeme.

## Phase 6 – Tests ausführen (Subagent „Testlauf“)

Eigener Subagent führt `dotnet test` aus und berichtet: Anzahl grün/rot, je Fehler Test, Meldung und vermutete Ursache (Code oder Test). Fehler beheben (Entwicklungs- bzw. Test-Subagent) und erneut laufen lassen, bis alles grün ist.

Sobald alles grün ist: **Commit** mit beschreibender Message (was das Feature kann, welche Planschritte, Testanzahl).

## Phase 7 – Mutationsprüfung gegen hohle Tests

Ziel: Jeder neue Test schlägt fehl, wenn das Verhalten, das er absichert, kaputt ist.

1. Für jedes abgesicherte Verhalten eine gezielte Mutation im **Produktivcode** (`src/`) einbauen, z. B. Bedingung umdrehen, Default ändern, Fallback entfernen, Aufruf weglassen, Idempotenzprüfung entfernen, Log-Maskierung entfernen. Liste führen: Mutation → Test, der fehlschlagen muss.
2. Tests ausführen (Subagent „Testlauf“). Für jede Mutation prüfen, ob der erwartete Test fehlschlägt.
3. Bleibt ein Test grün, ist er hohl: den **Test** korrigieren (Subagent „Tests“), nie die Mutation abschwächen. Wiederholen, bis jede Mutation erkannt wird.
4. Alle Mutationen zurücknehmen: nur die Dateien unter `src/` auf den Stand des letzten Commits setzen (`git restore --source=HEAD -- <dateien>`), Test-Änderungen behalten. Mit `git diff --stat` prüfen, dass nur noch Dateien unter `tests/` geändert sind.
5. Tests ausführen: Alles muss wieder grün sein.
6. Hatte es Testkorrekturen gegeben: **Commit** `Tests: Mutationsprüfung, hohle Tests korrigiert` mit Liste der korrigierten Tests. Sonst kurz an Martin melden, dass alle Mutationen erkannt wurden.

## Phase 8 – Code-Review (Subagent „Review“)

Eigener Subagent prüft den gesamten Branch-Diff gegen `main`:

- Einhaltung von Plan, Konzepten und Beschlüssen
- harte Regeln (PII in Logs, Secrets, Idempotenz, Doppelversand, DryRun-Default)
- bestehende Strukturen, Fehlerbehandlung fachlich/technisch, Log-Messages vorhanden und sinnvoll
- Doku: `docs/konfiguration.md`, `.env.example`, `instance.json`, Konzeptdokumente
- Korrektheitsfehler und Randfälle

Befunde nach Schwere sortiert. Berechtigte Befunde beheben, danach Phase 6 (Tests grün) und für neu getestetes Verhalten Phase 7 erneut. Abgelehnte Befunde mit Begründung im PR erwähnen. Commit `Review-Befunde behoben`.

## Phase 9 – Pull Request

1. Prüfen: Tests grün, Arbeitsverzeichnis sauber, Doku aktualisiert, `docs/context/05-status-offene-fragen.md` bei Bedarf nachgezogen.
2. Branch pushen (`git push -u origin <branch>`).
3. PR auf `main` mit `gh pr create --base main` erstellen. PR-Text auf Deutsch:
   - **Was** (Ziel, Link zur Plan-Datei)
   - **Änderungen** je Planschritt
   - **Tests** (Anzahl, Mutationsprüfung: wie viele Mutationen, alle erkannt)
   - **Review** (behobene und bewusst nicht behobene Befunde)
   - **Konfiguration** (neue Schlüssel, was vor dem Deploy gesetzt werden muss)
   - **Offene Punkte**
   - am Ende die Attributionszeile aus der aktuellen System-Erinnerung
4. Martin den PR-Link geben und in 2–3 Sätzen sagen, was fertig ist und was offen bleibt. Nicht selbst mergen.
