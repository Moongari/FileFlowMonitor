using Xunit;
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
    [Fact]
    public void Plan_Expands_Date_Tokens_In_FileNameTemplate()
    {
        var rule = new MonitoringRule("Citi daily", @"D:\\Inbound", MatchingMode.FileNameTemplate);
        rule.ConfigureMatching(MatchingMode.FileNameTemplate, "trades_{DATE}_{YYYY}{MM}{DD}.csv");
        rule.ConfigureCalendar(WeekDays.EveryDay, "UTC");
        rule.SetScheduleTimes([new TimeOnly(8, 0)]);

        var result = new ExpectedFilePlanner().Plan(rule, new DateOnly(2026, 11, 21));

        var expected = Assert.Single(result);
        Assert.Equal("trades_20261121_20261121.csv", expected.ExpectedFileName);
    }

    [Fact]
    public void Plan_Skips_Dates_Outside_Rule_Calendar()
    {
        var rule = new MonitoringRule("Weekday feed", @"D:\\Inbound", MatchingMode.ExactName);
        rule.SetExpectedFileNames(["trades.csv"]);
        rule.ConfigureCalendar(WeekDays.Weekdays, "UTC");
        rule.SetScheduleTimes([new TimeOnly(8, 0)]);

        var result = new ExpectedFilePlanner().Plan(rule, new DateOnly(2026, 11, 22));

        Assert.Empty(result);
    }


    [Fact]
    public void Plan_Expands_Multiple_File_Templates_For_Each_Schedule_Time()
    {
        var rule = new MonitoringRule("Citi hourly", @"D:\\Inbound", MatchingMode.FileNameTemplate);
        rule.ConfigureMatching(MatchingMode.FileNameTemplate,
            "trade1_{DATE}_{TIME}.csv;trade2_{DATE}_{TIME}.csv");
        rule.ConfigureCalendar(WeekDays.EveryDay, "UTC");
        rule.ConfigureTiming(15, 5);
        rule.SetScheduleTimes([new TimeOnly(8, 0), new TimeOnly(9, 0), new TimeOnly(18, 0)]);

        var result = new ExpectedFilePlanner().Plan(rule, new DateOnly(2026, 11, 21));

        Assert.Equal(6, result.Count);
        Assert.Contains(result, x => x.ExpectedFileName == "trade1_20261121_080000.csv");
        Assert.Contains(result, x => x.ExpectedFileName == "trade2_20261121_080000.csv");
        Assert.Contains(result, x => x.ExpectedFileName == "trade1_20261121_090000.csv");
        Assert.Contains(result, x => x.ExpectedFileName == "trade2_20261121_090000.csv");
        Assert.Contains(result, x => x.ExpectedFileName == "trade1_20261121_180000.csv");
        Assert.Contains(result, x => x.ExpectedFileName == "trade2_20261121_180000.csv");
        Assert.Equal(2, result.Count(x => x.ExpectedAtUtc == result.Single(y => y.ExpectedFileName == "trade1_20261121_180000.csv").ExpectedAtUtc));
    }

    [Fact]
    public void Plan_Expands_Hour_Minute_And_Second_Tokens()
    {
        var rule = new MonitoringRule("Hourly", @"D:\\Inbound", MatchingMode.FileNameTemplate);
        rule.ConfigureMatching(MatchingMode.FileNameTemplate,
            "feed_{YYYY}{MM}{DD}_{HH}{MINUTES}{SECONDS}.dat");
        rule.ConfigureCalendar(WeekDays.EveryDay, "UTC");
        rule.SetScheduleTimes([new TimeOnly(9, 7, 5)]);

        var result = Assert.Single(new ExpectedFilePlanner().Plan(rule, new DateOnly(2026, 11, 21)));

        Assert.Equal("feed_20261121_090705.dat", result.ExpectedFileName);
    }

}
