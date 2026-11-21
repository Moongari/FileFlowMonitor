using FileFlowMonitor.Domain.Entities;
using FileFlowMonitor.Domain.Enums;

namespace FileFlowMonitor.Application.ExpectedFiles;

public sealed class ExpectedFilePlanner
{
    public IReadOnlyList<ExpectedFile> Plan(MonitoringRule rule, DateOnly businessDate)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (!rule.IsEnabled) return [];

        var zone = TimeZoneInfo.FindSystemTimeZoneById(rule.TimeZoneId);
        var day = businessDate.DayOfWeek switch
        {
            DayOfWeek.Monday => WeekDays.Monday,
            DayOfWeek.Tuesday => WeekDays.Tuesday,
            DayOfWeek.Wednesday => WeekDays.Wednesday,
            DayOfWeek.Thursday => WeekDays.Thursday,
            DayOfWeek.Friday => WeekDays.Friday,
            DayOfWeek.Saturday => WeekDays.Saturday,
            DayOfWeek.Sunday => WeekDays.Sunday,
            _ => WeekDays.None
        };
        if ((rule.Days & day) == 0) return [];

        var results = new List<ExpectedFile>();
        foreach (var schedule in rule.ScheduleTimes.OrderBy(x => x.ExpectedTime))
        {
            var local = businessDate.ToDateTime(schedule.ExpectedTime);
            var expectedAt = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
            var deadline = expectedAt.AddMinutes(rule.GracePeriodMinutes);

            // Expand each configured template independently for every schedule.
            // This creates a separate expected occurrence for each file and time slot.
            foreach (var name in ResolveNames(rule, businessDate, schedule.ExpectedTime))
                results.Add(new ExpectedFile(rule.Id, name, expectedAt, deadline));
        }

        return results;
    }

    private static IReadOnlyList<string> ResolveNames(MonitoringRule rule, DateOnly date, TimeOnly time)
    {
        if (rule.MatchingMode == MatchingMode.ExactName)
            return rule.ExpectedFileNames.ToArray();

        if (rule.MatchingMode == MatchingMode.FileNameTemplate &&
            !string.IsNullOrWhiteSpace(rule.FileNamePattern))
        {
            var templates = rule.FileNamePattern
                .Split(['\\r', '\\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return templates.Select(template => template
                .Replace("{DATE}", date.ToString("yyyyMMdd"), StringComparison.OrdinalIgnoreCase)
                .Replace("{YYYY}", date.ToString("yyyy"), StringComparison.OrdinalIgnoreCase)
                .Replace("{MM}", date.ToString("MM"), StringComparison.OrdinalIgnoreCase)
                .Replace("{DD}", date.ToString("dd"), StringComparison.OrdinalIgnoreCase)
                .Replace("{TIME}", time.ToString("HHmmss"), StringComparison.OrdinalIgnoreCase)
                .Replace("{HH}", time.ToString("HH"), StringComparison.OrdinalIgnoreCase)
                .Replace("{MINUTES}", time.ToString("mm"), StringComparison.OrdinalIgnoreCase)
                .Replace("{SECONDS}", time.ToString("ss"), StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        // Pattern and quantity rules cannot determine individual missing file names.
        return [];
    }
}
