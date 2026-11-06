using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Core.Netzbetreiber;

/// <summary>Ermittelt den zuständigen Netzbetreiber zur Anlagenadresse.</summary>
public interface INetzbetreiberResolver
{
    /// <returns>Netzbetreiber-Code (z. B. <c>netze-duisburg</c>) oder <c>null</c>, wenn nicht eindeutig.</returns>
    Task<string?> ResolveAsync(PostalAddress address, CancellationToken ct);
}

/// <summary>
/// Ein Netzbetreiber = ein Adapter. Der Adapter erzeugt die Unterlagen; der Versand läuft über
/// den austauschbaren <see cref="Channel"/> (Portal oder Mail) und das <see cref="SubmissionGate"/>.
/// </summary>
public interface INetzbetreiberAdapter
{
    string Code { get; }

    ISubmissionChannel Channel { get; }

    /// <summary>
    /// Einreichungen, die dieser Netzbetreiber separat erwartet. Fehlt eine Art (z. B. Zählerwechsel bei
    /// Netze Duisburg, der Zählerantrag geht mit dem E.8 raus), überspringt der Vorgang diese Stufe.
    /// </summary>
    IReadOnlySet<SubmissionKind> Einreichungen { get; }

    /// <summary>Fachliche Prüfung vor dem Erzeugen. Leere Liste = alles in Ordnung.</summary>
    IReadOnlyList<string> Validate(SubmissionKind kind, NabDataCard card);

    Task<ApplicationPackage> PrepareAsync(SubmissionKind kind, AnmeldeProcess process, CancellationToken ct);
}
