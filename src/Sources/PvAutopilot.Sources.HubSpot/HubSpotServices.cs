using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Mastr;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;
using PvAutopilot.Sources.HubSpot.Api;

namespace PvAutopilot.Sources.HubSpot;

// Lesen: HubSpotCrmProjectSource. Schreiben: die Sinks unten, nur bei HubSpot:WriteEnabled = true; sonst wird
// nur auf Debug geloggt und nichts geworfen, damit ein DryRun die Strecke durchlaufen kann.
// Logs enthalten nur Typ, Deal-ID und Schlüssel – nie Inhalte, Dateinamen, Property-Werte oder Response-Bodies.

/// <summary>
/// Legt ein CRM-Objekt höchstens einmal je Schlüssel an (<see cref="ICrmWriteLedger"/>).
/// </summary>
/// <remarks>
/// Reihenfolge: Ledger prüfen → in HubSpot anlegen → sofort im Ledger festhalten (ohne Abbruch-Token, damit ein
/// Abbruch des Jobs nicht genau dazwischen fällt). Verbleibende Lücke: stürzt der Prozess zwischen dem erfolgreichen
/// HubSpot-Request und dem Ledger-Eintrag ab (oder ist die Datenbank genau dann weg), legt der Retry das Objekt ein
/// zweites Mal an. Ebenso, wenn HubSpot das Objekt anlegt, aber mit 5xx antwortet und der Backoff-Handler den POST
/// wiederholt. Eine Dublette ist dort das kleinere Übel gegenüber einer verlorenen Aufgabe (docs/konzept/hubspot-adapter.md).
/// </remarks>
public sealed class HubSpotWriteOnce(ICrmWriteLedger ledger, ILogger<HubSpotWriteOnce> logger)
{
    public async Task<string> RunAsync(string writeType, string dealId, string key, Func<CancellationToken, Task<string>> write, CancellationToken ct)
    {
        if (await ledger.FindAsync(key, ct) is { } existing)
        {
            logger.LogDebug("HubSpot-{WriteType} für Deal {DealId} übersprungen: {WriteKey} schon angelegt", writeType, dealId, key);
            return existing.ExternalId;
        }

        var externalId = await write(ct);
        if (!await ledger.RecordAsync(key, externalId, CancellationToken.None))
            logger.LogWarning("HubSpot-{WriteType} für Deal {DealId}: {WriteKey} wurde parallel ein zweites Mal angelegt", writeType, dealId, key);

        logger.LogInformation("HubSpot-{WriteType} für Deal {DealId} angelegt: {WriteKey}", writeType, dealId, key);
        return externalId;
    }

    /// <summary>Kurzer SHA-256-Hash (16 Hex-Zeichen) für Schlüsselbestandteile, die selbst nicht in Schlüssel und Log gehören.</summary>
    public static string ShortHash(string value) => Sha256(Encoding.UTF8.GetBytes(value))[..16];

    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
}

/// <summary>Text für HubSpot-Rich-Text-Felder (Notiz, Aufgabe): HTML-kodiert, Zeilenumbrüche als <c>&lt;br&gt;</c>.</summary>
internal static class HubSpotText
{
    public static string Html(string text) =>
        WebUtility.HtmlEncode(text).ReplaceLineEndings("<br>");

    public static string Timestamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}

