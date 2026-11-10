using PvAutopilot.Core.DataCard;

namespace PvAutopilot.Core.Orders;

/// <summary>Quelle unterschriebener Aufträge (Instanz reinsolar: Reonic, nur lesend).</summary>
public interface ISignedOrderSource
{
    /// <param name="signatureRequestId">Gegebene Signaturanfrage; <c>null</c> = die jüngste unterschriebene.</param>
    /// <returns><c>null</c>, wenn das Projekt nicht existiert.</returns>
    Task<SignedOrder?> GetSignedOrderAsync(string planningProjectId, string? signatureRequestId, CancellationToken ct);

    /// <returns>Inhalt des Dokuments; <c>null</c>, wenn es nicht (mehr) abrufbar ist (fachlich).</returns>
    Task<byte[]?> DownloadAsync(DocumentRef document, CancellationToken ct);
}

/// <summary>
/// Überträgt einen unterschriebenen Auftrag ins CRM (Instanz reinsolar: Artikel, Properties und PDF am HubSpot-Deal).
/// Muss idempotent sein; ohne Schreibfreigabe nur Vorschau.
/// </summary>
public interface ICrmOrderSink
{
    /// <param name="order">Auftrag mit <see cref="SignedOrder.CrmProjectId"/> und <see cref="SignedOrder.SignatureRequestId"/>.</param>
    /// <param name="signedPdf">Inhalt von <see cref="SignedOrder.SignedPdf"/>, falls heruntergeladen.</param>
    Task<OrderPublishResult> PublishAsync(SignedOrder order, byte[]? signedPdf, CancellationToken ct);

    /// <summary>
    /// Legt eine Aufgabe zur Auftragsübernahme am CRM-Objekt an (je Schlüssel höchstens eine; ohne Schreibfreigabe nur Log).
    /// </summary>
    /// <returns><c>false</c>, wenn das CRM-Objekt nicht existiert (dann ist keine Aufgabe möglich).</returns>
    Task<bool> CreateOrderTaskAsync(string dealId, string taskKey, string title, string body, CancellationToken ct);
}

/// <summary>Plant die Übertragung eines Auftrags als Hintergrund-Job ein (Instanz: Hangfire).</summary>
public interface IOrderTransferScheduler
{
    void Enqueue(string planningProjectId, string? signatureRequestId);
}

/// <summary>
/// Ergebnis bzw. Vorschau einer Übertragung, ohne Werte: nur Anzahlen, Property-Namen und Ja/Nein.
/// <see cref="Written"/> = <c>false</c> mit <see cref="SkipReason"/>: nichts geschrieben; die übrigen Felder zeigen dann,
/// was geschrieben würde (Vorschau bei <see cref="OrderSkipReasons.WriteDisabled"/>).
/// </summary>
public sealed record OrderPublishResult
{
    public bool Written { get; init; }
    public string? SkipReason { get; init; }
    public int LineItemCount { get; init; }

    /// <summary>Interne Namen der (zu) schreibenden CRM-Properties.</summary>
    public IReadOnlyList<string> PropertyNames { get; init; } = [];

    /// <summary>PDF abgelegt bzw. (Vorschau) würde abgelegt.</summary>
    public bool PdfStored { get; init; }

    /// <summary>Artikel am CRM-Objekt, die nicht aus dieser Übertragung stammen.</summary>
    public int ForeignLineItems { get; init; }

    /// <summary>Das CRM hat den PATCH der Auftrags-Properties dauerhaft abgelehnt (4xx außer 429); Artikel und PDF sind trotzdem übertragen.</summary>
    public bool PropertiesRejected { get; init; }

    public bool DealNotFound => SkipReason == OrderSkipReasons.DealNotFound;
}

/// <summary>Gründe, aus denen eine Übertragung nichts schreibt.</summary>
public static class OrderSkipReasons
{
    /// <summary><c>HubSpot:WriteEnabled = false</c>: Vorschau, nichts geschrieben.</summary>
    public const string WriteDisabled = "WriteEnabled=false";

    /// <summary>Am CRM-Objekt wurde schon eine andere Unterschrift übertragen.</summary>
    public const string SecondSignature = "second-signature";

    /// <summary>Das CRM-Objekt existiert nicht (404).</summary>
    public const string DealNotFound = "deal-not-found";

    public const string ProjectNotFound = "project-not-found";
    public const string NoDeal = "no-deal";
    public const string NotSigned = "not-signed";

    /// <summary>Unterschriebene Variante fehlt oder ist nicht ladbar: ohne Positionen wird nichts übertragen.</summary>
    public const string VariantMissing = "variant-missing";
}

/// <summary>Ergebnis von <see cref="OrderTransferService.TransferAsync"/> für Admin-Endpunkt und Log, ohne Werte.</summary>
public sealed record OrderTransferResult
{
    public required string PlanningProjectId { get; init; }
    public string? SignatureRequestId { get; init; }
    public string? CrmProjectId { get; init; }
    public bool Written { get; init; }
    public string? SkipReason { get; init; }
    public int LineItemCount { get; init; }
    public IReadOnlyList<string> PropertyNames { get; init; } = [];
    public bool PdfStored { get; init; }
    public int ForeignLineItems { get; init; }
    public bool PropertiesRejected { get; init; }

    /// <summary>Fachliche Probleme (Produktnamen erlaubt, keine Werte).</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];
}

/// <summary>
/// Eine ausdrücklich gemeldete Signaturanfrage ist in der Quelle nicht (oder nicht als unterschrieben) sichtbar. Technisch:
/// der Job wiederholt, nach dem letzten Versuch Alarm. Die Meldung enthält nur Projekt- und Signatur-ID.
/// </summary>
public sealed class SignatureNotVisibleException(string planningProjectId, string signatureRequestId)
    : InvalidOperationException($"Reonic-Projekt {planningProjectId}: Signatur {signatureRequestId} nicht gefunden oder nicht unterschrieben.")
{
    public string PlanningProjectId { get; } = planningProjectId;
    public string SignatureRequestId { get; } = signatureRequestId;
}
