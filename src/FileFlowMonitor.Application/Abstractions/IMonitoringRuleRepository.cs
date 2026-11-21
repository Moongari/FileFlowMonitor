using FileFlowMonitor.Domain.Entities;

namespace FileFlowMonitor.Application.Abstractions;

public interface IMonitoringRuleRepository
{
    Task<MonitoringRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MonitoringRule>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MonitoringRule>> GetEnabledAsync(CancellationToken cancellationToken = default);
    Task AddAsync(MonitoringRule rule, CancellationToken cancellationToken = default);
    Task UpdateAsync(MonitoringRule rule, CancellationToken cancellationToken = default);
    Task CancelFutureExpectedFilesAsync(Guid ruleId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
    Task DeleteAsync(MonitoringRule rule, CancellationToken cancellationToken = default);
}
