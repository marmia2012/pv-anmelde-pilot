using System.Net;

namespace PvAutopilot.Sources.Reonic.Api;

/// <summary>
/// Lädt Dateien über signierte URLs (z. B. <c>signedPdfUrl</c>). Eigener HttpClient <b>ohne</b> <c>X-Authorization</c>
/// und ohne Basis-URL (<see cref="DependencyInjection"/>): der Reonic-API-Key darf nicht an fremde Hosts gehen.
/// Nur <c>https</c>, höchstens <see cref="MaxBytes"/>. Fehlermeldungen enthalten nie die URL (personenbezogen).
/// </summary>
public sealed class ReonicFileDownloader(HttpClient http)
{
    /// <summary>Name des HttpClients (für Tests und Konfiguration).</summary>
    public const string HttpClientName = "reonic-signed-files";

    public const long MaxBytes = 20L * 1024 * 1024;

    /// <returns>
    /// Inhalt; <c>null</c>, wenn die URL kein absolutes <c>https</c> ist, die Datei fehlt bzw. die Signatur abgelaufen ist
    /// (403/404/410) oder sie größer als <see cref="MaxBytes"/> ist (fachlich). Andere Fehler werfen (Retry).
    /// </returns>
    public async Task<byte[]?> DownloadAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Gone)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Reonic-Datei (signierte URL): HTTP {(int)response.StatusCode}", null, response.StatusCode);

        if (response.Content.Headers.ContentLength is > MaxBytes)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
