using FileFlowMonitor.Domain.Enums;

namespace FileFlowMonitor.Application.MonitoringRules;

public sealed record MonitoringRuleConfiguration(
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
    bool IsEnabled);
