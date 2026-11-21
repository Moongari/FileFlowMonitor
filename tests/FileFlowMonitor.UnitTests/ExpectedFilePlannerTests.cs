using FileFlowMonitor.Application.ExpectedFiles;
using FileFlowMonitor.Domain.Entities;
using FileFlowMonitor.Domain.Enums;

namespace FileFlowMonitor.UnitTests;

public sealed class ExpectedFilePlannerTests
{
    [Fact]
    public void Plan_CreatesOneExpectedFilePerNameAndSchedule()
    {
        var rule = new MonitoringRule("Citi", @"D:\Inbound", MatchingMode.ExactName);
        rule.SetExpectedFileNames(["trades.csv", "positions.csv"]);
        rule.ConfigureCalendar(WeekDays.EveryDay, "UTC");
        rule.ConfigureTiming(15, 5);
        rule.ScheduleTimes.Add(new ScheduleTime(rule.Id, new TimeOnly(8, 0)));
        rule.ScheduleTimes.Add(new ScheduleTime(rule.Id, new TimeOnly(12, 0)));

        var result = new ExpectedFilePlanner().Plan(rule, new DateOnly(2026, 11, 21));

        Assert.Equal(4, result.Count);
        Assert.All(result, x => Assert.Equal(x.ExpectedAtUtc.AddMinutes(15), x.DeadlineAtUtc));
    }

    [Fact]
    public void Plan_DoesNotGuessNamesForPatternOnlyRule()
    {
        var rule = new MonitoringRule("Citi", @"D:\Inbound", MatchingMode.Pattern);
        rule.ConfigureMatching(MatchingMode.Pattern, "trade*.csv", 2);
        rule.ConfigureCalendar(WeekDays.EveryDay, "UTC");
        rule.ScheduleTimes.Add(new ScheduleTime(rule.Id, new TimeOnly(8, 0)));

        var result = new ExpectedFilePlanner().Plan(rule, new DateOnly(2026, 11, 21));

        Assert.Empty(result);
    }
}