/// <summary>
/// Fachliche Aufgabe am Deal: zugewiesen an den Deal-Inhaber (<c>hubspot_owner_id</c>), sonst an
/// <see cref="HubSpotOptions.DefaultTaskOwnerId"/>, ohne beide unzugewiesen. Je <c>taskKey</c> genau eine Aufgabe.
/// </summary>
public sealed class HubSpotTaskSink(
    HubSpotApiClient api,
    HubSpotWriteOnce once,
    IOptions<HubSpotOptions> options,
    TimeProvider time,
    ILogger<HubSpotTaskSink> logger) : IHumanTaskSink
{
    public const string WriteType = "Aufgabe";

    public async Task CreateTaskAsync(string crmProjectId, string taskKey, string title, string body, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.WriteEnabled)
        {
            logger.LogDebug("HubSpot-{WriteType} für Deal {DealId} übersprungen (WriteEnabled=false): {WriteKey}", WriteType, crmProjectId, taskKey);
            return;
        }

        await once.RunAsync(WriteType, crmProjectId, $"task:{taskKey}", async token =>
        {
            var properties = new Dictionary<string, string>
            {
                ["hs_timestamp"] = HubSpotText.Timestamp(time.GetUtcNow()),
                ["hs_task_subject"] = title,
                ["hs_task_body"] = HubSpotText.Html(body),
                ["hs_task_status"] = "NOT_STARTED",
                ["hs_task_type"] = "TODO",
            };
            if (await OwnerAsync(crmProjectId, token) is { } owner)
                properties[HubSpotPropertyKeys.HubSpotOwnerId] = owner;

            return await api.CreateAssociatedObjectAsync("tasks", properties, o.ObjectTypeId, crmProjectId, token);
        }, ct);
    }

    private async Task<string?> OwnerAsync(string dealId, CancellationToken ct)
    {
        var o = options.Value;
        var deal = await api.GetObjectAsync(o.ObjectTypeId, dealId, [HubSpotPropertyKeys.HubSpotOwnerId], ct);
        if (deal?.Value(HubSpotPropertyKeys.HubSpotOwnerId) is { } owner)
            return owner;

        var fallback = string.IsNullOrWhiteSpace(o.DefaultTaskOwnerId) ? null : o.DefaultTaskOwnerId.Trim();
        logger.LogDebug("Deal {DealId} hat keinen Inhaber: Aufgabe {Assignment}", dealId, fallback is null ? "ohne Zuweisung" : "an DefaultTaskOwnerId");
        return fallback;
    }
}

/// <summary>
/// Audit-Ereignis als Notiz am Deal, wenn seine Stufe mindestens <see cref="HubSpotOptions.AuditLevel"/> ist.
/// Der vollständige Audit-Trail steht unabhängig davon im technischen Log (SerilogAuditSink).
/// </summary>
public sealed class HubSpotAuditSink(
    HubSpotApiClient api,
    HubSpotWriteOnce once,
    IOptions<HubSpotOptions> options,
    TimeProvider time,
    ILogger<HubSpotAuditSink> logger) : IAuditSink
{
    public const string WriteType = "Notiz";

    public async Task WriteAsync(AnmeldeProcess process, AuditEvent evt, CancellationToken ct)
    {
        var o = options.Value;
        if (evt.Level < o.AuditLevel)
        {
            logger.LogDebug("HubSpot-Audit {AuditType} für Deal {DealId} übersprungen: Stufe {AuditEventLevel} unter {AuditLevel}",
                evt.Type, process.CrmProjectId, evt.Level, o.AuditLevel);
            return;
        }

        var key = Key(process, evt);
        if (!o.WriteEnabled)
        {
            logger.LogDebug("HubSpot-{WriteType} für Deal {DealId} übersprungen (WriteEnabled=false): {WriteKey}", WriteType, process.CrmProjectId, key);
            return;
        }

        await once.RunAsync(WriteType, process.CrmProjectId, key, token => api.CreateAssociatedObjectAsync("notes",
            new Dictionary<string, string>
            {
                ["hs_timestamp"] = HubSpotText.Timestamp(time.GetUtcNow()),
                ["hs_note_body"] = HubSpotText.Html($"PV Autopilot · {evt.Type}: {evt.Message}"),
            },
            o.ObjectTypeId, process.CrmProjectId, token), ct);
    }

    /// <summary>
    /// Vorgang + Ereignistyp + Anzahl Zustandswechsel + Hash der Nachricht: derselbe Eintrag im selben Zustand
    /// (Retry, erneuter Lauf mit derselben Aufgabe) ergibt dieselbe Notiz, jeder neue Zustandswechsel eine neue.
    /// </summary>
    public static string Key(AnmeldeProcess process, AuditEvent evt) =>
        $"note:{process.Id:N}:{evt.Type}:{process.Transitions.Count}:{HubSpotWriteOnce.ShortHash(evt.Message)}";
}

