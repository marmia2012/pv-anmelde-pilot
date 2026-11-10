using System.Text.Json;
using PvAutopilot.Core.Orders;
using PvAutopilot.Sources.Reonic.Webhooks;

namespace PvAutopilot.Host.Endpoints;

/// <summary>
/// <c>POST /webhooks/reonic</c>: Reonic meldet Ereignisse, ausgewertet wird nur <c>residentialOffer_signed</c>
/// (→ Job „Auftragsübernahme“). Signatur ungültig oder Secret leer → 401, ungültiges JSON → 400, sonst sofort 200.
/// Geloggt werden nur Event-Typ, Event-ID, Projekt-ID und Signatur-ID, nie der Body.
/// </summary>
public static class ReonicWebhookEndpoints
{
    public const string Path = "/webhooks/reonic";
    public const string LoggerName = "PvAutopilot.Host.Endpoints.ReonicWebhook";

    public static void MapReonicWebhooks(this IEndpointRouteBuilder app)
    {
        // Feature optional: ohne Secret kein Startabbruch, nur einmal Warning.
        if (!app.ServiceProvider.GetRequiredService<ReonicWebhookValidator>().IsEnabled)
            app.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerName)
                .LogWarning("Reonic-Webhook deaktiviert: Reonic__WebhookSecret ist nicht gesetzt, {WebhookPath} lehnt alles mit 401 ab", Path);

        app.MapPost(Path, async (HttpRequest request, ReonicWebhookValidator validator, IOrderTransferScheduler scheduler,
                ILoggerFactory loggers, CancellationToken ct) =>
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            return Handle(buffer.ToArray(), request.Headers, validator, scheduler, loggers.CreateLogger(LoggerName));
        });
    }

    /// <summary>Verarbeitung ohne HTTP-Pipeline (für Tests): Rohbody und Header rein, Ergebnis raus.</summary>
    public static IResult Handle(byte[] body, IHeaderDictionary headers, ReonicWebhookValidator validator,
        IOrderTransferScheduler scheduler, ILogger logger)
    {
        var eventId = headers[ReonicWebhookValidator.EventIdHeader].ToString();
        if (!validator.IsValid(body, headers[ReonicWebhookValidator.SignatureHeader], headers[ReonicWebhookValidator.TimestampHeader]))
        {
            logger.LogWarning("Reonic-Webhook {ReonicEventId} abgelehnt: Signatur oder Zeitstempel ungültig bzw. Webhook deaktiviert", eventId);
            return Results.Unauthorized();
        }

        ReonicWebhookEvent evt;
        try
        {
            evt = ReonicWebhookParser.Parse(body, headers[ReonicWebhookValidator.EventHeader]);
        }
        catch (JsonException)
        {
            logger.LogWarning("Reonic-Webhook {ReonicEventId} abgelehnt: kein gültiges JSON", eventId);
            return Results.BadRequest();
        }

        switch (evt.Type)
        {
            case ReonicWebhookParser.OfferSigned:
                if (evt.ProjectId is null || evt.SignatureRequestId is null)
                {
                    logger.LogWarning("Reonic-Webhook {ReonicEventType} {ReonicEventId} abgelehnt: projectId oder signatureRequestId fehlt",
                        evt.Type, eventId);
                    return Results.BadRequest();
                }
                scheduler.Enqueue(evt.ProjectId, evt.SignatureRequestId);
                logger.LogInformation("Reonic-Webhook {ReonicEventType} {ReonicEventId}: Auftragsübernahme für Projekt {ReonicProjectId}, Signatur {SignatureRequestId} eingeplant",
                    evt.Type, eventId, evt.ProjectId, evt.SignatureRequestId);
                return Results.Ok();

            case ReonicWebhookParser.SignatureWithdrawn:
                logger.LogInformation("Reonic-Webhook {ReonicEventType} {ReonicEventId} für Projekt {ReonicProjectId}, Signatur {SignatureRequestId} nicht ausgewertet",
                    evt.Type, eventId, evt.ProjectId, evt.SignatureRequestId);
                return Results.Ok();

            default:
                logger.LogDebug("Reonic-Webhook {ReonicEventType} {ReonicEventId} ignoriert", evt.Type, eventId);
                return Results.Ok();
        }
    }
}
