using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Sources;
using PvAutopilot.Sources.HubSpot;
using PvAutopilot.Sources.HubSpot.Api;
using PvAutopilot.Sources.HubSpot.Documents;
using K = PvAutopilot.Sources.HubSpot.HubSpotPropertyKeys;

namespace PvAutopilot.Tests.Sources.HubSpot;

// Simulierte HubSpot-API: feste JSON-Antworten, kein echter Aufruf. Alle Daten sind erfunden.

internal sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string? Body, string? Authorization = null)
{
    public bool IsWrite => Method != HttpMethod.Get && !Path.EndsWith("batch/read", StringComparison.Ordinal);

    /// <summary>Namen aus <c>?properties=a,b,c</c>.</summary>
    public IReadOnlyList<string> RequestedProperties =>
        Query.TrimStart('?').Split('&').Where(p => p.StartsWith("properties=", StringComparison.Ordinal))
            .SelectMany(p => Uri.UnescapeDataString(p["properties=".Length..]).Split(',')).ToList();
}

/// <summary>Antwortet je "METHODE pfad" (ohne Query); unbekannte Pfade sind 404.</summary>
internal sealed class HubSpotHandler : HttpMessageHandler
{
    public Dictionary<string, Func<RecordedRequest, HttpResponseMessage>> Routes { get; } = [];
    public List<RecordedRequest> Requests { get; } = [];

    public IEnumerable<RecordedRequest> Writes => Requests.Where(r => r.IsWrite);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var recorded = new RecordedRequest(request.Method, Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/'), uri.Query, body, request.Headers.Authorization?.ToString());
        Requests.Add(recorded);
        return Routes.TryGetValue($"{recorded.Method} {recorded.Path}", out var respond)
            ? respond(recorded)
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public void Map(HttpMethod method, string path, Func<RecordedRequest, HttpResponseMessage> respond) =>
        Routes[$"{method} {path}"] = respond;

    public void Get(string path, object json) => Map(HttpMethod.Get, path, _ => Json(json));
    public void Get(string path, HttpStatusCode status, string body = "") => Map(HttpMethod.Get, path, _ => Status(status, body));

    public static HttpResponseMessage Json(object json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(json), Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status, string body = "") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    /// <summary>Formatierte Texte und alle strukturierten Werte.</summary>
    public string All => string.Join('\n', Messages);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var text = new StringBuilder(formatter(state, exception));
        if (state is IEnumerable<KeyValuePair<string, object?>> values)
            foreach (var (key, value) in values)
                text.Append(' ').Append(key).Append('=')
                    .Append(value is System.Collections.IEnumerable e && value is not string ? string.Join(",", e.Cast<object?>()) : value);
        Messages.Add($"{logLevel}: {text}");
    }
}

/// <summary>Verdrahtet Optionen, HTTP-Attrappe und die lesenden Klassen des Adapters.</summary>
internal sealed class HubSpotRig
{
    public const string BaseUrl = "https://api.hubspot.test/";
    public const string DealId = "101";

    public HubSpotHandler Handler { get; } = new();
    public HubSpotOptions Options { get; } = DefaultOptions();
    public CapturingLogger<HubSpotCrmProjectSource> SourceLog { get; } = new();
    public CapturingLogger<VollmachtFinder> FinderLog { get; } = new();

    private readonly Dictionary<string, List<(string Id, string? Attachments, string? Timestamp)>> _engagements =
        new() { ["notes"] = [], ["emails"] = [] };

    public HubSpotRig()
    {
        SetDeal(BaseDeal());
        foreach (var type in _engagements.Keys)
        {
            var t = type;
            Handler.Map(HttpMethod.Get, $"crm/v4/objects/0-3/{DealId}/associations/{t}", _ =>
                HubSpotHandler.Json(new { results = _engagements[t].Select(e => new { toObjectId = e.Id }) }));
            Handler.Map(HttpMethod.Post, $"crm/v3/objects/{t}/batch/read", _ =>
                HubSpotHandler.Json(new
                {
                    results = _engagements[t].Select(e => new
                    {
                        id = e.Id,
                        properties = new Dictionary<string, string?> { ["hs_attachment_ids"] = e.Attachments, ["hs_timestamp"] = e.Timestamp },
                    }),
                }));
        }
    }

