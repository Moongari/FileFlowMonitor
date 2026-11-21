using FileFlowMonitor.Application.Abstractions;
using FileFlowMonitor.Domain.Entities;

namespace FileFlowMonitor.Application.MonitoringRules;

public sealed class MonitoringRuleService(IMonitoringRuleRepository repository)
{
    public Task<IReadOnlyList<MonitoringRule>> GetAllAsync(CancellationToken cancellationToken = default) =>
        repository.GetAllAsync(cancellationToken);

    public Task<IReadOnlyList<MonitoringRule>> GetEnabledAsync(CancellationToken cancellationToken = default) =>
        repository.GetEnabledAsync(cancellationToken);

    public Task<MonitoringRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        repository.GetByIdAsync(id, cancellationToken);

    public async Task<MonitoringRule> CreateAsync(MonitoringRuleConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var rule = new MonitoringRule(configuration.Name, configuration.FolderPath, configuration.MatchingMode);
        Apply(rule, configuration);
        await repository.AddAsync(rule, cancellationToken);
        return rule;
    }

    public async Task<MonitoringRule?> UpdateAsync(Guid id, MonitoringRuleConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var rule = await repository.GetByIdAsync(id, cancellationToken);
        if (rule is null) return null;

        rule.Rename(configuration.Name);
        rule.SetFolderPath(configuration.FolderPath);
        Apply(rule, configuration);
        await repository.UpdateAsync(rule, cancellationToken);
        return rule;
    }

    public async Task<bool> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
    {
        var rule = await repository.GetByIdAsync(id, cancellationToken);
        if (rule is null) return false;
        rule.SetEnabled(enabled);
        await repository.UpdateAsync(rule, cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await repository.GetByIdAsync(id, cancellationToken);
        if (rule is null) return false;
        await repository.DeleteAsync(rule, cancellationToken);
        return true;
    }

    private static void Apply(MonitoringRule rule, MonitoringRuleConfiguration configuration)
    {
        rule.ConfigureMatching(configuration.MatchingMode, configuration.FileNamePattern, configuration.ExpectedCount);
        rule.SetExpectedFileNames(configuration.ExpectedFileNames);
        rule.SetAllowedExtensions(configuration.AllowedExtensions);
        rule.SetScheduleTimes(configuration.ScheduleTimes);
        rule.ConfigureCalendar(configuration.Days, configuration.TimeZoneId);
        rule.ConfigureTiming(configuration.GracePeriodMinutes, configuration.StabilityPeriodSeconds);
        rule.SetEnabled(configuration.IsEnabled);
    }
}
