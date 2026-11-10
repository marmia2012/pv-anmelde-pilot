using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Orders;
using PvAutopilot.Core.Submission;
using PvAutopilot.Sources.HubSpot.Api;
using PvAutopilot.Sources.HubSpot.Mapping;

namespace PvAutopilot.Sources.HubSpot;

/// <summary>
/// Überträgt einen unterschriebenen Reonic-Auftrag an den HubSpot-Deal (docs/konzept/reonic-hubspot-auftrag.md):
/// je Position ein Artikel (Line Item, Bruttopreis je Stück), die <c>Auftrag*</c>-Properties und das unterschriebene PDF.
/// Ziel ist immer der Deal (<see cref="DealObjectTypeId"/>), weil die ID aus <c>integrations.hubspotDealId</c> stammt –
/// unabhängig von <see cref="HubSpotOptions.ObjectTypeId"/>.
/// </summary>
/// <remarks>
/// Schreibt nur mit <see cref="HubSpotOptions.WriteEnabled"/>; sonst Vorschau (gelesen werden Deal und Artikel-Verknüpfungen).
/// Idempotenz: Artikel über <see cref="HubSpotWriteOnce"/> (<see cref="LineItemKey"/>), PDF über den Ablage-Schlüssel
/// <c>order-pdf:&lt;signatur&gt;</c>. Vor dem ersten Write beansprucht die Signatur den Deal im Ledger (<see cref="OrderKey"/> =
/// Signatur-ID, Anspruch statt Abschluss): eine andere Signatur bzw. ein anderes Projekt am selben Deal schreibt danach nichts,
/// auch wenn die erste Übertragung abgebrochen ist, und legt nur eine Aufgabe an. Nichts wird gelöscht oder ersetzt.
/// Ein dauerhaft abgelehnter PATCH der Auftrags-Properties (4xx außer 429) alarmiert einmal und blockiert den Rest nicht.
/// Logs enthalten nur Deal-ID, Signatur-ID, Anzahlen und Property-Namen – nie Namen, Preise, Texte, Dateinamen.
/// </remarks>
public sealed class HubSpotOrderSink(
    HubSpotApiClient api,
    HubSpotWriteOnce once,
    ICrmWriteLedger ledger,
    HubSpotTaskSink tasks,
    HubSpotDocumentArchive archive,
    IOperatorAlert alert,
    IOptions<HubSpotOptions> options,
    ILogger<HubSpotOrderSink> logger) : ICrmOrderSink
{
    public const string DealObjectTypeId = "0-3";
    public const string LineItemObjectType = "line_items";
    public const string WriteType = "Artikel";
    public const string PropertiesWriteType = "Auftrag-Properties";

    public const string SecondSignatureTaskTitle = "Zweite Unterschrift in Reonic – Auftrag nicht übernommen";
    public const string ForeignItemsTaskTitle = "Artikel am Deal prüfen (nicht vom PV Autopilot)";

    public static string OrderKey(string dealId) => $"order:{dealId}";
    public static string LineItemKey(string dealId, string signatureRequestId, int index) =>
        $"lineitem:{dealId}:{signatureRequestId}:{index.ToString(CultureInfo.InvariantCulture)}";
    public static string PdfArchiveKey(string signatureRequestId) => $"order-pdf:{signatureRequestId}";
    public static string SecondSignatureTaskKey(string signatureRequestId) => $"order-second-signature:{signatureRequestId}";
    public static string ForeignItemsTaskKey(string signatureRequestId) => $"order-foreign-items:{signatureRequestId}";
    public static string PatchAlertKey(string signatureRequestId) => $"alert:order-patch:{signatureRequestId}";

    /// <summary>Aufgabe zur Auftragsübernahme, immer am Deal <c>0-3</c>; vorher wird geprüft (lesend), ob der Deal existiert.</summary>
    public async Task<bool> CreateOrderTaskAsync(string dealId, string taskKey, string title, string body, CancellationToken ct)
    {
        if (await api.GetObjectAsync(DealObjectTypeId, dealId, [], ct) is null)
        {
            logger.LogWarning("Aufgabe {WriteKey} nicht angelegt: HubSpot-Deal {DealId} nicht gefunden", taskKey, dealId);
            return false;
        }
        // Ohne WriteEnabled nur Debug-Log (HubSpotTaskSink).
        await tasks.CreateTaskAsync(DealObjectTypeId, dealId, taskKey, title, body, ct);
        return true;
    }

    public async Task<OrderPublishResult> PublishAsync(SignedOrder order, byte[]? signedPdf, CancellationToken ct)
    {
        var dealId = order.CrmProjectId?.Trim();
        var signature = order.SignatureRequestId?.Trim();
        if (string.IsNullOrEmpty(dealId) || string.IsNullOrEmpty(signature))
            throw new ArgumentException("Auftrag ohne Deal-ID oder Signatur-ID kann nicht übertragen werden.", nameof(order));

        var o = options.Value;
        logger.LogInformation("Auftragsübernahme nach HubSpot-Deal {DealId} gestartet: Signatur {SignatureRequestId}, {PositionCount} Positionen",
            dealId, signature, order.Positions.Count);

        // Lesend, auch ohne WriteEnabled: Deal muss existieren.
        if (await api.GetObjectAsync(DealObjectTypeId, dealId, [], ct) is null)
        {
            logger.LogWarning("Auftragsübernahme: HubSpot-Deal {DealId} nicht gefunden (Signatur {SignatureRequestId})", dealId, signature);
            return new OrderPublishResult { SkipReason = OrderSkipReasons.DealNotFound };
        }

        var properties = OrderPropertyMapper.Map(order, o);
        var propertyNames = properties.Keys.Order(StringComparer.Ordinal).ToList();

        // Deal beanspruchen, bevor irgendetwas geschrieben wird. Ohne WriteEnabled nur nachsehen.
        var claim = await ledger.FindAsync(OrderKey(dealId), ct);
        if (claim is null && o.WriteEnabled && !await ledger.RecordAsync(OrderKey(dealId), signature, CancellationToken.None))
            claim = await ledger.FindAsync(OrderKey(dealId), ct);

        // Zweite, andere Unterschrift (oder anderes Projekt) zum selben Deal: nichts schreiben, nur Aufgabe.
        if (claim is { } previous && previous.ExternalId != signature)
        {
            logger.LogWarning("Auftragsübernahme für Deal {DealId}: Signatur {SignatureRequestId} abweichend von der übertragenen {PreviousSignatureRequestId}, nichts geschrieben",
                dealId, signature, previous.ExternalId);
            await tasks.CreateTaskAsync(DealObjectTypeId, dealId, SecondSignatureTaskKey(signature), SecondSignatureTaskTitle,
                "In Reonic wurde zu diesem Deal eine weitere Unterschrift gemeldet. Der Autopilot hat die Daten der ersten Unterschrift "
                + "übernommen und nichts ersetzt. Bitte Artikel und Auftragsdaten am Deal prüfen und gegebenenfalls von Hand anpassen.", ct);
            return new OrderPublishResult
            {
                SkipReason = OrderSkipReasons.SecondSignature, LineItemCount = order.Positions.Count,
                PropertyNames = propertyNames, PdfStored = signedPdf is not null,
            };
        }

        var foreign = await CountForeignLineItemsAsync(dealId, signature, order.Positions.Count, ct);

        if (!o.WriteEnabled)
        {
            logger.LogDebug("HubSpot-{WriteType} für Deal {DealId} übersprungen (WriteEnabled=false): Signatur {SignatureRequestId}, "
                + "{LineItemCount} Artikel, Properties {PropertyNames}, PDF {HasPdf}, {ForeignLineItems} fremde Artikel",
                WriteType, dealId, signature, order.Positions.Count, propertyNames, signedPdf is not null, foreign);
            return new OrderPublishResult
            {
                SkipReason = OrderSkipReasons.WriteDisabled, LineItemCount = order.Positions.Count,
                PropertyNames = propertyNames, PdfStored = signedPdf is not null, ForeignLineItems = foreign,
            };
        }

        if (foreign > 0)
        {
            logger.LogInformation("Deal {DealId} hat {ForeignLineItems} Artikel, die nicht vom Autopiloten stammen: Aufgabe", dealId, foreign);
            await tasks.CreateTaskAsync(DealObjectTypeId, dealId, ForeignItemsTaskKey(signature), ForeignItemsTaskTitle,
                $"Am Deal hängen {foreign} Artikel, die nicht aus der Reonic-Unterschrift stammen (z. B. aus der bisherigen Übertragung). "
                + "Der Autopilot hat sie nicht verändert und die Artikel des unterschriebenen Angebots zusätzlich angelegt. Bitte Dubletten von Hand entfernen.",
                ct);
        }

        for (var i = 0; i < order.Positions.Count; i++)
        {
            var position = order.Positions[i];
            var index = i;
            await once.RunAsync(WriteType, dealId, LineItemKey(dealId, signature, index), token =>
                api.CreateAssociatedObjectAsync(LineItemObjectType, LineItemProperties(position, index), DealObjectTypeId, dealId, token), ct);
        }
        logger.LogInformation("Auftragsübernahme Deal {DealId}: {LineItemCount} Artikel angelegt bzw. vorhanden", dealId, order.Positions.Count);

        var propertiesRejected = false;
        if (properties.Count > 0)
        {
            try
            {
                await api.UpdateObjectAsync(DealObjectTypeId, dealId, properties, ct);
                logger.LogInformation("HubSpot-{WriteType} für Deal {DealId} geschrieben: {PropertyNames}", PropertiesWriteType, dealId, propertyNames);
            }
            catch (HttpRequestException ex) when (HubSpotStatusSink.IsPermanent(ex.StatusCode))
            {
                propertiesRejected = true;
                await PatchRejectedAsync(dealId, signature, (int)ex.StatusCode!.Value, propertyNames, ct);
            }
        }
        else
            logger.LogDebug("HubSpot-{WriteType} für Deal {DealId} übersprungen: keine Auftrag-Property konfiguriert", PropertiesWriteType, dealId);

        if (signedPdf is not null)
        {
            await archive.StoreAsync(DealObjectTypeId, dealId,
                [new GeneratedDocument(PdfFileName(order, o), "application/pdf", signedPdf, PdfArchiveKey(signature))], ct);
            logger.LogInformation("Auftragsübernahme Deal {DealId}: unterschriebenes PDF abgelegt (Signatur {SignatureRequestId})", dealId, signature);
        }

        logger.LogInformation("Auftragsübernahme nach HubSpot-Deal {DealId} abgeschlossen: Signatur {SignatureRequestId}", dealId, signature);
        return new OrderPublishResult
        {
            Written = true, LineItemCount = order.Positions.Count, PropertyNames = propertyNames,
            PdfStored = signedPdf is not null, ForeignLineItems = foreign, PropertiesRejected = propertiesRejected,
        };
    }

    /// <summary>
    /// 4xx außer 429 (z. B. Property-Name falsch, Wert passt nicht zum Property-Typ): Wiederholen hilft nicht. Error-Log und einmal
    /// je Signatur ein Alarm (<see cref="PatchAlertKey"/>), danach geht die Übertragung weiter.
    /// </summary>
    private async Task PatchRejectedAsync(string dealId, string signature, int statusCode, IReadOnlyList<string> propertyNames, CancellationToken ct)
    {
        logger.LogError("HubSpot-{WriteType} für Deal {DealId} (Signatur {SignatureRequestId}) dauerhaft abgelehnt: HTTP {StatusCode}",
            PropertiesWriteType, dealId, signature, statusCode);

        var key = PatchAlertKey(signature);
        if (await ledger.FindAsync(key, ct) is not null)
        {
            logger.LogDebug("Alarm {WriteKey} schon gesendet", key);
            return;
        }
        // Nur IDs, Statuscode und Property-Namen, keine Werte.
        await alert.SendAsync($"HubSpot: Auftrags-Properties für Deal {dealId} abgelehnt",
            $"PATCH der Auftrags-Properties am Deal {dealId} (Signatur {signature}): HTTP {statusCode}. Artikel und PDF wurden übertragen. "
            + $"Bitte HubSpot:Properties (Auftrag*) und die Property-Typen (Datum, Zahl, Text, Checkbox) prüfen: {string.Join(", ", propertyNames)}.",
            null, ct);
        await ledger.RecordAsync(key, "alert", CancellationToken.None);
    }

    /// <summary>Verknüpfte Artikel des Deals minus die eigenen dieser Signatur (laut Ledger).</summary>
    private async Task<int> CountForeignLineItemsAsync(string dealId, string signature, int positionCount, CancellationToken ct)
    {
        var associated = await api.GetAssociatedIdsAsync(DealObjectTypeId, dealId, LineItemObjectType, ct);
        if (associated.Count == 0)
            return 0;

        var own = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < positionCount; i++)
        {
            if (await ledger.FindAsync(LineItemKey(dealId, signature, i), ct) is { } record)
                own.Add(record.ExternalId);
        }
        return associated.Count(id => !own.Contains(id));
    }

    /// <summary>Artikel-Properties: Name, Menge, Bruttopreis je Stück, Artikelnummer, Langtext, Position.</summary>
    public static Dictionary<string, string> LineItemProperties(OrderPosition position, int index)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = position.DisplayName,
            ["quantity"] = position.Quantity.ToString(CultureInfo.InvariantCulture),
            ["hs_position_on_quote"] = index.ToString(CultureInfo.InvariantCulture),
        };
        if (position.UnitPriceGross is { } gross)
            properties["price"] = gross.ToString(CultureInfo.InvariantCulture);
        if (position.ArticleNumber is { Length: > 0 } sku)
            properties["hs_sku"] = sku;
        if (position.Description is { Length: > 0 } description)
            properties["description"] = description;
        return properties;
    }

    /// <summary>„Auftrag unterschrieben &lt;yyyy-MM-dd&gt;.pdf“ (Kalendertag in der Instanz-Zeitzone), ohne Kundennamen.</summary>
    public static string PdfFileName(SignedOrder order, HubSpotOptions options) =>
        order.SignedAt is { } at
            ? $"Auftrag unterschrieben {OrderPropertyMapper.SignedDay(at, options.ResolveTimeZone()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf"
            : "Auftrag unterschrieben.pdf";
}
