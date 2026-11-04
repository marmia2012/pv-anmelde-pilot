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

public sealed record MastrRegistration(string? AnlagenMastrNummer, string? SpeicherMastrNummer);

public sealed record MastrCredentials(string Username, string Password)
{
    public override string ToString() => "MastrCredentials { *** }";
}
