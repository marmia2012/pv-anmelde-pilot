using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PvAutopilot.Core.Http;

namespace PvAutopilot.Sources.HubSpot.Api;

/// <summary>
/// Zugriff auf die HubSpot-API (CRM v3/v4, Files v3). Auth, Backoff und Timeout setzt die
/// HttpClient-Konfiguration (<see cref="DependencyInjection"/>). Schreibende Methoden ruft nur ein Sink auf, der
/// <see cref="HubSpotOptions.WriteEnabled"/> geprüft hat; der Client selbst prüft das nicht.
/// </summary>
public sealed class HubSpotApiClient(HttpClient http)
{
    /// <summary>
    /// Von HubSpot vordefinierte Verknüpfungstypen (<c>HUBSPOT_DEFINED</c>) Notiz/Aufgabe → Standardobjekt, Artikel → Deal.
    /// Für andere Objekttypen (z. B. Projekte, Custom Objects) wird nach dem Anlegen die Standard-Verknüpfung gesetzt.
    /// </summary>
    private static readonly Dictionary<(string From, string To), int> AssociationTypeIds = new()
    {
        [("notes", "0-1")] = 202, [("notes", "0-2")] = 190, [("notes", "0-3")] = 214, [("notes", "0-5")] = 228,
        [("tasks", "0-1")] = 204, [("tasks", "0-2")] = 192, [("tasks", "0-3")] = 216, [("tasks", "0-5")] = 230,
        [("line_items", "0-3")] = 20,
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>HubSpot erlaubt höchstens 100 IDs je Batch-Read.</summary>
    internal const int BatchSize = 100;

    /// <returns><c>null</c>, wenn das Objekt nicht existiert (404).</returns>
    public Task<HubSpotObject?> GetObjectAsync(string objectType, string id, IEnumerable<string> properties, CancellationToken ct)
    {
        var names = string.Join(',', properties.Select(Escape));
        var query = names.Length == 0 ? string.Empty : $"?properties={names}";
        return GetOrNullAsync<HubSpotObject>($"crm/v3/objects/{Escape(objectType)}/{Escape(id)}{query}", ct);
    }

    /// <summary>IDs der verknüpften Objekte (alle Seiten). Existiert das Ausgangsobjekt nicht, leer.</summary>
    public async Task<IReadOnlyList<string>> GetAssociatedIdsAsync(string fromType, string id, string toType, CancellationToken ct)
    {
        var ids = new List<string>();
        string? after = null;
        do
        {
            var path = $"crm/v4/objects/{Escape(fromType)}/{Escape(id)}/associations/{Escape(toType)}?limit=500"
                + (after is null ? string.Empty : $"&after={Escape(after)}");
            var page = await GetOrNullAsync<HubSpotAssociationsResponse>(path, ct);
            if (page is null)
                break;
            ids.AddRange((page.Results ?? []).Select(r => r.ToObjectId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            after = page.Paging?.Next?.After;
        }
        while (!string.IsNullOrEmpty(after));

        return ids.Distinct().ToList();
    }

    /// <summary>Liest Objekte in Blöcken zu <see cref="BatchSize"/>. Nicht (mehr) vorhandene IDs fehlen im Ergebnis.</summary>
    public async Task<IReadOnlyList<HubSpotObject>> BatchReadAsync(
        string objectType, IReadOnlyList<string> ids, IReadOnlyList<string> properties, CancellationToken ct)
    {
        var result = new List<HubSpotObject>();
        foreach (var chunk in ids.Chunk(BatchSize))
        {
            var path = $"crm/v3/objects/{Escape(objectType)}/batch/read";
            var body = new { properties, inputs = chunk.Select(id => new { id }) };
            using var response = await http.PostAsJsonAsync(path, body, Json, ct);
            EnsureSuccess(path, response);
            var batch = await response.Content.ReadFromJsonAsync<HubSpotBatchResponse>(Json, ct);
            result.AddRange(batch?.Results ?? []);
        }
        return result;
    }

    /// <returns><c>null</c>, wenn die Datei nicht (mehr) existiert (404).</returns>
    public Task<HubSpotFile?> GetFileAsync(string fileId, CancellationToken ct) =>
        GetOrNullAsync<HubSpotFile>($"files/v3/files/{Escape(fileId)}", ct);

    public async Task<IReadOnlyList<HubSpotPipeline>> GetPipelinesAsync(string objectType, CancellationToken ct) =>
        (await GetRequiredAsync<HubSpotPipelinesResponse>($"crm/v3/pipelines/{Escape(objectType)}", ct)).Results ?? [];

    public async Task<IReadOnlyList<HubSpotProperty>> GetPropertiesAsync(string objectType, CancellationToken ct) =>
        (await GetRequiredAsync<HubSpotPropertiesResponse>($"crm/v3/properties/{Escape(objectType)}", ct)).Results ?? [];

    /// <summary>
    /// Legt ein Objekt (z. B. <c>notes</c>, <c>tasks</c>) an und verknüpft es mit <paramref name="toObjectType"/>/<paramref name="toId"/>.
    /// Bekannte Verknüpfungstypen gehen im selben Request mit; sonst folgt ein zweiter Request (Standard-Verknüpfung).
    /// </summary>
    /// <returns>ID des angelegten Objekts.</returns>
    public async Task<string> CreateAssociatedObjectAsync(
        string objectType, IReadOnlyDictionary<string, string> properties, string toObjectType, string toId, CancellationToken ct)
    {
        var inline = AssociationTypeIds.TryGetValue((objectType, toObjectType), out var typeId);
        object[] associations = inline
            ?
            [
                new
                {
                    to = new { id = toId },
                    types = new[] { new { associationCategory = "HUBSPOT_DEFINED", associationTypeId = typeId } },
                },
            ]
            : [];

        var path = $"crm/v3/objects/{Escape(objectType)}";
        using var response = await CreatingPostAsync(path, JsonContent.Create(new { properties, associations }, options: Json), ct);
        EnsureSuccess(path, response);
        var created = await response.Content.ReadFromJsonAsync<HubSpotObject>(Json, ct)
            ?? throw new InvalidOperationException($"HubSpot {path}: leere Antwort.");

        if (!inline)
        {
            // Lücke: bricht der Lauf hier ab, bleibt ein unverknüpftes Objekt zurück; der Retry legt ein neues an.
            // Der PUT ist idempotent (dieselbe Verknüpfung entsteht nicht doppelt) und darf deshalb voll wiederholt werden.
            var associate = $"crm/v4/objects/{Escape(objectType)}/{Escape(created.Id)}/associations/default/{Escape(toObjectType)}/{Escape(toId)}";
            using var associated = await http.PutAsync(associate, content: null, ct);
            EnsureSuccess(associate, associated);
        }
        return created.Id;
    }

    /// <summary>Setzt Properties eines Objekts (<c>PATCH</c>). Ein leerer Wert leert die Property in HubSpot.</summary>
    public async Task UpdateObjectAsync(string objectType, string id, IReadOnlyDictionary<string, string> properties, CancellationToken ct)
    {
        var path = $"crm/v3/objects/{Escape(objectType)}/{Escape(id)}";
        using var response = await http.PatchAsJsonAsync(path, new { properties }, Json, ct);
        EnsureSuccess(path, response);
    }

    /// <summary>Lädt eine Datei privat (nur für angemeldete HubSpot-Nutzer sichtbar) in <paramref name="folderPath"/> hoch.</summary>
    /// <returns>Die angelegte Datei (ID für <c>hs_attachment_ids</c>).</returns>
    public async Task<HubSpotFile> UploadFileAsync(
        string folderPath, string fileName, string contentType, byte[] content, CancellationToken ct)
    {
        const string path = "files/v3/files";
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        var form = new MultipartFormDataContent
        {
            { file, "file", fileName },
            { new StringContent(fileName), "fileName" },
            { new StringContent(folderPath), "folderPath" },
            {
                // Gleicher Name im Ordner: HubSpot legt eine neue Datei mit eigenem Namen an statt zu überschreiben.
                new StringContent(JsonSerializer.Serialize(new { access = "PRIVATE", overwrite = false, duplicateValidationStrategy = "NONE" })),
                "options"
            },
        };

        using var response = await CreatingPostAsync(path, form, ct);
        EnsureSuccess(path, response);
        return await response.Content.ReadFromJsonAsync<HubSpotFile>(Json, ct)
            ?? throw new InvalidOperationException($"HubSpot {path}: leere Antwort.");
    }

    /// <summary>
    /// <c>POST</c>, der etwas anlegt: der Backoff wiederholt nur bei 429 (dann hat HubSpot nichts angelegt). Ein 5xx geht
    /// an den Aufrufer – HubSpot kann trotzdem angelegt haben; der Job-Retry prüft vorher das Ledger.
    /// </summary>
    private async Task<HttpResponseMessage> CreatingPostAsync(string path, HttpContent content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        request.Options.Set(ExponentialBackoffHandler.RateLimitOnly, true);
        return await http.SendAsync(request, ct);
    }

    /// <returns><c>null</c> bei 404 (fachlich: nicht vorhanden). Andere Fehler werfen und werden vom Job wiederholt.</returns>
    private async Task<T?> GetOrNullAsync<T>(string path, CancellationToken ct) where T : class
    {
        using var response = await http.GetAsync(path, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        EnsureSuccess(path, response);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new InvalidOperationException($"HubSpot {PathOnly(path)}: leere Antwort.");
    }

    /// <summary>Auch 404 ist hier technisch (z. B. falscher Objekttyp in der Konfiguration).</summary>
    private async Task<T> GetRequiredAsync<T>(string path, CancellationToken ct) where T : class
    {
        using var response = await http.GetAsync(path, ct);
        EnsureSuccess(path, response);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new InvalidOperationException($"HubSpot {PathOnly(path)}: leere Antwort.");
    }

    private static void EnsureSuccess(string path, HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            // Nur Pfad und Status – der Body kann Kundendaten enthalten.
            throw new HttpRequestException($"HubSpot {PathOnly(path)}: HTTP {(int)response.StatusCode}", null, response.StatusCode);
    }

    private static string PathOnly(string path) => path.Split('?')[0];

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
