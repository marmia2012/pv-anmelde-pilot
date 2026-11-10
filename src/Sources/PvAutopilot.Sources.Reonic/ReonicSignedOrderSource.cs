using Microsoft.Extensions.Logging;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Orders;
using PvAutopilot.Sources.Reonic.Api;
using PvAutopilot.Sources.Reonic.Mapping;

namespace PvAutopilot.Sources.Reonic;

/// <summary>
/// Liest den unterschriebenen Auftrag eines Reonic-Projekts (docs/konzept/reonic-hubspot-auftrag.md):
/// <list type="number">
/// <item>Projekt → <c>integrations.hubspotDealId</c></item>
/// <item>Signaturanfragen → die gegebene, sonst die jüngste mit <c>status = signed</c></item>
/// <item>Variante <c>signedVariantId</c> → alle Positionen (alle Systeme + gewählte Zusatzpakete)</item>
/// <item>Katalog der relevanten Komponenten → Zusammenfassung über <see cref="ComponentMapper"/></item>
/// </list>
/// Rein lesend. Fachliche Lücken landen ohne Werte in <see cref="SignedOrder.Problems"/>.
/// </summary>
public sealed class ReonicSignedOrderSource(
    ReonicApiClient api,
    ReonicFileDownloader downloader,
    ILogger<ReonicSignedOrderSource> logger) : ISignedOrderSource
{
    public const string DocumentSource = "reonic-signature";
    public const string SignedPdfFileName = "Auftrag unterschrieben.pdf";

    public async Task<SignedOrder?> GetSignedOrderAsync(string planningProjectId, string? signatureRequestId, CancellationToken ct)
    {
        var requestedId = string.IsNullOrWhiteSpace(signatureRequestId) ? null : signatureRequestId.Trim();
        logger.LogInformation("Reonic-Auftrag für Projekt {ReonicProjectId} wird gelesen (Signatur {SignatureRequestId})",
            planningProjectId, requestedId);

        var project = await api.GetProjectAsync(planningProjectId, ct);
        if (project is null)
        {
            logger.LogInformation("Reonic-Projekt {ReonicProjectId} nicht gefunden", planningProjectId);
            return null;
        }

        var dealId = string.IsNullOrWhiteSpace(project.Integrations?.HubspotDealId) ? null : project.Integrations!.HubspotDealId!.Trim();
        var problems = new List<string>();
        var requests = await api.GetSignatureRequestsAsync(project.Id, ct);

        var signature = requestedId is not null
            ? requests.FirstOrDefault(r => r.Id == requestedId)
            : requests.Where(r => r.IsSigned).OrderByDescending(r => r.SignedAt ?? DateTimeOffset.MinValue).FirstOrDefault();

        SignedOrder End(SignedOrder order)
        {
            logger.LogInformation(
                "Reonic-Auftrag für Projekt {ReonicProjectId} gelesen: Signatur {SignatureRequestId}, unterschrieben {IsSigned}, "
                + "{PositionCount} Positionen, {ProblemCount} Probleme",
                project.Id, order.SignatureRequestId, order.IsSigned, order.Positions.Count, order.Problems.Count);
            return order;
        }

        if (signature is null)
        {
            problems.Add(requestedId is not null
                ? "Reonic: Signaturanfrage nicht gefunden"
                : "Reonic: keine unterschriebene Signaturanfrage am Projekt");
            return End(new SignedOrder
            {
                PlanningProjectId = project.Id, SignatureRequestId = requestedId, CrmProjectId = dealId, Problems = problems,
            });
        }

        if (!signature.IsSigned)
        {
            // Der Status ist ein fester Wert der API (kein Freitext).
            problems.Add($"Reonic: Signaturanfrage hat Status „{signature.Status ?? "leer"}“ statt „{ReonicSignatureRequest.StatusSigned}“");
            return End(new SignedOrder
            {
                PlanningProjectId = project.Id, SignatureRequestId = signature.Id, CrmProjectId = dealId,
                SignedAt = signature.SignedAt, Problems = problems,
            });
        }

        DocumentRef? pdf = null;
        if (string.IsNullOrWhiteSpace(signature.SignedPdfUrl))
            problems.Add("Reonic: kein unterschriebenes Auftrags-PDF (signedPdfUrl)");
        else
            pdf = new DocumentRef(DocumentKind.Auftrag, DocumentSource, signature.SignedPdfUrl, SignedPdfFileName);

        var variant = await LoadVariantAsync(project.Id, signature, problems, ct);
        var positions = new List<OrderPosition>();
        var summary = OrderSummary.Empty;
        if (variant is not null)
        {
            var lineItems = variant.SelectedLineItems.ToList();
            foreach (var item in lineItems)
            {
                var position = new OrderPosition(
                    item.ComponentType ?? string.Empty,
                    item.Brand?.Trim() ?? string.Empty,
                    item.Name?.Trim() ?? string.Empty,
                    string.IsNullOrWhiteSpace(item.ArticleNumber) ? null : item.ArticleNumber.Trim(),
                    string.IsNullOrWhiteSpace(item.Description) ? null : item.Description.Trim(),
                    item.Quantity,
                    item.UnitSalesPrice?.Gross,
                    item.UnitSalesPrice?.VatRate);
                if (position.UnitPriceGross is null)
                    problems.Add($"Reonic-Angebot: Position „{position.DisplayName}“ ohne Bruttopreis");
                positions.Add(position);
            }

            var components = await ComponentLoader.LoadAsync(api, lineItems, problems, ct);
            var componentData = ComponentMapper.Map(components);
            // Fehlende Datenblätter und Grenzen der Netzanmeldung sind für die Auftragsübernahme ohne Belang.
            problems.AddRange(componentData.ProblemDetails
                .Where(p => p.Kind is not (ComponentProblemKind.MissingDatasheet or ComponentProblemKind.NotYetSupportedForNetzanmeldung))
                .Select(p => p.Text));
            summary = new OrderSummary(componentData.Module, componentData.Wechselrichter, componentData.Speicher, componentData.Wallbox);
        }

        return End(new SignedOrder
        {
            PlanningProjectId = project.Id,
            SignatureRequestId = signature.Id,
            IsSigned = true,
            CrmProjectId = dealId,
            SignedAt = signature.SignedAt,
            VariantId = variant?.Id,
            Positions = positions,
            Summary = summary,
            SignedPdf = pdf,
            Problems = problems,
        });
    }

    public async Task<byte[]?> DownloadAsync(DocumentRef document, CancellationToken ct)
    {
        var content = await downloader.DownloadAsync(document.Reference, ct);
        // Nie URL oder Dateinamen loggen.
        if (content is null)
            logger.LogWarning("Reonic-Dokument {DocumentKind} nicht abrufbar (keine https-URL, abgelaufen, fehlt oder größer als {MaxBytes} Bytes)",
                document.Kind, ReonicFileDownloader.MaxBytes);
        else
            logger.LogInformation("Reonic-Dokument {DocumentKind} geladen: {ByteCount} Bytes", document.Kind, content.Length);
        return content;
    }

    private async Task<ReonicVariant?> LoadVariantAsync(
        string projectId, ReonicSignatureRequest signature, List<string> problems, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(signature.SignedVariantId))
        {
            problems.Add("Reonic: Signaturanfrage ohne unterschriebene Variante (signedVariantId)");
            return null;
        }

        var variant = await api.GetVariantAsync(projectId, signature.SignedVariantId, ct);
        if (variant is null)
            problems.Add("Reonic: unterschriebene Variante nicht gefunden");
        return variant;
    }
}
