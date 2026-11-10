using System.Security.Cryptography;
using System.Text;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Orders;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;
using PvAutopilot.Infrastructure.Jobs;
using PvAutopilot.Infrastructure.Persistence;
using PvAutopilot.Sources.HubSpot;

namespace PvAutopilot.Host.Endpoints;

/// <summary>
/// Betriebs-Endpunkte. Geschützt über Header <c>X-Admin-Key</c> = <c>Admin:ApiKey</c>;
/// ohne konfigurierten Key sind sie gesperrt. Antworten enthalten keine personenbezogenen Daten.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").AddEndpointFilter(RequireAdminKey);

        admin.MapPut("/netzbetreiber-plz", async (HttpRequest request, NetzbetreiberPlzImporter importer, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            try
            {
                var count = await importer.ReplaceAllAsync(reader, ct);
                return Results.Ok(new { imported = count });
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Pipelines/Stages und Properties des konfigurierten Objekttyps, um instance.json zu füllen. Nur Metadaten.
        admin.MapGet("/hubspot/structure", async (HubSpotStructureReader reader, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await reader.ReadAsync(ct));
            }
            catch (HttpRequestException ex)
            {
                // Message enthält nur Pfad und Status (HubSpotApiClient), keinen Antwort-Body.
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        // Auftragsübernahme Reonic → HubSpot manuell anstoßen bzw. nachholen; bei HubSpot:WriteEnabled=false nur Vorschau.
        admin.MapPost("/reonic/projects/{projectId}/order-sync", (string projectId, string? signatureRequestId,
                OrderTransferService service, IProcessRunLock runLock, CancellationToken ct) =>
            OrderSyncAsync(projectId, signatureRequestId, service, runLock, ct));

        admin.MapGet("/processes/{id:guid}", async (Guid id, IProcessStore store, CancellationToken ct) =>
            await store.GetAsync(id, ct) is { } p
                ? Results.Ok(new
                {
                    p.Id, p.CrmProjectId, State = p.State.ToString(), p.StateChangedAt, p.Revision,
                    p.NetzbetreiberCode, BlockedFrom = p.BlockedFrom?.ToString(), p.BlockReason,
                    ApprovedSubmission = p.ApprovedSubmission?.ToString(), p.ReviewApprovedAt,
                    // Fakten aus Ereignissen; bewusst ohne Datenkarte (personenbezogene Daten).
                    p.NetzbetreiberReferenz, p.EinspeisezusageDatum, p.RueckfrageOffenSeit,
                    RueckfrageErledigung = p.RueckfrageErledigung?.ToString(), p.RueckfrageGeschlossenAm,
                    p.Inbetriebnahmedatum, p.ZaehlerwechselDatum, p.MastrNummerAnlage, p.MastrNummerSpeicher,
                    p.CancellationRequestedAt, p.CreatedAt, p.UpdatedAt,
                    Transitions = p.Transitions.Select(t => new { From = t.From.ToString(), To = t.To.ToString(), t.At, t.Note }),
                })
                : Results.NotFound());

        admin.MapPost("/processes/{id:guid}/events", (Guid id, HttpRequest request, IProcessStore store,
                ProcessTriggerHandler handler, TimeProvider time, CancellationToken ct) =>
            AdminProcessEvents.PostAsync(id, request.Body, store, handler, time, ct));

        admin.MapPost("/processes/{id:guid}/submissions/{kind}/resolve", (Guid id, string kind, HttpRequest request,
                IProcessStore store, ISubmissionLedger ledger, ProcessTriggerHandler handler, AuditLog audit, IProcessRunLock runLock,
                CancellationToken ct) =>
            AdminSubmissionResolution.PostAsync(id, kind, request.Body, store, ledger, handler, audit, runLock, ct));

        admin.MapPost("/processes/{id:guid}/resume", async (Guid id, ProcessTriggerHandler handler, CancellationToken ct) =>
            await handler.ResumeAsync(id, ct) switch
            {
                ResumeResult.NotFound => Results.NotFound(),
                ResumeResult.NotBlocked => Results.Conflict(new { error = "Vorgang ist nicht blockiert." }),
                _ => Results.Accepted(),
            });

        admin.MapPost("/processes/{id:guid}/run", async (Guid id, IProcessStore store, IProcessScheduler scheduler, CancellationToken ct) =>
        {
            if (await store.GetAsync(id, ct) is null)
                return Results.NotFound();
            scheduler.Enqueue(id);
            return Results.Accepted();
        });
    }

    /// <summary>
    /// Führt die Übernahme synchron aus. Dasselbe Lock wie der Job (<see cref="OrderTransferJob.LockId"/>), damit Webhook-Job
    /// und manueller Aufruf nicht parallel Artikel anlegen: belegt → 409. Technischer Fehler → 502 mit Pfad und Status.
    /// Antwort ohne Werte (nur IDs, Anzahlen, Property-Namen, Probleme mit Produktnamen).
    /// </summary>
    public static async Task<IResult> OrderSyncAsync(string projectId, string? signatureRequestId,
        OrderTransferService service, IProcessRunLock runLock, CancellationToken ct)
    {
        var project = projectId?.Trim();
        if (string.IsNullOrEmpty(project))
            return Results.BadRequest(new { error = "projectId fehlt." });
        // Leere Signatur = keine: dann die jüngste unterschriebene.
        var signature = string.IsNullOrWhiteSpace(signatureRequestId) ? null : signatureRequestId.Trim();

        await using var held = await runLock.TryAcquireAsync(OrderTransferJob.LockId(project), OrderTransferJob.LockWait, ct);
        if (held is null)
            return Results.Conflict(new { error = "Auftragsübernahme für dieses Projekt läuft bereits." });

        try
        {
            return Results.Ok(await service.TransferAsync(project, signature, ct));
        }
        catch (HttpRequestException ex)
        {
            // Message enthält nur Pfad und Status (Api-Clients), nie Antwort-Body oder signierte URL.
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (SignatureNotVisibleException ex)
        {
            // Nur Projekt- und Signatur-ID in der Meldung.
            return Results.Conflict(new { error = ex.Message });
        }
    }

    private static async ValueTask<object?> RequireAdminKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var expected = http.RequestServices.GetRequiredService<IConfiguration>()["Admin:ApiKey"];
        var provided = http.Request.Headers["X-Admin-Key"].ToString();

        if (string.IsNullOrEmpty(expected)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided)))
            return Results.Unauthorized();

        return await next(context);
    }
}
