using FileFlowMonitor.Domain.Enums;

namespace FileFlowMonitor.Domain.Entities;

public sealed class MonitoringRule
{
    private readonly List<string> _expectedFileNames = [];
    private readonly List<string> _allowedExtensions = [];

    private MonitoringRule() { }

    public MonitoringRule(string name, string folderPath, MatchingMode matchingMode)
    {
        Id = Guid.NewGuid();
        Rename(name);
        SetFolderPath(folderPath);
        MatchingMode = matchingMode;
        TimeZoneId = TimeZoneInfo.Utc.Id;
        Days = WeekDays.Weekdays;
        IsEnabled = true;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string FolderPath { get; private set; } = string.Empty;
    public MatchingMode MatchingMode { get; private set; }
    public string? FileNamePattern { get; private set; }
    public int ExpectedCount { get; private set; } = 1;
    public int GracePeriodMinutes { get; private set; } = 15;
    public int StabilityPeriodSeconds { get; private set; } = 5;
    public string TimeZoneId { get; private set; } = TimeZoneInfo.Utc.Id;
    public WeekDays Days { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public IReadOnlyCollection<string> ExpectedFileNames => _expectedFileNames.AsReadOnly();
    public IReadOnlyCollection<string> AllowedExtensions => _allowedExtensions.AsReadOnly();
    public ICollection<ScheduleTime> ScheduleTimes { get; private set; } = new List<ScheduleTime>();

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Rule name is required.", nameof(name));
        Name = name.Trim();
    }

    public void SetFolderPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Folder path is required.", nameof(path));
        FolderPath = path.Trim();
    }

    public void ConfigureMatching(MatchingMode mode, string? pattern = null, int expectedCount = 1)
    {
        if (expectedCount < 1) throw new ArgumentOutOfRangeException(nameof(expectedCount));
        if (mode is MatchingMode.FileNameTemplate or MatchingMode.Pattern && string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("A file name template or pattern is required.", nameof(pattern));
        MatchingMode = mode;
        FileNamePattern = pattern?.Trim();
        ExpectedCount = expectedCount;
    }

    public void ConfigureTiming(int gracePeriodMinutes, int stabilityPeriodSeconds)
    {
        if (gracePeriodMinutes < 0) throw new ArgumentOutOfRangeException(nameof(gracePeriodMinutes));
        if (stabilityPeriodSeconds < 0) throw new ArgumentOutOfRangeException(nameof(stabilityPeriodSeconds));
        GracePeriodMinutes = gracePeriodMinutes;
        StabilityPeriodSeconds = stabilityPeriodSeconds;
    }

    public void ConfigureCalendar(WeekDays days, string timeZoneId)
    {
        if (days == WeekDays.None) throw new ArgumentException("At least one execution day is required.", nameof(days));
        if (string.IsNullOrWhiteSpace(timeZoneId)) throw new ArgumentException("Time zone is required.", nameof(timeZoneId));
        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        Days = days;
        TimeZoneId = timeZoneId;
    }

    public void SetExpectedFileNames(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var values = names.Select(x => x?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (MatchingMode == MatchingMode.ExactName && values.Count == 0)
            throw new ArgumentException("At least one exact expected file name is required.", nameof(names));
        _expectedFileNames.Clear();
        _expectedFileNames.AddRange(values);
    }

    public void SetAllowedExtensions(IEnumerable<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        _allowedExtensions.Clear();
        _allowedExtensions.AddRange(extensions.Select(x => x.Trim().TrimStart('.').ToLowerInvariant())
            .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}
