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
                var discovered = await ReconcileFolderAsync(rule, now, cancellationToken);

                var expected = await db.ExpectedFiles
                    .Where(x => x.MonitoringRuleId == rule.Id)
                    .ToListAsync(cancellationToken);

                foreach (var item in expected)
                    item.RefreshStatus(now);

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

        // Load persisted occurrences once, then keep the in-memory key set updated
        // while planning both dates. This also deduplicates within a single cycle.
        var existing = await db.ExpectedFiles.AsNoTracking()
            .Where(x => x.MonitoringRuleId == rule.Id)
            .ToListAsync(cancellationToken);
        var existingKeys = existing
            .Select(x => (x.ExpectedAtUtc.UtcDateTime.Ticks, x.ExpectedFileName.ToUpperInvariant()))
            .ToHashSet();

        foreach (var date in new[] { localDate, localDate.AddDays(1) })
        {
            var planned = planner.Plan(rule, date);
            foreach (var item in planned)
            {
                var key = (item.ExpectedAtUtc.UtcDateTime.Ticks, item.ExpectedFileName.ToUpperInvariant());
                if (existingKeys.Add(key))
                    db.ExpectedFiles.Add(item);
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
        // EnumerateFiles supports local paths and UNC shares. No files are moved,
        // opened for writing, or modified by the monitoring engine.
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
                continue;
            }

            if (existing.StableAtUtc.HasValue)
                continue;

            if (rule.StabilityPeriodSeconds > 0 &&
                now < existing.DetectedAtUtc.AddSeconds(rule.StabilityPeriodSeconds))
                continue;

            // Re-read metadata immediately before qualifying the file. A changed
            // file will be observed as a new version on the next reconciliation.
            info.Refresh();
            if (!info.Exists || info.Length != existing.SizeBytes ||
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero) != existing.LastWriteTimeUtc)
                continue;

            if (rule.AllowedExtensions.Count > 0 &&
                !rule.AllowedExtensions.Contains(info.Extension.TrimStart('.'),
                    StringComparer.OrdinalIgnoreCase))
            {
                existing.MarkStable(now);
                var invalidCandidates = await db.ExpectedFiles
                    .Where(x => x.MonitoringRuleId == rule.Id &&
                                x.Status != ExpectedFileStatus.Received &&
                                x.Status != ExpectedFileStatus.Late)
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
