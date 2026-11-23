using FileFlowMonitor.Domain.Entities;
using FileFlowMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FileFlowMonitor.Api.Endpoints;

public sealed record UpdateIncidentWorkflowRequest(
    string? Status,
    string? Severity,
    string? AssignedTo,
    string? ResolutionComment);

/// <summary>Gestion opérationnelle et historique des incidents de surveillance.</summary>
public static class MonitoringIncidentEndpoints
{
    public static IEndpointRouteBuilder MapMonitoringIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/monitoring/incidents").WithTags("Incidents");

        group.MapPatch("/{id:guid}/workflow", async (
            Guid id, UpdateIncidentWorkflowRequest request, FileFlowDbContext db, CancellationToken ct) =>
        {
            var incident = await db.MonitoringIncidents.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (incident is null) return Results.NotFound(new { message = "Incident introuvable." });
            if (request.Status is null && request.Severity is null &&
                request.AssignedTo is null && request.ResolutionComment is null)
                return Results.BadRequest(new { message = "Au moins un champ doit être fourni." });

            var workflow = await db.MonitoringIncidentWorkflows.SingleOrDefaultAsync(x => x.MonitoringIncidentId == id, ct);
            var now = DateTimeOffset.UtcNow;
            if (workflow is null)
            {
                workflow = new MonitoringIncidentWorkflow(id, incident.OccurredAtUtc);
                db.MonitoringIncidentWorkflows.Add(workflow);
                db.MonitoringIncidentHistories.Add(new MonitoringIncidentHistory(id, "WorkflowCreated", "Initialisation du suivi opérateur.", now));
            }

            var changes = new List<string>();
            if (request.Status is not null) changes.Add($"Statut demandé : {request.Status}");
            if (request.Severity is not null) changes.Add($"Priorité demandée : {request.Severity}");
            if (request.AssignedTo is not null) changes.Add($"Assignation : {(string.IsNullOrWhiteSpace(request.AssignedTo) ? "retirée" : request.AssignedTo.Trim())}");
            if (request.ResolutionComment is not null) changes.Add("Commentaire de résolution mis à jour");

            try
            {
                workflow.Update(request.Status, request.Severity, request.AssignedTo, request.ResolutionComment, now);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["workflow"] = [ex.Message] });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }

            db.MonitoringIncidentHistories.Add(new MonitoringIncidentHistory(
                id, "WorkflowUpdated", string.Join(" · ", changes), now));
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                incidentId = id, workflow.Status, workflow.Severity, workflow.AssignedTo,
                workflow.AcknowledgedAtUtc, workflow.ResolvedAtUtc, workflow.ClosedAtUtc,
                workflow.ResolutionComment, workflow.CreatedAtUtc, workflow.UpdatedAtUtc
            });
        })
        .WithName("UpdateMonitoringIncidentWorkflow")
        .WithSummary("Mettre à jour le suivi opérationnel d'un incident")
        .WithDescription("""
            Modifie le statut (Open, InProgress, Resolved, Closed), la priorité (Low, Medium, High, Critical),
            l'assignation et le commentaire de résolution. Les champs omis restent inchangés.
            Envoyer une chaîne vide pour retirer l'assignation ou effacer le commentaire.
            Un incident Closed doit être rouvert avant toute nouvelle transition.
            """)
        .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict).ProducesValidationProblem();

        group.MapGet("/{id:guid}/history", async (Guid id, FileFlowDbContext db, CancellationToken ct) =>
        {
            var exists = await db.MonitoringIncidents.AnyAsync(x => x.Id == id, ct);
            if (!exists) return Results.NotFound(new { message = "Incident introuvable." });
            var history = await db.MonitoringIncidentHistories.AsNoTracking()
                .Where(x => x.MonitoringIncidentId == id)
                .OrderByDescending(x => x.OccurredAtUtc)
                .Select(x => new { x.Id, x.Action, x.Details, x.OccurredAtUtc })
                .ToListAsync(ct);
            return Results.Ok(history);
        })
        .WithName("GetMonitoringIncidentHistory")
        .WithSummary("Consulter l'historique des actions d'un incident")
        .WithDescription("Retourne les changements de statut, de priorité et d'assignation enregistrés pour l'incident.")
        .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