/// <summary>
/// Legt Dokumente privat in HubSpot-Dateien ab (Ordner <c>/pv-autopilot/&lt;dealId&gt;</c>) und hängt sie mit einer
/// Notiz an den Deal. Gleicher Name und Inhalt = keine zweite Datei; gleiche Dateien = keine zweite Notiz.
/// </summary>
public sealed class HubSpotDocumentArchive(
    HubSpotApiClient api,
    HubSpotWriteOnce once,
    IOptions<HubSpotOptions> options,
    TimeProvider time,
    ILogger<HubSpotDocumentArchive> logger) : IDocumentArchive
{
    public const string FileWriteType = "Datei";
    public const string NoteWriteType = "Ablage-Notiz";
    public const string FolderRoot = "/pv-autopilot";

    public async Task StoreAsync(string crmProjectId, IReadOnlyList<GeneratedDocument> documents, CancellationToken ct)
    {
        if (documents.Count == 0)
            return;

        var o = options.Value;
        if (!o.WriteEnabled)
        {
            logger.LogDebug("HubSpot-{WriteType} für Deal {DealId} übersprungen (WriteEnabled=false): {DocumentCount} Dokument(e)",
                FileWriteType, crmProjectId, documents.Count);
            return;
        }

        var keys = documents.Select(d => FileKey(crmProjectId, d)).ToList();
        var fileIds = new List<string>(documents.Count);
        for (var i = 0; i < documents.Count; i++)
        {
            var document = documents[i];
            fileIds.Add(await once.RunAsync(FileWriteType, crmProjectId, keys[i], async token =>
                (await api.UploadFileAsync($"{FolderRoot}/{crmProjectId}", document.FileName, document.ContentType, document.Content, token)).Id,
                ct));
        }

        // Die Notiz macht die Dateien am Deal sichtbar; ihr Schlüssel hängt an genau diesen Dateien.
        var noteKey = $"file-note:{crmProjectId}:{HubSpotWriteOnce.ShortHash(string.Join('|', keys))}";
        await once.RunAsync(NoteWriteType, crmProjectId, noteKey, token => api.CreateAssociatedObjectAsync("notes",
            new Dictionary<string, string>
            {
                ["hs_timestamp"] = HubSpotText.Timestamp(time.GetUtcNow()),
                // Dateinamen stehen nur in HubSpot, nie im Log.
                ["hs_note_body"] = HubSpotText.Html("PV Autopilot · Dokumente abgelegt:" + Environment.NewLine
                    + string.Join(Environment.NewLine, documents.Select(d => d.FileName))),
                ["hs_attachment_ids"] = string.Join(';', fileIds),
            },
            o.ObjectTypeId, crmProjectId, token), ct);
    }

    /// <summary>Deal + Hash des Dateinamens (kann Kundennamen enthalten) + SHA-256 des Inhalts.</summary>
    public static string FileKey(string dealId, GeneratedDocument document) =>
        $"file:{dealId}:{HubSpotWriteOnce.ShortHash(document.FileName)}:{HubSpotWriteOnce.Sha256(document.Content)}";
}

/// <summary>
/// Spiegelt den Stand des Vorgangs an den Deal (<c>PATCH</c>), Property-Namen aus <see cref="HubSpotOptions.Properties"/>;
/// leerer Name = Feld wird nicht geschrieben. Idempotent, da immer der aktuelle Stand geschrieben wird.
/// </summary>
public sealed class HubSpotStatusSink(HubSpotApiClient api, IOptions<HubSpotOptions> options, ILogger<HubSpotStatusSink> logger) : ICrmStatusSink
{
    public const string WriteType = "Status";

    public async Task PublishAsync(AnmeldeProcess process, CancellationToken ct)
    {
        var o = options.Value;
        var properties = Properties(process, o);
        if (!o.WriteEnabled || properties.Count == 0)
        {
            logger.LogDebug("HubSpot-{WriteType} für Deal {DealId} übersprungen ({SkipReason}): {State}", WriteType, process.CrmProjectId,
                o.WriteEnabled ? "keine Status-Property konfiguriert" : "WriteEnabled=false", process.State);
            return;
        }

        await api.UpdateObjectAsync(o.ObjectTypeId, process.CrmProjectId, properties, ct);
        logger.LogInformation("HubSpot-{WriteType} für Deal {DealId} geschrieben: {State} ({PropertyCount} Properties)",
            WriteType, process.CrmProjectId, process.State, properties.Count);
    }