    public HubSpotApiClient Api => new(new HttpClient(Handler) { BaseAddress = new Uri(BaseUrl) });

    public VollmachtFinder Finder() => new(Api, Wrap(Options), FinderLog);

    public HubSpotCrmProjectSource Source() => new(Api, Finder(), Wrap(Options), SourceLog);

    public static IOptions<HubSpotOptions> Wrap(HubSpotOptions o) => Microsoft.Extensions.Options.Options.Create(o);

    public static HubSpotOptions DefaultOptions()
    {
        var all = typeof(K).GetFields().Where(f => f.IsLiteral && f.Name != nameof(K.HubSpotOwnerId)).Select(f => f.Name);
        return new HubSpotOptions
        {
            AccessToken = "test-token",
            Properties = all.ToDictionary(k => k, PropertyName),
            EinspeiseartValues = new(StringComparer.OrdinalIgnoreCase)
            {
                ["voll"] = Einspeiseart.Volleinspeisung,
                ["teil"] = Einspeiseart.Teileinspeisung,
            },
            TriggerRules = [new() { Kind = ProcessTriggerKind.Start, PropertyName = "dealstage", Values = ["stage-start"] }],
        };
    }

    /// <summary><c>KundeIban</c> wird zu <c>kunde_iban</c>.</summary>
    public static string PropertyName(string key) => Regex.Replace(key, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();

    /// <summary>Vollständiger Deal mit Kundenadresse in getrennten Feldern; alles andere kommt aus den Tests.</summary>
    public static Dictionary<string, string?> BaseDeal() => new()
    {
        [K.KundeAnrede] = "Frau",
        [K.KundeVorname] = "Erika",
        [K.KundeNachname] = "Mustermann",
        [K.KundeEmail] = "erika.mustermann@example.org",
        [K.KundeTelefon] = "0203 555010",
        [K.KundeIban] = "DE02 1203 0000 0000 2020 51",
        [K.KundeStrasse] = "Beispielweg",
        [K.KundeHausnummer] = "7",
        [K.KundePlz] = "47051",
        [K.KundeOrt] = "Duisburg",
        [K.ReonicProjectId] = "reonic-42",
    };

    /// <summary>Deal-Antwort mit Properties unter ihren konfigurierten HubSpot-Namen (<c>null</c> = leer).</summary>
    public void SetDeal(Dictionary<string, string?> byKey, string? ownerId = null)
    {
        var properties = byKey.ToDictionary(kv => Options.PropertyName(kv.Key) ?? kv.Key, kv => kv.Value);
        if (ownerId is not null)
            properties[K.HubSpotOwnerId] = ownerId;
        Handler.Get($"crm/v3/objects/0-3/{DealId}", new { id = DealId, properties });
    }

    public void AddAttachments(string engagementId, string fileIds, string? timestamp, string type = "notes") =>
        _engagements[type].Add((engagementId, fileIds, timestamp));

    public void AddFile(string fileId, string name, string extension, string createdAt) =>
        Handler.Get($"files/v3/files/{fileId}", new { id = fileId, name, extension, createdAt });

    public Task<CrmProjectData> ReadAsync() => Source().GetProjectAsync(DealId, default);
}

/// <summary>Merkt sich die Writes wie die Datenbank (Verhalten von <c>EfCrmWriteLedger</c>).</summary>
internal sealed class InMemoryCrmWriteLedger : ICrmWriteLedger
{
    public Dictionary<string, CrmWriteRecord> Records { get; } = [];

    public Task<CrmWriteRecord?> FindAsync(string key, CancellationToken ct) => Task.FromResult(Records.GetValueOrDefault(key));

    public Task<bool> RecordAsync(string key, string externalId, CancellationToken ct)
    {
        if (Records.ContainsKey(key))
            return Task.FromResult(false);
        Records[key] = new CrmWriteRecord { Key = key, ExternalId = externalId, CreatedAt = TestData.Now };
        return Task.FromResult(true);
    }
}
