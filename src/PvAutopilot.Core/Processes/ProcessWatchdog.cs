using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core.Processes;

public sealed class ProcessWatchdogOptions
{
    public const string SectionName = "Watchdog";

    /// <summary>Cron-Ausdruck des Hangfire-Jobs (Default: werktags 07:00 UTC).</summary>
    public string Schedule { get; set; } = "0 7 * * 1-5";

    /// <summary>Nach dieser Zeit in einem aktiven Zustand (<see cref="ProcessStates.IsActive"/>) wird der Vorgang neu eingeplant.</summary>
    public TimeSpan StalledAfter { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Frist je Wartezustand; danach entsteht ein HubSpot-Task. Fehlender Eintrag = keine Frist.</summary>
    public Dictionary<ProcessState, TimeSpan> MaxWait { get; set; } = new()
    {
        [ProcessState.NetzanmeldungSubmitted] = TimeSpan.FromDays(30),
        [ProcessState.RueckfrageOpen] = TimeSpan.FromDays(5),
        [ProcessState.NetzanmeldungAwaitingReview] = TimeSpan.FromDays(2),
        [ProcessState.InbetriebnahmeAwaitingReview] = TimeSpan.FromDays(2),
        [ProcessState.ZaehlerwechselAwaitingReview] = TimeSpan.FromDays(2),
        [ProcessState.MastrAwaitingReview] = TimeSpan.FromDays(2),
        [ProcessState.EinspeisezusageReceived] = TimeSpan.FromDays(180),
        [ProcessState.ZaehlerwechselRequested] = TimeSpan.FromDays(42),
        [ProcessState.ZaehlerwechselCompleted] = TimeSpan.FromDays(7),
        // Erinnerung zusätzlich zum Sofort-Task und Alarm beim Blockieren – blockierte Vorgänge geraten sonst in Vergessenheit.
        [ProcessState.Blocked] = TimeSpan.FromDays(1),
    };
}

public sealed record WatchdogReport(int Checked, int Rescheduled, int Overdue);

/// <summary>Täglicher Lauf: Fristen-Tasks für überfällige Wartezustände, Neueinplanung hängengebliebener Vorgänge.</summary>
public sealed class ProcessWatchdog(
    IProcessStore store,
    IProcessScheduler scheduler,
    IHumanTaskSink tasks,
    IOptions<ProcessWatchdogOptions> options,
    IOptions<SubmissionOptions> submission,
    TimeProvider time,
    ILogger<ProcessWatchdog> logger)
{
    public async Task<WatchdogReport> RunAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var settings = options.Value;
        var today = ProcessCalendar.Today(time);
        var processes = await store.ListOpenAsync(ct);
        int rescheduled = 0, overdue = 0;

        foreach (var process in processes)
        {
            if (process.State.IsTerminal())
                continue;

            var inState = now - process.StateChangedAt;

            // Ein Storno wird erst im nächsten Lauf umgesetzt; ging der Job verloren, holen wir ihn hier nach.
            var pendingCancellation = process.CancellationRequestedAt is not null;
            if ((IsStalled(process.State) || pendingCancellation) && inState > settings.StalledAfter)
            {
                logger.LogWarning("Vorgang {ProcessId} hängt seit {Duration} in {State}; neu eingeplant", process.Id, inState, process.State);
                scheduler.Enqueue(process.Id);
                rescheduled++;
                continue;
            }

            // Das Inbetriebnahmedatum kann vorab gemeldet werden; erreicht ist es erst an diesem Tag, ohne Ereignis.
            if (process.State == ProcessState.EinspeisezusageReceived && process.Inbetriebnahmedatum <= today)
            {
                logger.LogInformation("Vorgang {ProcessId}: Inbetriebnahmedatum erreicht; neu eingeplant", process.Id);
                scheduler.Enqueue(process.Id);
                rescheduled++;
                continue;
            }

            if (settings.MaxWait.TryGetValue(process.State, out var maxWait) && inState > maxWait)
            {
                logger.LogInformation("Vorgang {ProcessId} überfällig in {State} seit {Duration}", process.Id, process.State, inState);
                // Eintritt in den Zustand im Schlüssel: wer den Zustand später erneut erreicht, bekommt einen neuen Task.
                await tasks.CreateTaskAsync(process.CrmProjectId,
                    $"overdue:{process.Id:N}:{process.State}:{TaskKeys.Stamp(process.StateChangedAt)}",
                    $"Anmeldung: Frist überschritten ({process.State})", OverdueHint(process.State, inState), ct);
                overdue++;
            }
        }

        logger.LogInformation("Watchdog: {Checked} Vorgänge geprüft, {Rescheduled} neu eingeplant, {Overdue} überfällig",
            processes.Count, rescheduled, overdue);
        return new WatchdogReport(processes.Count, rescheduled, overdue);
    }

    /// <summary>
    /// Ein aktiver Zustand gilt als hängengeblieben. Ausnahme „Vorbereitet“ im DryRun: dort endet der Vorgang
    /// planmäßig, eine stündliche Neueinplanung wäre nur Rauschen. Nach dem Wechsel auf Review/Live läuft er weiter.
    /// </summary>
    private bool IsStalled(ProcessState state) =>
        state.IsActive() && !(state.IsPrepared() && submission.Value.Mode == SubmissionMode.DryRun);

    private static string OverdueHint(ProcessState state, TimeSpan inState)
    {
        var hint = state switch
        {
            ProcessState.NetzanmeldungSubmitted => "Der Netzbetreiber hat noch nicht geantwortet. Bitte nachfassen.",
            ProcessState.RueckfrageOpen => "Die Rückfrage des Netzbetreibers ist noch offen.",
            ProcessState.NetzanmeldungAwaitingReview or ProcessState.InbetriebnahmeAwaitingReview
                or ProcessState.ZaehlerwechselAwaitingReview or ProcessState.MastrAwaitingReview =>
                "Die Einreichung wartet auf Freigabe.",
            ProcessState.EinspeisezusageReceived => "Das Inbetriebnahmedatum fehlt im Projekt.",
            ProcessState.ZaehlerwechselRequested => "Der Zählerwechsel ist noch nicht erfolgt. Bitte beim Netzbetreiber nachfassen.",
            ProcessState.ZaehlerwechselCompleted =>
                "Die MaStR-Registrierung steht aus (meist fehlt der Kundenlogin). Frist: 1 Monat nach Inbetriebnahme.",
            ProcessState.Blocked => "Der Vorgang ist blockiert und muss geprüft und fortgesetzt werden.",
            _ => "Der Vorgang wartet ungewöhnlich lange.",
        };
        return $"{hint} Seit {(int)inState.TotalDays} Tagen in diesem Zustand.";
    }
}
