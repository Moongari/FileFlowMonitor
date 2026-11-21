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
            var rules = await db.MonitoringRules.AsNoTracking().Include(x => x.ScheduleTimes).ToListAsync(ct);
            var zones = rules.ToDictionary(x => x.Id, x => TimeZoneInfo.FindSystemTimeZoneById(x.TimeZoneId));
            // SQLite stores DateTimeOffset as text by default and cannot range-filter/order it.
            // Keep the existing schema and evaluate each expected item on its rule's business date.
            var expected = (await db.ExpectedFiles.AsNoTracking().ToListAsync(ct))
                .Where(x => zones.TryGetValue(x.MonitoringRuleId, out var zone) &&
                    DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.ExpectedAtUtc, zone).DateTime) ==
                    DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime))
                .ToList();
            var allIncidents = await db.MonitoringIncidents.AsNoTracking().ToListAsync(ct);
            var incidents = allIncidents.Where(x => x.ResolvedAtUtc == null)
                .OrderByDescending(x => x.OccurredAtUtc).Take(10).ToList();
            var executions = (await db.MonitoringExecutions.AsNoTracking().ToListAsync(ct))
                .OrderByDescending(x => x.StartedAtUtc).Take(10).ToList();

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
                openIncidents = allIncidents.Count(x => x.ResolvedAtUtc == null),
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
            var rules = await db.MonitoringRules.AsNoTracking()
                .Select(x => new { x.Id, x.TimeZoneId }).ToListAsync(ct);
            var zones = rules.ToDictionary(x => x.Id, x => TimeZoneInfo.FindSystemTimeZoneById(x.TimeZoneId));
            var expected = await db.ExpectedFiles.AsNoTracking().ToListAsync(ct);
            var filtered = expected.Where(x =>
                zones.TryGetValue(x.MonitoringRuleId, out var zone)
                    ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.ExpectedAtUtc, zone).DateTime) == selectedDate
                    : DateOnly.FromDateTime(x.ExpectedAtUtc.UtcDateTime) == selectedDate);
            if (ruleId.HasValue) filtered = filtered.Where(x => x.MonitoringRuleId == ruleId.Value);
            if (status.HasValue) filtered = filtered.Where(x => x.Status == status.Value);
            var rows = filtered.OrderBy(x => x.ExpectedAtUtc).Select(x => new
            {
                x.Id, x.MonitoringRuleId, x.ExpectedFileName, x.ExpectedAtUtc,
                x.DeadlineAtUtc, x.ReceivedAtUtc, status = x.Status.ToString()
            }).ToList();
            return Results.Ok(rows);
        })
        .WithName("GetExpectedFiles")
        .WithSummary("Consulter les fichiers attendus")
        .WithDescription("Liste les fichiers attendus pour une date. Filtres facultatifs : date (AAAA-MM-JJ), ruleId et status (Scheduled, Waiting, Missing, Received, Late, Invalid, Duplicate, Cancelled).")
        .Produces(StatusCodes.Status200OK);

        group.MapGet("/received-files", async (FileFlowDbContext db, int? limit, CancellationToken ct) =>
        {
            var take = Math.Clamp(limit ?? 100, 1, 500);
            var rows = (await db.ReceivedFiles.AsNoTracking().ToListAsync(ct))
                .OrderByDescending(x => x.DetectedAtUtc).Take(take).Select(x => new
                {
                    x.Id, x.MonitoringRuleId, x.FileName, x.FullPath, x.SizeBytes,
                    x.DetectedAtUtc, x.StableAtUtc, x.LastWriteTimeUtc
                }).ToList();
            return Results.Ok(rows);
        })
        .WithName("GetReceivedFiles")
        .WithSummary("Consulter les fichiers détectés")
        .WithDescription("Retourne les derniers fichiers observés par le moteur, avec chemin, taille, date de détection et date de stabilisation. Paramètre limit : 1 à 500, 100 par défaut.")
        .Produces(StatusCodes.Status200OK);

        group.MapGet("/incidents", async (FileFlowDbContext db, bool? openOnly, int? limit, CancellationToken ct) =>
        {
            var incidents = await db.MonitoringIncidents.AsNoTracking().ToListAsync(ct);
            if (openOnly == true) incidents = incidents.Where(x => x.ResolvedAtUtc == null).ToList();
            var rows = incidents.OrderByDescending(x => x.OccurredAtUtc)
                .Take(Math.Clamp(limit ?? 100, 1, 500))
                .Select(x => new
                {
                    x.Id, x.MonitoringRuleId, x.ExpectedFileId, x.Code, x.Message,
                    x.OccurredAtUtc, x.ResolvedAtUtc, isResolved = x.ResolvedAtUtc != null
                }).ToList();
            return Results.Ok(rows);
        })
        .WithName("GetMonitoringIncidents")
        .WithSummary("Consulter les incidents")
        .WithDescription("Liste les incidents de surveillance. Utiliser openOnly=true pour ne voir que les incidents non résolus. Limite comprise entre 1 et 500.")
        .Produces(StatusCodes.Status200OK);

        group.MapGet("/executions", async (FileFlowDbContext db, int? limit, CancellationToken ct) =>
        {
            var rows = (await db.MonitoringExecutions.AsNoTracking().ToListAsync(ct))
                .OrderByDescending(x => x.StartedAtUtc)
                .Take(Math.Clamp(limit ?? 100, 1, 500))
                .Select(x => new
                {
                    x.Id, x.MonitoringRuleId, x.StartedAtUtc, x.CompletedAtUtc,
                    x.FilesDiscovered, x.Error
                }).ToList();
            return Results.Ok(rows);
        })
        .WithName("GetMonitoringExecutions")
        .WithSummary("Consulter l'historique des cycles")
        .WithDescription("Retourne les dernières exécutions du moteur, leur durée, le nombre de fichiers détectés et les erreurs éventuelles.")
        .Produces(StatusCodes.Status200OK);

        return endpoints;
    }
}
