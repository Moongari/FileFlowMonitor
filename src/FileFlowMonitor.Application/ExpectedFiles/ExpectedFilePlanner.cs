using FileFlowMonitor.Domain.Entities;
using FileFlowMonitor.Domain.Enums;
using System.Text.RegularExpressions;

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

            foreach (var name in ResolveNames(rule, businessDate, schedule.ExpectedTime))
                results.Add(new ExpectedFile(rule.Id, name, expectedAt, deadline));
        }

        return results;
    }

    /// <summary>
    /// Matches a physical file against a persisted occurrence. Template matching
    /// accepts both HHmmss and the legacy HHmm0 time token used by test feeds.
    /// </summary>
    public bool MatchesFileName(MonitoringRule rule, ExpectedFile expected, string fileName)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(expected);
        if (string.IsNullOrWhiteSpace(fileName)) return false;

        if (rule.MatchingMode == MatchingMode.ExactName)
            return string.Equals(expected.ExpectedFileName, fileName, StringComparison.OrdinalIgnoreCase);

        if (rule.MatchingMode == MatchingMode.Pattern)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(rule.FileNamePattern) &&
                    Regex.IsMatch(fileName, rule.FileNamePattern,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(250));
            }
            catch (RegexMatchTimeoutException) { return false; }
            catch (ArgumentException) { return false; }
        }

        if (rule.MatchingMode == MatchingMode.Quantity)
        {
            var extension = Path.GetExtension(fileName).TrimStart('.');
            return rule.AllowedExtensions.Count == 0 ||
                rule.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        if (rule.MatchingMode != MatchingMode.FileNameTemplate ||
            string.IsNullOrWhiteSpace(rule.FileNamePattern))
            return false;

        var zone = TimeZoneInfo.FindSystemTimeZoneById(rule.TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(expected.ExpectedAtUtc, zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var templates = rule.FileNamePattern.Split(
            new[] { "\r", "\n", ";" },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return templates.Any(template =>
            string.Equals(ExpandTemplate(template, date, time, time.ToString("HHmmss")),
                fileName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ExpandTemplate(template, date, time, time.ToString("HHmm") + "0"),
                fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ResolveNames(MonitoringRule rule, DateOnly date, TimeOnly time)
    {
        if (rule.MatchingMode == MatchingMode.ExactName)
            return rule.ExpectedFileNames.ToArray();

        if (rule.MatchingMode == MatchingMode.FileNameTemplate &&
            !string.IsNullOrWhiteSpace(rule.FileNamePattern))
        {
            var templates = rule.FileNamePattern
                .Split(new[] { "\r", "\n", ";" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return templates.Select(template => ExpandTemplate(template, date, time, time.ToString("HHmmss")))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (rule.MatchingMode is MatchingMode.Pattern or MatchingMode.Quantity)
        {
            var prefix = rule.MatchingMode == MatchingMode.Pattern ? "Pattern match" : "Quantity file";
            return Enumerable.Range(1, rule.ExpectedCount)
                .Select(index => $"{prefix} {index:00}")
                .ToArray();
        }

        return [];
    }

    private static string ExpandTemplate(string template, DateOnly date, TimeOnly time, string timeToken) =>
        template
            .Replace("{DATE}", date.ToString("yyyyMMdd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{YYYY}", date.ToString("yyyy"), StringComparison.OrdinalIgnoreCase)
            .Replace("{MM}", date.ToString("MM"), StringComparison.OrdinalIgnoreCase)
            .Replace("{DD}", date.ToString("dd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{TIME}", timeToken, StringComparison.OrdinalIgnoreCase)
            .Replace("{TIME5}", time.ToString("HHmm") + "0", StringComparison.OrdinalIgnoreCase)
            .Replace("{HH}", time.ToString("HH"), StringComparison.OrdinalIgnoreCase)
            .Replace("{MINUTES}", time.ToString("mm"), StringComparison.OrdinalIgnoreCase)
            .Replace("{SECONDS}", time.ToString("ss"), StringComparison.OrdinalIgnoreCase);
}
