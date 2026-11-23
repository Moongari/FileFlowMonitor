using FileFlowMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FileFlowMonitor.Api.Endpoints;

public static class MonitoringAdminEndpoints
{
    public static IEndpointRouteBuilder MapMonitoringAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/monitoring/reset", async (FileFlowMonitor.Infrastructure.Monitoring.MonitoringEngineCycle cycle, CancellationToken ct) =>
        {
            await cycle.ResetDataAsync(ct);
            return Results.Ok(new { message = "Toutes les données de monitoring ont été supprimées.", resetAtUtc = DateTimeOffset.UtcNow });
        })
        .WithTags("Administration")
        .WithName("ResetMonitoringData")
        .WithSummary("Réinitialiser toutes les données FileFlowMonitor")
        .WithDescription("Supprime toutes les règles, horaires, fichiers reçus, occurrences attendues, incidents et historiques. La structure SQLite est conservée. Action destructive à confirmer dans l'interface.")
        .Produces(StatusCodes.Status200OK);
        return endpoints;
    }
}
