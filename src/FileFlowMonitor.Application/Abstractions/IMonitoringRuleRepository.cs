using FileFlowMonitor.Domain.Entities;

namespace FileFlowMonitor.Application.Abstractions;

public interface IMonitoringRuleRepository
{
    Task<MonitoringRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MonitoringRule>> GetEnabledAsync(CancellationToken cancellationToken = default);
    Task AddAsync(MonitoringRule rule, CancellationToken cancellationToken = default);
}
