using FileFlowMonitor.Application.Abstractions;
using FileFlowMonitor.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FileFlowMonitor.Infrastructure.Persistence;

public sealed class MonitoringRuleRepository(FileFlowDbContext dbContext) : IMonitoringRuleRepository
{
    public Task<MonitoringRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.MonitoringRules.Include(x => x.ScheduleTimes)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MonitoringRule>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.MonitoringRules.AsNoTracking().Include(x => x.ScheduleTimes)
            .OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MonitoringRule>> GetEnabledAsync(CancellationToken cancellationToken = default) =>
        await dbContext.MonitoringRules.AsNoTracking().Include(x => x.ScheduleTimes)
            .Where(x => x.IsEnabled).OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public async Task AddAsync(MonitoringRule rule, CancellationToken cancellationToken = default)
    {
        await dbContext.MonitoringRules.AddAsync(rule, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(MonitoringRule rule, CancellationToken cancellationToken = default)
    {
        // ScheduleTime rows are fully replaced by rule configuration. EF Core's
        // relationship snapshot can otherwise keep a stale child in Modified
        // state after collection replacement (especially when the same DbContext
        // created and then reloaded the aggregate). Reconcile these rows explicitly.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var trackedScheduleEntries = dbContext.ChangeTracker
            .Entries<ScheduleTime>()
            .Where(entry => entry.Entity.MonitoringRuleId == rule.Id)
            .ToList();

        foreach (var entry in trackedScheduleEntries)
            entry.State = EntityState.Detached;

        await dbContext.ScheduleTimes
            .Where(schedule => schedule.MonitoringRuleId == rule.Id)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.ScheduleTimes.AddRange(rule.ScheduleTimes);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelFutureExpectedFilesAsync(
        Guid ruleId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        // Load by rule id only: SQLite DateTimeOffset range comparisons are not portable.
        var expected = await dbContext.ExpectedFiles
            .Where(x => x.MonitoringRuleId == ruleId)
            .ToListAsync(cancellationToken);

        foreach (var item in expected)
        {
            if (item.ExpectedAtUtc > nowUtc &&
                item.Status is FileFlowMonitor.Domain.Enums.ExpectedFileStatus.Scheduled
                    or FileFlowMonitor.Domain.Enums.ExpectedFileStatus.Waiting)
            {
                item.Cancel();
            }
        }
    }

    public async Task DeleteAsync(MonitoringRule rule, CancellationToken cancellationToken = default)
    {
        dbContext.MonitoringRules.Remove(rule);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
