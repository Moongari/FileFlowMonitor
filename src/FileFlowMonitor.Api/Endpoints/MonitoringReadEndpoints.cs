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

        group.MapPost("/reconcile", async (
            FileFlowDbContext db, FileFlowMonitor.Infrastructure.Monitoring.MonitoringEngineCycle cycle,
            Guid? ruleId, CancellationToken ct) =>
        {
            if (ruleId.HasValue)
            {
                var exists = await db.MonitoringRules.AnyAsync(x => x.Id == ruleId.Value, ct);
                if (!exists) return Results.NotFound(new { message = "Règle de surveillance introuvable." });
                var enabled = await db.MonitoringRules.AnyAsync(x => x.Id == ruleId.Value && x.IsEnabled, ct);
                if (!enabled) return Results.BadRequest(new { message = "La règle est désactivée. Activez-la avant de forcer la réconciliation." });
            }

            var startedAt = DateTimeOffset.UtcNow;
            await cycle.ExecuteAsync(ct, ruleId);
            // DateTimeOffset comparisons with the computed threshold are not translated
            // consistently by all supported EF Core SQLite/provider versions.
            // Load the small execution history and apply the forced-cycle window in memory.
            var allExecutions = await db.MonitoringExecutions.AsNoTracking()
                .ToListAsync(ct);
            var executions = allExecutions
                .Where(x => x.StartedAtUtc >= startedAt.AddSeconds(-1) &&
                    (!ruleId.HasValue || x.MonitoringRuleId == ruleId.Value))
                .ToList();
            var expected = await db.ExpectedFiles.AsNoTracking()
                .Where(x => !ruleId.HasValue || x.MonitoringRuleId == ruleId.Value)
                .ToListAsync(ct);
            var received = await db.ReceivedFiles.AsNoTracking()
                .Where(x => !ruleId.HasValue || x.MonitoringRuleId == ruleId.Value)
                .ToListAsync(ct);
            var completed = executions.Count(x => x.CompletedAtUtc != null && x.Error == null);
            var failed = executions.Count(x => x.Error != null);
            var missing = expected.Count(x => x.Status == ExpectedFileStatus.Missing);
            var late = expected.Count(x => x.Status == ExpectedFileStatus.Late);
            var matched = expected.Count(x => x.Status is ExpectedFileStatus.Received or ExpectedFileStatus.Late);
            var unmatched = expected.Where(x => x.Status == ExpectedFileStatus.Missing)
                .OrderBy(x => x.ExpectedAtUtc).Take(20)
                .Select(x => new { x.ExpectedFileName, x.ExpectedAtUtc, x.DeadlineAtUtc }).ToList();
            return Results.Ok(new
            {
                message = "Réconciliation terminée.",
                startedAtUtc = startedAt,
                completedRules = completed,
                failedRules = failed,
                filesDetected = received.Count,
                expectedOccurrences = expected.Count,
                matchedOccurrences = matched,
                missingOccurrences = missing,
                lateOccurrences = late,
                remainingMissing = unmatched
            });
        })
        .WithName("ForceMonitoringReconciliation")
        .WithSummary("Forcer immédiatement la réconciliation des dossiers surveillés")
        .WithDescription("""
            Déclenche immédiatement le même cycle que la surveillance automatique, sans attendre le prochain intervalle.
            Facultatif : ruleId pour ne réconcilier qu'une règle précise. Sans ruleId, toutes les règles actives sont traitées.
            Exemple : POST /api/monitoring/reconcile
            Exemple ciblé : POST /api/monitoring/reconcile?ruleId=00000000-0000-0000-0000-000000000001
            Le dossier est rescanné, les fichiers stables sont rapprochés des occurrences attendues et les incidents correspondants sont résolus.
            """)
        .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status400BadRequest);


        group.MapGet("/overview", async (FileFlowDbContext db, DateOnly? date, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var rules = await db.MonitoringRules.AsNoTracking()
                .Include(x => x.ScheduleTimes).OrderBy(x => x.Name).ToListAsync(ct);
            var zones = rules.ToDictionary(x => x.Id, x => TimeZoneInfo.FindSystemTimeZoneById(x.TimeZoneId));
            var expectedAll = await db.ExpectedFiles.AsNoTracking().ToListAsync(ct);
            var selectedByRule = expectedAll.Where(x =>
            {
                if (!zones.TryGetValue(x.MonitoringRuleId, out var zone) ||
                    x.Status == ExpectedFileStatus.Cancelled) return false;
                var businessDate = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.ExpectedAtUtc, zone).DateTime) == businessDate;
            }).ToList();

            var ruleNames = rules.ToDictionary(x => x.Id, x => x.Name);
            var summaries = rules.Select(rule =>
            {
                var items = selectedByRule.Where(x => x.MonitoringRuleId == rule.Id).ToList();
                var received = items.Count(x => x.Status == ExpectedFileStatus.Received);
                var late = items.Count(x => x.Status == ExpectedFileStatus.Late);
                var missing = items.Count(x => x.Status == ExpectedFileStatus.Missing);
                var invalid = items.Count(x => x.Status == ExpectedFileStatus.Invalid);
                var waiting = items.Count(x => x.Status == ExpectedFileStatus.Waiting);
                var scheduled = items.Count(x => x.Status == ExpectedFileStatus.Scheduled);
                var zone = zones[rule.Id];
                var folderAvailable = Directory.Exists(rule.FolderPath);
                var health = !rule.IsEnabled ? "Disabled"
                    : !folderAvailable ? "FolderUnavailable"
                    : missing > 0 || invalid > 0 ? "Attention"
                    : late > 0 ? "Delayed"
                    : "Healthy";
                return new
                {
                    rule.Id, rule.Name, rule.FolderPath, rule.IsEnabled, rule.GracePeriodMinutes,
                    timeZoneId = rule.TimeZoneId,
                    scheduleTimes = rule.ScheduleTimes.Select(t => t.ExpectedTime).OrderBy(t => t).ToArray(),
                    expected = items.Count, received, late, missing, invalid, waiting, scheduled,
                    healthStatus = health
                };
            }).ToList();

            var allIncidents = await db.MonitoringIncidents.AsNoTracking().ToListAsync(ct);
            var incidents = allIncidents.Where(x => x.ResolvedAtUtc == null)
                .OrderByDescending(x => x.OccurredAtUtc).Take(10).ToList();
            var openIncidentCount = allIncidents.Count(x => x.ResolvedAtUtc == null &&
                zones.TryGetValue(x.MonitoringRuleId, out var incidentZone) &&
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.OccurredAtUtc, incidentZone).DateTime) ==
                (date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, incidentZone).DateTime)));
            var executions = (await db.MonitoringExecutions.AsNoTracking().ToListAsync(ct))
                .OrderByDescending(x => x.StartedAtUtc).Take(10).ToList();

            var expectedRows = selectedByRule.OrderBy(x => x.ExpectedAtUtc).ThenBy(x => x.ExpectedFileName)
                .Select(x =>
                {
                    var zone = zones[x.MonitoringRuleId];
                    return new
                    {
                        x.Id, x.MonitoringRuleId,
                        ruleName = ruleNames.GetValueOrDefault(x.MonitoringRuleId, "Règle inconnue"),
                        x.ExpectedFileName, x.ExpectedAtUtc, x.DeadlineAtUtc, x.ReceivedAtUtc,
                        receivedAtLocal = x.ReceivedAtUtc.HasValue ? TimeZoneInfo.ConvertTime(x.ReceivedAtUtc.Value, zone).ToString("yyyy-MM-dd HH:mm:ss") : null,
                        expectedAtLocal = TimeZoneInfo.ConvertTime(x.ExpectedAtUtc, zone).ToString("yyyy-MM-dd HH:mm:ss"),
                        deadlineAtLocal = TimeZoneInfo.ConvertTime(x.DeadlineAtUtc, zone).ToString("yyyy-MM-dd HH:mm:ss"),
                        status = x.Status.ToString()
                    };
                }).ToList();

            return Results.Ok(new
            {
                generatedAtUtc = now,
                selectedDate = date?.ToString("yyyy-MM-dd"),
                rulesTotal = rules.Count,
                rulesEnabled = rules.Count(x => x.IsEnabled),
                expectedToday = selectedByRule.Count,
                receivedToday = selectedByRule.Count(x => x.Status == ExpectedFileStatus.Received),
                lateToday = selectedByRule.Count(x => x.Status == ExpectedFileStatus.Late),
                missingToday = selectedByRule.Count(x => x.Status == ExpectedFileStatus.Missing),
                invalidToday = selectedByRule.Count(x => x.Status == ExpectedFileStatus.Invalid),
                waitingToday = selectedByRule.Count(x => x.Status == ExpectedFileStatus.Waiting),
                scheduledToday = selectedByRule.Count(x => x.Status == ExpectedFileStatus.Scheduled),
                openIncidents = openIncidentCount,
                ruleSummaries = summaries,
                expectedFiles = expectedRows,
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
        .WithSummary("Vue d'ensemble de la supervision pour une journée")
        .WithDescription("""
            Retourne les indicateurs globaux, les compteurs par règle et chaque fichier attendu pour la date métier sélectionnée.
            Le paramètre date (AAAA-MM-JJ) est facultatif. Sans date, chaque règle utilise sa date locale courante.
            Les règles restent permanentes ; les occurrences journalières sont générées par le moteur et conservées pour l'historique.
            Exemple : GET /api/monitoring/overview?date=2026-11-21.
            Exemple de réponse : generatedAtUtc, expectedToday, receivedToday, missingToday, ruleSummaries et expectedFiles.
            Un fichier manquant est retourné avec status=Missing et son nom exact, par exemple trades.csv.
            """)
        .Produces(StatusCodes.Status200OK);

        group.MapGet("/expected-files", async (
            FileFlowDbContext db, DateOnly? date, Guid? ruleId, ExpectedFileStatus? status, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var rules = await db.MonitoringRules.AsNoTracking()
                .Select(x => new { x.Id, x.Name, x.TimeZoneId }).ToListAsync(ct);
            var ruleMap = rules.ToDictionary(x => x.Id,
                x => (x.Name, Zone: TimeZoneInfo.FindSystemTimeZoneById(x.TimeZoneId)));
            var expected = await db.ExpectedFiles.AsNoTracking().ToListAsync(ct);
            var filtered = expected.Where(x =>
            {
                if (!ruleMap.TryGetValue(x.MonitoringRuleId, out var rule)) return false;
                var businessDate = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, rule.Zone).DateTime);
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.ExpectedAtUtc, rule.Zone).DateTime) == businessDate;
            });
            if (ruleId.HasValue) filtered = filtered.Where(x => x.MonitoringRuleId == ruleId.Value);
            if (status.HasValue) filtered = filtered.Where(x => x.Status == status.Value);
            var rows = filtered.OrderBy(x => x.ExpectedAtUtc).ThenBy(x => x.ExpectedFileName).Select(x =>
            {
                var rule = ruleMap[x.MonitoringRuleId];
                return new
                {
                    x.Id, x.MonitoringRuleId, ruleName = rule.Name, x.ExpectedFileName,
                    x.ExpectedAtUtc, x.DeadlineAtUtc, x.ReceivedAtUtc,
                    receivedAtLocal = x.ReceivedAtUtc.HasValue ? TimeZoneInfo.ConvertTime(x.ReceivedAtUtc.Value, rule.Zone).ToString("yyyy-MM-dd HH:mm:ss") : null,
                    expectedAtLocal = TimeZoneInfo.ConvertTime(x.ExpectedAtUtc, rule.Zone).ToString("yyyy-MM-dd HH:mm:ss"),
                    deadlineAtLocal = TimeZoneInfo.ConvertTime(x.DeadlineAtUtc, rule.Zone).ToString("yyyy-MM-dd HH:mm:ss"),
                    status = x.Status.ToString()
                };
            }).ToList();
            return Results.Ok(rows);
        })
        .WithName("GetExpectedFiles")
        .WithSummary("Consulter les fichiers attendus d'une journée et d'un flux")
        .WithDescription("""
            Liste individuellement les fichiers attendus pour une date métier locale, avec le nom de la règle et les heures locales.
            Exemple : GET /api/monitoring/expected-files?date=2026-11-21&status=Missing.
            Filtres facultatifs : date (AAAA-MM-JJ), ruleId et status (Scheduled, Waiting, Missing, Received, Late, Invalid, Duplicate, Cancelled).
            Sans date, utilise la date locale de chaque règle.
            Exemple de réponse : expectedFileName=trades.csv, expectedAtLocal=2026-11-21 08:00:00, status=Missing.
            """)
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
        .WithDescription("""
            Retourne les derniers fichiers observés par le moteur, avec chemin, taille, date de détection et date de stabilisation.
            Paramètre limit : 1 à 500, 100 par défaut.
            """)
        .Produces(StatusCodes.Status200OK);

        group.MapGet("/incidents", async (FileFlowDbContext db, bool? openOnly, int? limit, CancellationToken ct) =>
        {
            var incidents = await db.MonitoringIncidents.AsNoTracking().ToListAsync(ct);
            if (openOnly == true) incidents = incidents.Where(x => x.ResolvedAtUtc == null).ToList();
            var selected = incidents.OrderByDescending(x => x.OccurredAtUtc)
                .Take(Math.Clamp(limit ?? 100, 1, 500)).ToList();
            var ids = selected.Select(x => x.Id).ToArray();
            var workflows = await db.MonitoringIncidentWorkflows.AsNoTracking()
                .Where(x => ids.Contains(x.MonitoringIncidentId)).ToDictionaryAsync(x => x.MonitoringIncidentId, ct);
            var rows = selected.Select(x =>
            {
                workflows.TryGetValue(x.Id, out var workflow);
                return new
                {
                    x.Id, x.MonitoringRuleId, x.ExpectedFileId, x.Code, x.Message,
                    x.OccurredAtUtc, x.ResolvedAtUtc, isResolved = workflow?.Status is "Resolved" or "Closed" || x.ResolvedAtUtc != null,
                    status = workflow?.Status ?? (x.ResolvedAtUtc.HasValue ? "Resolved" : "Open"),
                    severity = workflow?.Severity ?? "Medium",
                    assignedTo = workflow?.AssignedTo,
                    acknowledgedAtUtc = workflow?.AcknowledgedAtUtc,
                    workflowResolvedAtUtc = workflow?.ResolvedAtUtc,
                    closedAtUtc = workflow?.ClosedAtUtc,
                    resolutionComment = workflow?.ResolutionComment
                };
            }).ToList();
            return Results.Ok(rows);
        })
        .WithName("GetMonitoringIncidents")
        .WithSummary("Consulter les incidents")
        .WithDescription("""
            Liste les incidents de surveillance. Utiliser openOnly=true pour ne voir que les incidents non résolus.
            Les fichiers manquants possèdent un incident lié à leur occurrence attendue.
            Limite comprise entre 1 et 500.
            """)
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
        .WithDescription("""
            Retourne les dernières exécutions du moteur de réconciliation, leur durée, le nombre de fichiers détectés et les erreurs éventuelles.
            """)
        .Produces(StatusCodes.Status200OK);

        return endpoints;
    }
}