    /// <summary>
    /// Status (Zustandsname) und Fehlertext werden immer geschrieben, der Fehlertext außerhalb von Blocked geleert.
    /// Netzbetreiber und MaStR-Nummern nur, wenn bekannt – ein leerer Stand überschreibt keinen von Hand gepflegten Wert.
    /// Der Fehlertext ist <see cref="AnmeldeProcess.BlockReason"/> und enthält keine personenbezogenen Daten (<see cref="BlockReasons"/>).
    /// </summary>
    public static Dictionary<string, string> Properties(AnmeldeProcess process, HubSpotOptions o)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        void Set(string key, string? value, bool always)
        {
            if (o.PropertyName(key) is { } name && (always || !string.IsNullOrWhiteSpace(value)))
                properties[name] = value ?? string.Empty;
        }

        Set(HubSpotPropertyKeys.Status, process.State.ToString(), always: true);
        Set(HubSpotPropertyKeys.Fehlertext, process.State == ProcessState.Blocked ? process.BlockReason : null, always: true);
        Set(HubSpotPropertyKeys.Netzbetreiber, process.NetzbetreiberCode, always: false);
        Set(HubSpotPropertyKeys.MastrNummerAnlage, process.MastrNummerAnlage, always: false);
        Set(HubSpotPropertyKeys.MastrNummerSpeicher, process.MastrNummerSpeicher, always: false);
        return properties;
    }
}

/// <summary>
/// MaStR-Login aus den Deal-Properties <c>MastrBenutzername</c>/<c>MastrPasswort</c> (nur lesend).
/// <see cref="DeleteAsync"/> löscht bewusst nicht: Abweichung vom Beschluss Termin 3 (26.10.) auf Martins Wunsch,
/// Bestätigung durch Michael am 09.11. offen (docs/konzept/hubspot-adapter.md).
/// </summary>
public sealed class HubSpotMastrCredentialStore(
    HubSpotApiClient api,
    IOptions<HubSpotOptions> options,
    ILogger<HubSpotMastrCredentialStore> logger) : IMastrCredentialStore
{
    public async Task<MastrCredentials?> GetAsync(string crmProjectId, CancellationToken ct)
    {
        var o = options.Value;
        var userProperty = o.PropertyName(HubSpotPropertyKeys.MastrBenutzername);
        var passwordProperty = o.PropertyName(HubSpotPropertyKeys.MastrPasswort);
        if (userProperty is null || passwordProperty is null)
        {
            logger.LogWarning("MaStR-Login für Deal {DealId} nicht lesbar: HubSpot:Properties {MissingKeys} nicht konfiguriert", crmProjectId,
                new[] { (HubSpotPropertyKeys.MastrBenutzername, userProperty), (HubSpotPropertyKeys.MastrPasswort, passwordProperty) }
                    .Where(p => p.Item2 is null).Select(p => p.Item1).ToList());
            return null;
        }

        var deal = await api.GetObjectAsync(o.ObjectTypeId, crmProjectId, [userProperty, passwordProperty], ct);
        var user = deal?.Value(userProperty);
        var password = deal?.Value(passwordProperty);
        var found = user is not null && password is not null;
        // Nie Benutzername oder Passwort loggen.
        logger.LogInformation("MaStR-Login für Deal {DealId} gelesen, vollständig: {Found}", crmProjectId, found);
        return found ? new MastrCredentials(user!, password!) : null;
    }

    public Task DeleteAsync(string crmProjectId, CancellationToken ct)
    {
        logger.LogInformation("MaStR-Passwort am Deal {DealId} wird nicht gelöscht (bewusste Abweichung, Bestätigung offen)", crmProjectId);
        return Task.CompletedTask;
    }
}
