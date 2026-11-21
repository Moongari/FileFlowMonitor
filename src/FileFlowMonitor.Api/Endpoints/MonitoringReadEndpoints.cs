using FileFlowMonitor.Domain.Enums;
using FileFlowMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FileFlowMonitor.Api.Endpoints;

/// <summary>Expose les indicateurs de supervision et les données opérationnelles en lecture seule.</summary>
public static class MonitoringReadEndpoints
{
    public static IEndpointRouteBuilder MapMonitoringReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/monitoring").WithTags("Monitoring");

        group.MapGet("/overview", async (FileFlowDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
            var rules = await db.MonitoringRules.AsNoTracking().Include(x => x.ScheduleTimes).ToListAsync(ct);
            var dayEnd = dayStart.AddDays(1);
            var expected = await db.ExpectedFiles.AsNoTracking()
                .Where(x => x.ExpectedAtUtc >= dayStart && x.ExpectedAtUtc < dayEnd).ToListAsync(ct);
            var incidents = await db.MonitoringIncidents.AsNoTracking()
                .Where(x => x.ResolvedAtUtc == null).OrderByDescending(x => x.OccurredAtUtc)
                .Take(10).ToListAsync(ct);
            var executions = await db.MonitoringExecutions.AsNoTracking()
                .OrderByDescending(x => x.StartedAtUtc).Take(10).ToListAsync(ct);

            return Results.Ok(new
            {
                generatedAtUtc = now,
                rulesTotal = rules.Count,
                rulesEnabled = rules.Count(x => x.IsEnabled),
                expectedToday = expected.Count,
                receivedToday = expected.Count(x => x.Status == ExpectedFileStatus.Received),
                lateToday = expected.Count(x => x.Status == ExpectedFileStatus.Late),
                missingToday = expected.Count(x => x.Status == ExpectedFileStatus.Missing),
                invalidToday = expected.Count(x => x.Status == ExpectedFileStatus.Invalid),
                openIncidents = await db.MonitoringIncidents.CountAsync(x => x.ResolvedAtUtc == null, ct),
                rules = rules.OrderBy(x => x.Name).Select(x => new
                {
                    x.Id, x.Name, x.FolderPath, x.IsEnabled, x.GracePeriodMinutes,
                    scheduleTimes = x.ScheduleTimes.Select(t => t.ExpectedTime).OrderBy(t => t).ToArray()
                }),
                latestIncidents = incidents.Select(x => new
                {
                    x.Id, x.MonitoringRuleId, x.Code, x.Message, x.OccurredAtUtc
                }),
                latestExecutions = executions.Select(x => new
                {
                    x.Id, x.MonitoringRuleId, x.StartedAtUtc, x.CompletedAtUtc,
                    x.FilesDiscovered, x.Error
                })
            });
        })
        .WithName("GetMonitoringOverview")
        .WithSummary("Vue d'ensemble de la supervision")
        .WithDescription("Retourne les indicateurs du jour, l'état des règles, les incidents ouverts et les dernières exécutions. Exemple : rulesEnabled=3, receivedToday=5, missingToday=1.")
        .Produces(StatusCodes.Status200OK);

        group.MapGet("/expected-files", async (
            FileFlowDbContext db, DateOnly? date, Guid? ruleId, ExpectedFileStatus? status, CancellationToken ct) =>
        {
            var selectedDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var start = new DateTimeOffset(selectedDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            var end = start.AddDays(1);
            var query = db.ExpectedFiles.AsNoTracking()
                .Where(x => x.ExpectedAtUtc >= start && x.ExpectedAtUtc < end);
            if (ruleId.HasValue) query = query.Where(x => x.MonitoringRuleId == ruleId.Value);
            if (status.HasValue) query = query.Where(x => x.Status == status.Value);
            var rows = await query.OrderBy(x => x.ExpectedAtUtc).Select(x => new
            {
                x.Id, x.MonitoringRuleId, x.ExpectedFileName, x.ExpectedAtUtc,
                x.DeadlineAtUtc, x.ReceivedAtUtc, status = x.Status.ToString()
            }).ToListAsync(ct);
            return Results.Ok(rows);
        })
        .WithName("GetExpectedFiles")
        .WithSummary("Consulter les fichiers attendus")
        .WithDescription("Liste les fichiers attendus pour une date. Filtres facultatifs : date (AAAA-MM-JJ), ruleId et status (Scheduled, Waiting, Missing, Received, Late, Invalid, Duplicate).")
        .Produces(StatusCodes.Status200OK);

        group.MapGet("/received-files", async (FileFlowDbContext db, int? limit, CancellationToken ct) =>
        {
            var take = Math.Clamp(limit ?? 100, 1, 500);
            var rows = await db.ReceivedFiles.AsNoTracking().OrderByDescending(x => x.DetectedAtUtc)
                .Take(take).Select(x => new
                {
                    x.Id, x.MonitoringRuleId, x.FileName, x.FullPath, x.SizeBytes,
                    x.DetectedAtUtc, x.StableAtUtc, x.LastWriteTimeUtc
                }).ToListAsync(ct);
            return Results.Ok(rows);
        })
        .WithName("GetReceivedFiles")
        .WithSummary("Consulter les fichiers détectés")
        .WithDescription("Retourne les derniers fichiers observés par le moteur, avec chemin, taille, date de détection et date de stabilisation. Paramètre limit : 1 à 500, 100 par défaut.")
        .Produces(StatusCodes.Status200OK);

        group.MapGet("/incidents", async (FileFlowDbContext db, bool? openOnly, int? limit, CancellationToken ct) =>
        {
            var query = db.MonitoringIncidents.AsNoTracking();
            if (openOnly == true) query = query.Where(x => x.ResolvedAtUtc == null);
            var rows = await query.OrderByDescending(x => x.OccurredAtUtc)
                .Take(Math.Clamp(limit ?? 100, 1, 500))
                .Select(x => new
                {
                    x.Id, x.MonitoringRuleId, x.ExpectedFileId, x.Code, x.Message,
                    x.OccurredAtUtc, x.ResolvedAtUtc, isResolved = x.ResolvedAtUtc != null
                }).ToListAsync(ct);
            return Results.Ok(rows);
        })
        .WithName("GetMonitoringIncidents")
        .WithSummary("Consulter les incidents")
        .WithDescription("Liste les incidents de surveillance. Utiliser openOnly=true pour ne voir que les incidents non résolus. Limite comprise entre 1 et 500.")
        .Produces(StatusCodes.Status200OK);

        group.MapGet("/executions", async (FileFlowDbContext db, int? limit, CancellationToken ct) =>
        {
            var rows = await db.MonitoringExecutions.AsNoTracking()
                .OrderByDescending(x => x.StartedAtUtc)
                .Take(Math.Clamp(limit ?? 100, 1, 500))
                .Select(x => new
                {
                    x.Id, x.MonitoringRuleId, x.StartedAtUtc, x.CompletedAtUtc,
                    x.FilesDiscovered, x.Error
                }).ToListAsync(ct);
            return Results.Ok(rows);
        })
        .WithName("GetMonitoringExecutions")
        .WithSummary("Consulter l'historique des cycles")
        .WithDescription("Retourne les dernières exécutions du moteur, leur durée, le nombre de fichiers détectés et les erreurs éventuelles.")
        .Produces(StatusCodes.Status200OK);

        return endpoints;
    }
}
