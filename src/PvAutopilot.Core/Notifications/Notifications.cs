using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core.Notifications;

/// <summary>Fachliche Aufgaben für Menschen (Instanz reinsolar: HubSpot-Aufgabe am Deal).</summary>
public interface IHumanTaskSink
{
    /// <param name="taskKey">Stabiler Schlüssel; dieselbe Aufgabe wird nicht doppelt angelegt.</param>
    Task CreateTaskAsync(string crmProjectId, string taskKey, string title, string body, CancellationToken ct);
}

/// <summary>Technische Alarme an den Betreiber (Mail an Martin, ggf. mit Screenshot).</summary>
public interface IOperatorAlert
{
    Task SendAsync(string subject, string body, byte[]? screenshot, CancellationToken ct);
}

/// <summary>Ablage erzeugter Dokumente am CRM-Objekt (Instanz reinsolar: HubSpot-Dateien + Notiz am Deal). Muss idempotent sein.</summary>
public interface IDocumentArchive
{
    Task StoreAsync(string crmProjectId, IReadOnlyList<GeneratedDocument> documents, CancellationToken ct);
}

/// <summary>
/// Spiegelt den Stand eines Vorgangs ins CRM (Instanz reinsolar: Properties am HubSpot-Projekt):
/// Status, Netzbetreiber, Blockadegrund, MaStR-Nummern. Wird nach jedem Zustandswechsel aufgerufen und muss idempotent sein.
/// </summary>
public interface ICrmStatusSink
{
    Task PublishAsync(AnmeldeProcess process, CancellationToken ct);
}

/// <summary>Ein Eintrag im Audit-Log. <see cref="Level"/> entscheidet, ob ein Ziel ihn übernimmt (Serilog: immer).</summary>
public sealed record AuditEvent(string Type, string Message, AuditLevel Level);

/// <summary>
/// Stufe eines Audit-Ereignisses bzw. Filter eines Audit-Ziels: <see cref="Verbose"/> = alles,
/// <see cref="Info"/> = wichtige Ereignisse und Fehler, <see cref="Error"/> = nur Fehler.
/// </summary>
public enum AuditLevel { Verbose, Info, Error }

/// <summary>Stufen der Audit-Ereignisse aus der Prozesssteuerung.</summary>
public static class AuditLevels
{
    /// <summary>
    /// Stufe eines Zustandswechsels nach <paramref name="to"/>: Blockade = <see cref="AuditLevel.Error"/>;
    /// interne Zwischenschritte (Datenkarte, Netzbetreiber ermittelt, neue Revision) = <see cref="AuditLevel.Verbose"/>;
    /// alle übrigen (Einreichung vorbereitet/eingereicht, Freigabe wartet, Zusage, Rückfrage, Inbetriebnahme,
    /// Zählerwechsel, MaStR registriert, Abschluss, Storno) = <see cref="AuditLevel.Info"/>.
    /// </summary>
    public static AuditLevel ForStateChange(ProcessState to) => to switch
    {
        ProcessState.Blocked => AuditLevel.Error,
        ProcessState.Created or ProcessState.DataCardAggregated or ProcessState.NetzbetreiberResolved => AuditLevel.Verbose,
        _ => AuditLevel.Info,
    };
}

/// <summary>Ein Ziel des Audit-Logs. Nachrichten dürfen keine personenbezogenen Daten enthalten.</summary>
public interface IAuditSink
{
    Task WriteAsync(AnmeldeProcess process, AuditEvent evt, CancellationToken ct);
}

/// <summary>Schreibt jedes Audit-Ereignis in alle registrierten Sinks (HubSpot-Timeline + Serilog).</summary>
public sealed class AuditLog(IEnumerable<IAuditSink> sinks)
{
    public async Task RecordAsync(AnmeldeProcess process, AuditEvent evt, CancellationToken ct)
    {
        foreach (var sink in sinks)
            await sink.WriteAsync(process, evt, ct);
    }
}

/// <summary>Fehler aus Browser-Automatisierung; der Screenshot geht mit in die Alarm-Mail.</summary>
public class AutomationException(string message, byte[]? screenshot = null, Exception? inner = null)
    : Exception(message, inner)
{
    public byte[]? Screenshot { get; } = screenshot;
}
