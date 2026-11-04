using Microsoft.Extensions.Logging;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;

namespace PvAutopilot.Infrastructure.Notifications;

/// <summary>Technischer Teil des Audit-Logs (Serilog).</summary>
public sealed class SerilogAuditSink(ILogger<SerilogAuditSink> logger) : IAuditSink
{
    public Task WriteAsync(AnmeldeProcess process, AuditEvent evt, CancellationToken ct)
    {
        logger.LogInformation("Audit {AuditType} für Vorgang {ProcessId}: {AuditMessage}", evt.Type, process.Id, evt.Message);
        return Task.CompletedTask;
    }
}

/// <summary>Platzhalter, bis der Mailversand über Microsoft Graph steht.</summary>
public sealed class LoggingOperatorAlert(ILogger<LoggingOperatorAlert> logger) : IOperatorAlert
{
    public Task SendAsync(string subject, string body, byte[]? screenshot, CancellationToken ct)
    {
        // TODO: Mail an den Betreiber über Microsoft Graph, Screenshot als Anhang.
        logger.LogError("Alarm: {AlertSubject} (Screenshot: {HasScreenshot})", subject, screenshot is not null);
        return Task.CompletedTask;
    }
}
