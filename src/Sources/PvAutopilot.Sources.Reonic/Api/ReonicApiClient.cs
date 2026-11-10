using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PvAutopilot.Sources.Reonic.Api;

/// <summary>
/// Lesender Zugriff auf die Reonic REST API v3. Auth-, Cache-Header und Backoff setzt die HttpClient-Konfiguration
/// (<see cref="DependencyInjection"/>).
/// </summary>
public sealed class ReonicApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ReonicProject?> GetProjectAsync(string projectId, CancellationToken ct) =>
        GetOrNullAsync<ReonicProject>($"residentialProjects/{Escape(projectId)}", ct);

    public Task<ReonicVariant?> GetVariantAsync(string projectId, string variantId, CancellationToken ct) =>
        GetOrNullAsync<ReonicVariant>($"residentialProjects/{Escape(projectId)}/variants/{Escape(variantId)}", ct);

    /// <returns>Alle Signaturanfragen des Projekts; leer, wenn das Projekt keine hat bzw. nicht existiert (404).</returns>
    public async Task<IReadOnlyList<ReonicSignatureRequest>> GetSignatureRequestsAsync(string projectId, CancellationToken ct) =>
        (await GetOrNullAsync<ReonicSignatureRequestsResponse>($"residentialProjects/{Escape(projectId)}/signatureRequests", ct))?.Data ?? [];

    public Task<ReonicComponent?> GetComponentVersionAsync(string componentId, string versionId, CancellationToken ct) =>
        GetOrNullAsync<ReonicComponent>($"components/{Escape(componentId)}/versions/{Escape(versionId)}", ct);

    public Task<ReonicChecklist?> GetChecklistAsync(string projectId, string stage, CancellationToken ct) =>
        GetOrNullAsync<ReonicChecklist>($"checklists/{Escape(projectId)}?stage={Escape(stage)}", ct);

    /// <returns><c>null</c> bei 404 (fachlich: nicht vorhanden). Andere Fehler werfen und werden vom Job wiederholt.</returns>
    private async Task<T?> GetOrNullAsync<T>(string path, CancellationToken ct) where T : class
    {
        using var response = await http.GetAsync(path, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            // Nur Pfad und Status – der Body kann Kundendaten enthalten.
            throw new HttpRequestException($"Reonic {path.Split('?')[0]}: HTTP {(int)response.StatusCode}", null, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new InvalidOperationException($"Reonic {path.Split('?')[0]}: leere Antwort.");
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
