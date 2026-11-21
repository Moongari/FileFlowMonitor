using FileFlowMonitor.Application.ExpectedFiles;
using FileFlowMonitor.Domain.Entities;
using FileFlowMonitor.Domain.Enums;
using FileFlowMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FileFlowMonitor.Infrastructure.Monitoring;

/// <summary>
/// Executes one reconciliation cycle. The filesystem is only read; all monitoring
/// state is persisted in SQLite so a process restart does not reset the history.
/// </summary>
public sealed class MonitoringEngineCycle(FileFlowDbContext db, ExpectedFilePlanner planner)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rules = await db.MonitoringRules
            .Include(x => x.ScheduleTimes)
            .Where(x => x.IsEnabled)
            .ToListAsync(cancellationToken);

        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var execution = new MonitoringExecution(rule.Id, now);
            db.MonitoringExecutions.Add(execution);

            try
            {
                await PlanExpectedFilesAsync(rule, now, cancellationToken);
                // Persist newly planned occurrences before folder reconciliation,
                // because matching queries are database-backed and must see them.
                await db.SaveChangesAsync(cancellationToken);
                var discovered = await ReconcileFolderAsync(rule, now, cancellationToken);

                var expected = await db.ExpectedFiles
                    .Where(x => x.MonitoringRuleId == rule.Id)
                    .ToListAsync(cancellationToken);

                foreach (var item in expected)
                {
                    item.RefreshStatus(now);
                    if (item.Status == ExpectedFileStatus.Missing)
                    {
                        await RecordMissingFileIncidentAsync(rule, item, now, cancellationToken);
                    }
                    else if (item.Status is ExpectedFileStatus.Received or ExpectedFileStatus.Late)
                    {
                        await ResolveMissingFileIncidentAsync(item.Id, now, cancellationToken);
                    }
                }

                execution.Complete(DateTimeOffset.UtcNow, discovered);
                await ResolveIncidentAsync(rule.Id, "MONITORING_EXECUTION_FAILED", DateTimeOffset.UtcNow, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                execution.Fail(DateTimeOffset.UtcNow, ex.Message);
                await RecordIncidentAsync(rule.Id, "MONITORING_EXECUTION_FAILED",
                    ex.Message, now, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task PlanExpectedFilesAsync(
        MonitoringRule rule, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var localDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(rule.TimeZoneId)).DateTime);

        // Generate the current business day and the next one on every cycle.
        // The persisted unique occurrence key makes this operation idempotent.
        var existing = await db.ExpectedFiles
            .Where(x => x.MonitoringRuleId == rule.Id)
            .ToListAsync(cancellationToken);
        var existingByKey = existing
            .GroupBy(x => (x.ExpectedAtUtc.UtcDateTime.Ticks, x.ExpectedFileName.ToUpperInvariant()))
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var date in new[] { localDate, localDate.AddDays(1) })
        {
            var planned = planner.Plan(rule, date);
            foreach (var item in planned)
            {
                var key = (item.ExpectedAtUtc.UtcDateTime.Ticks, item.ExpectedFileName.ToUpperInvariant());
                if (existingByKey.TryGetValue(key, out var existingOccurrence))
                {
                    existingOccurrence.Reschedule();
                    continue;
                }

                db.ExpectedFiles.Add(item);
                existingByKey.Add(key, item);
            }
        }
    }

    private async Task<int> ReconcileFolderAsync(
        MonitoringRule rule, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rule.FolderPath))
        {
            await RecordIncidentAsync(rule.Id, "FOLDER_UNAVAILABLE",
                $"The monitored folder is missing or inaccessible: {rule.FolderPath}",
                now, cancellationToken);
            return 0;
        }

        await ResolveIncidentAsync(rule.Id, "FOLDER_UNAVAILABLE", now, cancellationToken);
        var discovered = 0;
        foreach (var path in Directory.EnumerateFiles(rule.FolderPath, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            info.Refresh();
            if (!info.Exists) continue;

            var lastWrite = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            var fileVersions = await db.ReceivedFiles
                .Where(x => x.MonitoringRuleId == rule.Id && x.FullPath == info.FullName)
                .ToListAsync(cancellationToken);
            var existing = fileVersions.FirstOrDefault(x =>
                x.SizeBytes == info.Length && Math.Abs((x.LastWriteTimeUtc - lastWrite).TotalSeconds) < 1);

            if (existing is null)
            {
                existing = new ReceivedFile(rule.Id, info.FullName, info.Name,
                    info.Length, lastWrite, now);
                db.ReceivedFiles.Add(existing);
                discovered++;
                // When no stability delay is configured, a newly discovered file
                // can be verified and reconciled during this same cycle. Otherwise
                // wait for a subsequent cycle to confirm the file is stable.
                if (rule.StabilityPeriodSeconds > 0)
                    continue;
            }

            // A stable file must still be reconciled against expected occurrences
            // on every cycle. Stability only skips the verification phase, not matching.
            if (!existing.StableAtUtc.HasValue && rule.StabilityPeriodSeconds > 0 &&
                now < existing.DetectedAtUtc.AddSeconds(rule.StabilityPeriodSeconds))
                continue;

            info.Refresh();
            var confirmedLastWrite = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            if (!info.Exists || info.Length != existing.SizeBytes ||
                Math.Abs((confirmedLastWrite - existing.LastWriteTimeUtc).TotalSeconds) >= 1)
                continue;

            if (rule.AllowedExtensions.Count > 0 &&
                !rule.AllowedExtensions.Contains(info.Extension.TrimStart('.'),
                    StringComparer.OrdinalIgnoreCase))
            {
                existing.MarkStable(now);
                var invalidCandidates = await db.ExpectedFiles
                    .Where(x => x.MonitoringRuleId == rule.Id &&
                                x.Status != ExpectedFileStatus.Received &&
                                x.Status != ExpectedFileStatus.Late &&
                                x.Status != ExpectedFileStatus.Cancelled)
                    .ToListAsync(cancellationToken);
                var invalidExpected = invalidCandidates.FirstOrDefault(x =>
                    string.Equals(x.ExpectedFileName, info.Name, StringComparison.OrdinalIgnoreCase));
                invalidExpected?.MarkInvalid();
                continue;
            }

            existing.MarkStable(now);
            var expectedCandidates = await db.ExpectedFiles
                .Where(x => x.MonitoringRuleId == rule.Id)
                .ToListAsync(cancellationToken);
            var expectedFile = expectedCandidates
                .Where(x => (x.Status == ExpectedFileStatus.Scheduled ||
                             x.Status == ExpectedFileStatus.Waiting ||
                             x.Status == ExpectedFileStatus.Missing) &&
                            string.Equals(x.ExpectedFileName, info.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.ExpectedAtUtc <= existing.DetectedAtUtc)
                .ThenByDescending(x => x.ExpectedAtUtc)
                .FirstOrDefault();

            if (expectedFile is not null)
                expectedFile.MarkReceived(existing.Id, existing.DetectedAtUtc);
        }

        return discovered;
    }

    private async Task RecordMissingFileIncidentAsync(
        MonitoringRule rule, ExpectedFile expected, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var alreadyOpen = await db.MonitoringIncidents.AnyAsync(x =>
            x.ExpectedFileId == expected.Id &&
            x.Code == "EXPECTED_FILE_MISSING" &&
            x.ResolvedAtUtc == null, cancellationToken);

        if (!alreadyOpen)
        {
            db.MonitoringIncidents.Add(new MonitoringIncident(
                rule.Id, expected.Id, "EXPECTED_FILE_MISSING",
                $"Fichier manquant pour le flux « {rule.Name} » : {expected.ExpectedFileName} (échéance {expected.DeadlineAtUtc:yyyy-MM-dd HH:mm} UTC).",
                now));
        }
    }

    private async Task ResolveMissingFileIncidentAsync(
        Guid expectedFileId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var open = await db.MonitoringIncidents.FirstOrDefaultAsync(x =>
            x.ExpectedFileId == expectedFileId &&
            x.Code == "EXPECTED_FILE_MISSING" &&
            x.ResolvedAtUtc == null, cancellationToken);
        open?.Resolve(now);
    }

    private async Task RecordIncidentAsync(
        Guid ruleId, string code, string message, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var open = await db.MonitoringIncidents.FirstOrDefaultAsync(x =>
            x.MonitoringRuleId == ruleId && x.Code == code && x.ResolvedAtUtc == null,
            cancellationToken);

        if (open is null)
            db.MonitoringIncidents.Add(new MonitoringIncident(ruleId, null, code, message, now));
    }

    private async Task ResolveIncidentAsync(
        Guid ruleId, string code, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var open = await db.MonitoringIncidents.FirstOrDefaultAsync(x =>
            x.MonitoringRuleId == ruleId && x.Code == code && x.ResolvedAtUtc == null,
            cancellationToken);

        open?.Resolve(now);
    }
}