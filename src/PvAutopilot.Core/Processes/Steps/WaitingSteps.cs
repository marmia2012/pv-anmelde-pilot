namespace PvAutopilot.Core.Processes.Steps;

/// <summary>
/// Wartet nach der Netzanmeldung auf Einspeisezusage oder Rückfrage und setzt die Erledigung einer Rückfrage um.
/// Die Zusage gewinnt immer, auch wenn gerade eine Rückfrage offen ist.
/// </summary>
public sealed class AwaitNetzbetreiberResponseStep(TimeProvider time) : IProcessStep
{
    public bool CanHandle(ProcessState state) => state is ProcessState.NetzanmeldungSubmitted or ProcessState.RueckfrageOpen;

    public Task<StepResult> ExecuteAsync(AnmeldeProcess process, CancellationToken ct) => Task.FromResult(Execute(process));

    private StepResult Execute(AnmeldeProcess process)
    {
        var now = time.GetUtcNow();

        if (process.EinspeisezusageDatum is not null)
        {
            if (process.RueckfrageOffenSeit is not null)
                process.CloseRueckfrage(now);
            process.TransitionTo(ProcessState.EinspeisezusageReceived, now);
            return StepResult.Continue;
        }

        if (process.State == ProcessState.NetzanmeldungSubmitted)
        {
            if (process.RueckfrageOffenSeit is not { } offenSeit)
                return StepResult.Wait("wartet auf Einspeisezusage oder Rückfrage");

            process.TransitionTo(ProcessState.RueckfrageOpen, now);
            // Schon erledigt, bevor dieser Lauf kam: direkt weiter, kein Task nötig.
            if (process.RueckfrageErledigung is not null)
                return StepResult.Continue;

            // Zeitpunkt im Schlüssel: eine zweite Rückfrage (nach direkter Antwort) bekommt einen neuen Task.
            return StepResult.HumanAction(
                $"rueckfrage:{process.Id:N}:r{process.Revision}:{offenSeit.UtcDateTime:yyyyMMddHHmmss}",
                "Rückfrage des Netzbetreibers bearbeiten",
                "Der Netzbetreiber hat eine Rückfrage zur Netzanmeldung gestellt. Bitte bearbeiten und danach im Projekt "
                + "angeben, ob sie direkt beantwortet wurde oder der Antrag neu eingereicht werden soll. Steht die "
                + "Erledigungs-Property noch von einer früheren Rückfrage auf diesem Wert, bitte erst zurücksetzen und dann "
                + "neu setzen – nur eine Änderung löst die Erledigung aus.");
        }

        switch (process.RueckfrageErledigung)
        {
            case RueckfrageResolution.AnsweredDirectly:
                process.CloseRueckfrage(now);
                process.TransitionTo(ProcessState.NetzanmeldungSubmitted, now, "Rückfrage direkt beantwortet");
                return StepResult.Continue;
            case RueckfrageResolution.Resubmit:
                process.StartNewRevision(now);
                return StepResult.Continue;
            default:
                return StepResult.Wait("Rückfrage offen");
        }
    }
}

/// <summary>
/// Zusage liegt vor; sobald das Inbetriebnahmedatum bekannt (auch wenn es vorher kam) und erreicht ist, geht es zum E.8.
/// Ein vorab gemeldetes Datum in der Zukunft holt der Watchdog an diesem Tag ab.
/// </summary>
public sealed class AwaitInbetriebnahmeStep(TimeProvider time) : IProcessStep
{
    public bool CanHandle(ProcessState state) => state == ProcessState.EinspeisezusageReceived;

    public Task<StepResult> ExecuteAsync(AnmeldeProcess process, CancellationToken ct)
    {
        if (process.Inbetriebnahmedatum is not { } datum)
            return Task.FromResult(StepResult.Wait("wartet auf Inbetriebnahmedatum"));
        // Das E.8 meldet eine erfolgte Inbetriebnahme; vor dem Datum wäre es inhaltlich falsch.
        if (datum > ProcessCalendar.Today(time))
            return Task.FromResult(StepResult.Wait($"Inbetriebnahme erst am {datum:yyyy-MM-dd}"));

        process.TransitionTo(ProcessState.InbetriebnahmeReady, time.GetUtcNow());
        return Task.FromResult(StepResult.Continue);
    }
}

public sealed class AwaitZaehlerwechselStep(TimeProvider time) : IProcessStep
{
    public bool CanHandle(ProcessState state) => state == ProcessState.ZaehlerwechselRequested;

    public Task<StepResult> ExecuteAsync(AnmeldeProcess process, CancellationToken ct)
    {
        if (process.ZaehlerwechselDatum is null)
            return Task.FromResult(StepResult.Wait("wartet auf Zählerwechsel"));

        process.TransitionTo(ProcessState.ZaehlerwechselCompleted, time.GetUtcNow());
        return Task.FromResult(StepResult.Continue);
    }
}

public sealed class CompleteStep(TimeProvider time) : IProcessStep
{
    public bool CanHandle(ProcessState state) => state == ProcessState.MastrRegistered;

    public Task<StepResult> ExecuteAsync(AnmeldeProcess process, CancellationToken ct)
    {
        process.TransitionTo(ProcessState.Completed, time.GetUtcNow());
        return Task.FromResult(StepResult.Continue);
    }
}
