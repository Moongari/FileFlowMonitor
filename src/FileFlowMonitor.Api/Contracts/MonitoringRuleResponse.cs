using FileFlowMonitor.Domain.Entities;
using FileFlowMonitor.Domain.Enums;

namespace FileFlowMonitor.Api.Contracts;

/// <summary>
/// Représentation publique d'une règle de surveillance retournée par l'API.
/// </summary>
public sealed record MonitoringRuleResponse(
    Guid Id,
    string Name,
    string FolderPath,
    MatchingMode MatchingMode,
    string? FileNamePattern,
    int ExpectedCount,
    IReadOnlyCollection<string> ExpectedFileNames,
    IReadOnlyCollection<string> AllowedExtensions,
    IReadOnlyCollection<TimeOnly> ScheduleTimes,
    WeekDays Days,
    string TimeZoneId,
    int GracePeriodMinutes,
    int StabilityPeriodSeconds,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc)
{
    /// <summary>Construit le contrat de réponse à partir de l'entité métier.</summary>
    public static MonitoringRuleResponse FromEntity(MonitoringRule rule) => new(
        rule.Id,
        rule.Name,
        rule.FolderPath,
        rule.MatchingMode,
        rule.FileNamePattern,
        rule.ExpectedCount,
        rule.ExpectedFileNames,
        rule.AllowedExtensions,
        rule.ScheduleTimes.Select(x => x.ExpectedTime).OrderBy(x => x).ToArray(),
        rule.Days,
        rule.TimeZoneId,
        rule.GracePeriodMinutes,
        rule.StabilityPeriodSeconds,
        rule.IsEnabled,
        rule.CreatedAtUtc);
}
