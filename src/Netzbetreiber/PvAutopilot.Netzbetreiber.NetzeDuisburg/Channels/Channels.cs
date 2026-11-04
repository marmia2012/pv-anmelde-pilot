using PvAutopilot.Core.Submission;

namespace PvAutopilot.Netzbetreiber.NetzeDuisburg.Channels;

/// <summary>Versand als Mail aus dem Postfach anschlussplanung@… (Microsoft Graph).</summary>
public sealed class NetzeDuisburgMailChannel : ISubmissionChannel
{
    public string Name => "netze-duisburg-mail";

    public Task<SubmissionReceipt> SendAsync(ApplicationPackage package, CancellationToken ct) =>
        // TODO: Mail mit allen Unterlagen über Graph senden; Message-ID als ExternalReference.
        throw new SubmissionNotSentException("Mailversand Netze Duisburg ist noch nicht umgesetzt.");
}

/// <summary>Versand über das PV-Anmeldeportal (SAP UI5) per Playwright – oder direkt per OData mit Portal-Session.</summary>
public sealed class NetzeDuisburgPortalChannel : ISubmissionChannel
{
    public string Name => "netze-duisburg-portal";

    public Task<SubmissionReceipt> SendAsync(ApplicationPackage package, CancellationToken ct) =>
        // TODO: Klickstrecke mit NetzeDuisburgSelectors; Vorgangsnummer des Portals als ExternalReference.
        throw new SubmissionNotSentException("Portalversand Netze Duisburg ist noch nicht umgesetzt.");
}

/// <summary>Alle Selektoren des Portals an einer Stelle.</summary>
internal static class NetzeDuisburgSelectors
{
    // TODO: beim Bau der Klickstrecke füllen.
}
