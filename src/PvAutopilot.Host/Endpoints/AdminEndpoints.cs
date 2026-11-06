using System.Security.Cryptography;
using System.Text;
using PvAutopilot.Core.Notifications;
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

        admin.MapGet("/processes/{id:guid}",async (Guid id, IProcessStore store, CancellationToken ct) =>
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
