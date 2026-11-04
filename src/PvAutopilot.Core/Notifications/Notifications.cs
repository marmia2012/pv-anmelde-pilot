using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core.Notifications;

/// <summary>Fachliche Aufgaben für Menschen (Instanz reinsolar: HubSpot-Task am Projekt).</summary>
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

/// <summary>Ablage erzeugter Dokumente am CRM-Projekt (Instanz reinsolar: HubSpot).</summary>
public interface IDocumentArchive
{
    Task StoreAsync(string crmProjectId, IReadOnlyList<GeneratedDocument> documents, CancellationToken ct);
}

public sealed record AuditEvent(string Type, string Message);

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
