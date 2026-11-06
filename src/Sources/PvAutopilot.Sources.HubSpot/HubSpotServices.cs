using Microsoft.Extensions.Logging;
using PvAutopilot.Core.Mastr;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Sources;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Sources.HubSpot;

public sealed class HubSpotCrmProjectSource(HttpClient http) : ICrmProjectSource
{
    public Task<CrmProjectData> GetProjectAsync(string crmProjectId, CancellationToken ct)
    {
        _ = http;
        // TODO: Projekt + verknüpften Deal und Kontakt lesen; Property-Namen aus HubSpotOptions.Properties.
        throw new NotImplementedException("HubSpot-Mapping folgt, sobald die Projektstruktur festgelegt ist.");
    }
}

// Die folgenden Sinks loggen nur, bis die HubSpot-Anbindung steht. Sie werfen bewusst nicht,
// damit ein DryRun die Strecke durchlaufen kann.

public sealed class HubSpotTaskSink(HttpClient http, ILogger<HubSpotTaskSink> logger) : IHumanTaskSink
{
    public Task CreateTaskAsync(string crmProjectId, string taskKey, string title, string body, CancellationToken ct)
    {
        _ = http;
        // TODO: Task am Projekt anlegen, vorher per Suche nach taskKey Duplikate vermeiden.
        logger.LogWarning("HubSpot-Task noch nicht angebunden: {TaskKey} – {TaskTitle}", taskKey, title);
        return Task.CompletedTask;
    }
}

public sealed class HubSpotAuditSink(HttpClient http, ILogger<HubSpotAuditSink> logger) : IAuditSink
{
    public Task WriteAsync(AnmeldeProcess process, AuditEvent evt, CancellationToken ct)
    {
        _ = http;
        // TODO: Notiz bzw. Timeline-Event am Projekt.
        logger.LogDebug("HubSpot-Audit noch nicht angebunden: {AuditType}", evt.Type);
        return Task.CompletedTask;
    }
}

public sealed class HubSpotDocumentArchive(HttpClient http, ILogger<HubSpotDocumentArchive> logger) : IDocumentArchive
{
    public Task StoreAsync(string crmProjectId, IReadOnlyList<GeneratedDocument> documents, CancellationToken ct)
    {
        _ = http;
        // TODO: Dateien hochladen (Files API) und mit dem Projekt verknüpfen.
        logger.LogWarning("HubSpot-Ablage noch nicht angebunden: {DocumentCount} Dokument(e)", documents.Count);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Spiegelt den Stand des Vorgangs ans HubSpot-Projekt. Bis Martin Schreibzugriffe freigibt, nur ein Log.
/// </summary>
public sealed class HubSpotStatusSink(HttpClient http, ILogger<HubSpotStatusSink> logger) : ICrmStatusSink
{
    public Task PublishAsync(AnmeldeProcess process, CancellationToken ct)
    {
        _ = http;
        // TODO (erst nach Freigabe der Schreibrechte): PATCH crm/v3/objects/{ProjectObjectTypeId}/{CrmProjectId}
        // mit den Property-Namen aus HubSpotOptions.Properties; leerer Name = Feld nicht schreiben:
        //   Status              → process.State (bzw. lesbare Bezeichnung)
        //   Netzbetreiber       → process.NetzbetreiberCode
        //   Fehlertext          → process.BlockReason (bei Blocked, sonst leeren)
        //   MastrNummerAnlage   → process.MastrNummerAnlage
        //   MastrNummerSpeicher → process.MastrNummerSpeicher
        // Idempotent, da nur der aktuelle Stand geschrieben wird.
        logger.LogInformation("HubSpot-Status noch nicht angebunden: Vorgang {ProcessId} in {State}", process.Id, process.State);
        return Task.CompletedTask;
    }
}

public sealed class HubSpotMastrCredentialStore(HttpClient http) : IMastrCredentialStore
{
    public Task<MastrCredentials?> GetAsync(string crmProjectId, CancellationToken ct)
    {
        _ = http;
        throw new NotImplementedException("Herkunft des MaStR-Logins (Webformular → HubSpot) noch offen.");
    }

    public Task DeleteAsync(string crmProjectId, CancellationToken ct) =>
        throw new NotImplementedException("Herkunft des MaStR-Logins (Webformular → HubSpot) noch offen.");
}
