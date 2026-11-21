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

    public Task UpdateAsync(MonitoringRule rule, CancellationToken cancellationToken = default)
    {
        // GetByIdAsync returns a tracked aggregate. Preserve EF Core's change
        // tracking so replaced ScheduleTime children are handled as deletes/adds.
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(MonitoringRule rule, CancellationToken cancellationToken = default)
    {
        dbContext.MonitoringRules.Remove(rule);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
