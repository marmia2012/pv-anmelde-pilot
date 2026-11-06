using PvAutopilot.Core.Processes;

namespace PvAutopilot.Core.Mastr;

/// <summary>Registrierung von Anlage und Speicher im Marktstammdatenregister.</summary>
public interface IMastrRegistrar
{
    Task<MastrRegistration> RegisterAsync(AnmeldeProcess process, MastrCredentials credentials, CancellationToken ct);
}

/// <summary>Kundenlogin für das MaStR-Webportal. Wird nach erfolgreicher Registrierung gelöscht.</summary>
public interface IMastrCredentialStore
{
    Task<MastrCredentials?> GetAsync(string crmProjectId, CancellationToken ct);
    Task DeleteAsync(string crmProjectId, CancellationToken ct);
}

public sealed record MastrRegistration(string? AnlagenMastrNummer, string? SpeicherMastrNummer)
{
    /// <summary>
    /// Kodiert beide Nummern für <see cref="Submission.SubmissionRecord.ExternalReference"/>, damit sie nach
    /// einem Abbruch aus dem Ledger wiederhergestellt werden: <c>anlage=SEE…;speicher=SSE…</c>.
    /// Fehlende Teile entfallen; MaStR-Nummern enthalten weder <c>;</c> noch <c>=</c>.
    /// </summary>
    public string ToReference() => string.Join(';',
        new[] { ("anlage", AnlagenMastrNummer), ("speicher", SpeicherMastrNummer) }
            .Where(p => !string.IsNullOrWhiteSpace(p.Item2))
            .Select(p => $"{p.Item1}={p.Item2}"));

    public static MastrRegistration FromReference(string? reference)
    {
        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (reference ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Split('=', 2) is [var name, var value] && value.Length > 0)
                parts[name.Trim()] = value.Trim();
        }
        return new MastrRegistration(parts.GetValueOrDefault("anlage"), parts.GetValueOrDefault("speicher"));
    }
}

public sealed record MastrCredentials(string Username, string Password)
{
    public override string ToString() => "MastrCredentials { *** }";
}
