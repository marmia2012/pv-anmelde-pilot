using Microsoft.Extensions.Logging;
using PvAutopilot.Core.Notifications;

namespace PvAutopilot.Core.Orders;

/// <summary>
/// Überträgt einen unterschriebenen Auftrag aus der Planungssoftware ins CRM (docs/konzept/reonic-hubspot-auftrag.md).
/// Eigener Ablauf außerhalb der Anmelde-State-Machine: startet keinen Vorgang.
/// </summary>
/// <remarks>
/// Fachliche Fehler enden ohne Exception: ohne bekanntes CRM-Objekt mit einem einmaligen Alarm an den Betreiber
/// (Schlüssel <c>alert:order:…</c> im <see cref="ICrmWriteLedger"/>), sonst mit genau einer Aufgabe am Deal.
/// Technische Fehler (Quelle/CRM nicht erreichbar) werfen weiter; der Job wiederholt.
/// </remarks>
public sealed class OrderTransferService(
    ISignedOrderSource source,
    ICrmOrderSink sink,
    IHumanTaskSink tasks,
    IOperatorAlert alert,
    ICrmWriteLedger ledger,
    ILogger<OrderTransferService> logger)
{
    public const string ProblemsTaskTitle = "Auftragsübernahme aus Reonic prüfen";
    public const string NotSignedTaskTitle = "Auftragsübernahme aus Reonic: keine gültige Unterschrift";

    public static string NotFoundAlertKey(string projectId) => $"alert:order:{projectId}:not-found";
    public static string NoDealAlertKey(string projectId, string? signatureRequestId) => $"alert:order:{projectId}:{SignatureOrNone(signatureRequestId)}:no-deal";
    public static string DealNotFoundAlertKey(string projectId, string? signatureRequestId) => $"alert:order:{projectId}:{SignatureOrNone(signatureRequestId)}:deal-not-found";
    public static string ProblemsTaskKey(string? signatureRequestId) => $"order-problems:{SignatureOrNone(signatureRequestId)}";
    public static string NotSignedTaskKey(string projectId, string? signatureRequestId) => $"order-not-signed:{projectId}:{SignatureOrNone(signatureRequestId)}";

    /// <param name="signatureRequestId">Signaturanfrage aus dem Webhook; <c>null</c> = die jüngste unterschriebene.</param>
    public async Task<OrderTransferResult> TransferAsync(string planningProjectId, string? signatureRequestId, CancellationToken ct)
    {
        logger.LogInformation("Auftragsübernahme für Reonic-Projekt {ReonicProjectId} gestartet (Signatur {SignatureRequestId})",
            planningProjectId, signatureRequestId);

        var order = await source.GetSignedOrderAsync(planningProjectId, signatureRequestId, ct);
        if (order is null)
        {
            logger.LogWarning("Auftragsübernahme: Reonic-Projekt {ReonicProjectId} nicht gefunden", planningProjectId);
            await AlertOnceAsync(NotFoundAlertKey(planningProjectId),
                $"Auftragsübernahme: Reonic-Projekt {planningProjectId} nicht gefunden",
                $"Reonic-Projekt {planningProjectId} (Signatur {signatureRequestId ?? "–"}) existiert nicht. Projekt-ID im Webhook bzw. Aufruf prüfen.",
                ct);
            return End(new OrderTransferResult
            {
                PlanningProjectId = planningProjectId, SignatureRequestId = signatureRequestId,
                SkipReason = OrderSkipReasons.ProjectNotFound,
            });
        }

        var signature = order.SignatureRequestId ?? signatureRequestId;
        var dealId = string.IsNullOrWhiteSpace(order.CrmProjectId) ? null : order.CrmProjectId.Trim();
        var problems = order.Problems.ToList();
        OrderTransferResult Result(string? skipReason, OrderPublishResult? published = null) => new()
        {
            PlanningProjectId = planningProjectId,
            SignatureRequestId = signature,
            CrmProjectId = dealId,
            Written = published?.Written ?? false,
            SkipReason = skipReason ?? published?.SkipReason,
            LineItemCount = published?.LineItemCount ?? 0,
            PropertyNames = published?.PropertyNames ?? [],
            PdfStored = published?.PdfStored ?? false,
            ForeignLineItems = published?.ForeignLineItems ?? 0,
            Problems = problems,
        };

        if (dealId is null)
        {
            logger.LogWarning("Auftragsübernahme für Reonic-Projekt {ReonicProjectId}: keine HubSpot-Deal-ID am Projekt", planningProjectId);
            await AlertOnceAsync(NoDealAlertKey(planningProjectId, signature),
                $"Auftragsübernahme: Reonic-Projekt {planningProjectId} ohne HubSpot-Deal",
                $"Am Reonic-Projekt {planningProjectId} (Signatur {signature ?? "–"}) ist kein HubSpot-Deal verknüpft "
                + "(integrations.hubspotDealId). Verknüpfung in Reonic herstellen und die Übernahme über den Admin-Endpunkt nachholen.",
                ct);
            return End(Result(OrderSkipReasons.NoDeal));
        }

        if (!order.IsSigned || signature is null)
        {
            logger.LogWarning("Auftragsübernahme für Reonic-Projekt {ReonicProjectId}: Signatur {SignatureRequestId} fehlt oder ist nicht unterschrieben",
                planningProjectId, signature);
            await tasks.CreateTaskAsync(dealId, NotSignedTaskKey(planningProjectId, signature), NotSignedTaskTitle,
                ProblemText("Der Autopilot hat für dieses Reonic-Projekt keine gültige Unterschrift gefunden und nichts übernommen.", problems), ct);
            return End(Result(OrderSkipReasons.NotSigned));
        }

        if (order.VariantId is null || order.Positions.Count == 0)
        {
            // Ohne Positionen würden nur leere Werte geschrieben und die Unterschrift als übertragen markiert.
            if (order.VariantId is not null)
                problems.Add("Reonic: unterschriebene Variante ohne Positionen");
            logger.LogWarning("Auftragsübernahme für Reonic-Projekt {ReonicProjectId}, Signatur {SignatureRequestId}: keine Positionen, nichts übertragen",
                planningProjectId, signature);
            await TaskForProblemsAsync(dealId, signature, problems, ct);
            return End(Result(OrderSkipReasons.VariantMissing));
        }

        byte[]? pdf = null;
        if (order.SignedPdf is { } pdfRef)
        {
            pdf = await source.DownloadAsync(pdfRef, ct);
            if (pdf is null)
                problems.Add("Reonic: unterschriebenes Auftrags-PDF nicht abrufbar");
        }

        var published = await sink.PublishAsync(order, pdf, ct);
        if (published.DealNotFound)
        {
            await AlertOnceAsync(DealNotFoundAlertKey(planningProjectId, signature),
                $"Auftragsübernahme: HubSpot-Deal {dealId} nicht gefunden",
                $"Das Reonic-Projekt {planningProjectId} (Signatur {signature}) verweist auf den HubSpot-Deal {dealId}, den es nicht gibt. "
                + "Verknüpfung in Reonic prüfen und die Übernahme über den Admin-Endpunkt nachholen.",
                ct);
            return End(Result(null, published));
        }

        // Bei einer zweiten Unterschrift hat der Sink schon eine eigene Aufgabe angelegt und nichts geschrieben.
        if (published.SkipReason != OrderSkipReasons.SecondSignature)
            await TaskForProblemsAsync(dealId, signature, problems, ct);

        return End(Result(null, published));
    }

    private async Task TaskForProblemsAsync(string dealId, string signature, IReadOnlyList<string> problems, CancellationToken ct)
    {
        if (problems.Count == 0)
            return;
        await tasks.CreateTaskAsync(dealId, ProblemsTaskKey(signature), ProblemsTaskTitle,
            ProblemText("Die Auftragsdaten aus Reonic sind unvollständig. Bitte prüfen und in HubSpot bzw. Reonic ergänzen.", problems), ct);
    }

    private static string ProblemText(string intro, IReadOnlyList<string> problems) =>
        problems.Count == 0
            ? intro
            : intro + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(p => "• " + p));

    /// <summary>Alarm höchstens einmal je Schlüssel: Ledger prüfen → senden → festhalten.</summary>
    private async Task AlertOnceAsync(string key, string subject, string body, CancellationToken ct)
    {
        if (await ledger.FindAsync(key, ct) is not null)
        {
            logger.LogDebug("Alarm {WriteKey} schon gesendet", key);
            return;
        }
        await alert.SendAsync(subject, body, null, ct);
        await ledger.RecordAsync(key, "alert", CancellationToken.None);
    }

    private OrderTransferResult End(OrderTransferResult result)
    {
        logger.LogInformation(
            "Auftragsübernahme für Reonic-Projekt {ReonicProjectId} beendet: Signatur {SignatureRequestId}, Deal {DealId}, geschrieben {Written} "
            + "({SkipReason}), {LineItemCount} Artikel, {PropertyCount} Properties, PDF {PdfStored}, {ForeignLineItems} fremde Artikel, {ProblemCount} Probleme",
            result.PlanningProjectId, result.SignatureRequestId, result.CrmProjectId, result.Written, result.SkipReason,
            result.LineItemCount, result.PropertyNames.Count, result.PdfStored, result.ForeignLineItems, result.Problems.Count);
        return result;
    }

    private static string SignatureOrNone(string? signatureRequestId) =>
        string.IsNullOrWhiteSpace(signatureRequestId) ? "none" : signatureRequestId.Trim();
}
