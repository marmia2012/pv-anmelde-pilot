using PvAutopilot.Sources.HubSpot;
using PvAutopilot.Core.Processes;

namespace PvAutopilot.Host.Endpoints;

public static class HubSpotWebhookEndpoints
{
    public static void MapHubSpotWebhooks(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/hubspot", async (
            HttpRequest request,
            HubSpotWebhookValidator validator,
            HubSpotTriggerEvaluator evaluator,
            ProcessTriggerHandler handler,
            CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var uri = $"{request.Scheme}://{request.Host}{request.Path}{request.QueryString}";

            if (!validator.IsValid(request.Method, uri, body,
                    request.Headers["X-HubSpot-Signature-v3"], request.Headers["X-HubSpot-Request-Timestamp"]))
                return Results.Unauthorized();

            IReadOnlyList<ProcessTrigger> triggers;
            try
            {
                triggers = evaluator.Evaluate(body);
            }
            catch (System.Text.Json.JsonException)
            {
                // 400 statt 500: HubSpot würde einen 5xx sonst tagelang wiederholen.
                return Results.BadRequest();
            }

            // Immer 200, auch bei Ignored/NotFound: ein Fehlercode ließe HubSpot das Ereignis tagelang wiederholen,
            // am Ergebnis würde das nichts ändern. Der Handler loggt ignorierte Ereignisse selbst.
            var results = new List<TriggerResult>(triggers.Count);
            foreach (var trigger in triggers)
                results.Add(await handler.HandleAsync(trigger, ct));

            return Results.Ok(new
            {
                applied = results.Count(r => r == TriggerResult.Applied),
                ignored = results.Count(r => r == TriggerResult.Ignored),
                notFound = results.Count(r => r == TriggerResult.NotFound),
            });
        });
    }
}
