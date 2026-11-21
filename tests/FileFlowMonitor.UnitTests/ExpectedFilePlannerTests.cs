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
    public void Plan_CreatesExpectedSlotsForPatternRule()
    {
        var rule = new MonitoringRule("Citi", @"D:\Inbound", MatchingMode.Pattern);
        rule.ConfigureMatching(MatchingMode.Pattern, @"^trade.*\.csv$", 2);
        rule.ConfigureCalendar(WeekDays.EveryDay, "UTC");
        rule.ScheduleTimes.Add(new ScheduleTime(rule.Id, new TimeOnly(8, 0)));

        var result = new ExpectedFilePlanner().Plan(rule, new DateOnly(2026, 11, 21));

        Assert.Equal(2, result.Count);
        Assert.Equal(["Pattern match 01", "Pattern match 02"], result.Select(x => x.ExpectedFileName));
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

    [Theory]
    [InlineData("trade1_20261121_13000.csv")]
    [InlineData("trade1_20261121_130000.csv")]
    public void MatchesFileName_Accepts_HHmm0_And_HHmmss_Template_Tokens(string fileName)
    {
        var rule = new MonitoringRule("Citi hourly", @"D:\\Inbound", MatchingMode.FileNameTemplate);
        rule.ConfigureMatching(MatchingMode.FileNameTemplate, "trade1_{DATE}_{TIME}.csv");
        rule.ConfigureCalendar(WeekDays.EveryDay, "UTC");
        rule.SetScheduleTimes([new TimeOnly(13, 0)]);

        var expected = Assert.Single(new ExpectedFilePlanner().Plan(rule, new DateOnly(2026, 11, 21)));

        Assert.True(new ExpectedFilePlanner().MatchesFileName(rule, expected, fileName));
    }

    [Fact]
    public void MatchesFileName_ExactName_Remains_Strict()
    {
        var rule = new MonitoringRule("Exact feed", @"D:\\Inbound", MatchingMode.ExactName);
        rule.SetExpectedFileNames(["Trade1_20261121_13000.csv"]);
        rule.ConfigureCalendar(WeekDays.EveryDay, "UTC");
        rule.SetScheduleTimes([new TimeOnly(13, 0)]);
        var expected = Assert.Single(new ExpectedFilePlanner().Plan(rule, new DateOnly(2026, 11, 21)));

        Assert.True(new ExpectedFilePlanner().MatchesFileName(rule, expected, "trade1_20261121_13000.csv"));
        Assert.False(new ExpectedFilePlanner().MatchesFileName(rule, expected, "trade1_20261121_130000.csv"));
    }

    [Fact]
    public void Plan_Pattern_Creates_Expected_Count_And_Matches_Regex()
    {
        var rule = new MonitoringRule("Pattern feed", @"D:\\Inbound", MatchingMode.Pattern);
        rule.ConfigureMatching(MatchingMode.Pattern, @"^Trade[12]_\d{8}_\d{5}\.csv$", 2);
        rule.SetAllowedExtensions(["csv"]);
        rule.ConfigureCalendar(WeekDays.EveryDay, "UTC");
        rule.SetScheduleTimes([new TimeOnly(13, 0)]);

        var expected = new ExpectedFilePlanner().Plan(rule, new DateOnly(2026, 11, 21));

        Assert.Equal(2, expected.Count);
        Assert.True(new ExpectedFilePlanner().MatchesFileName(rule, expected[0], "Trade1_20261121_13000.csv"));
        Assert.True(new ExpectedFilePlanner().MatchesFileName(rule, expected[1], "Trade2_20261121_13000.csv"));
        Assert.False(new ExpectedFilePlanner().MatchesFileName(rule, expected[0], "Trade1_20261121_13000.txt"));
    }

    [Fact]
    public void Plan_Quantity_Creates_Expected_Count_And_Filters_Extensions()
    {
        var rule = new MonitoringRule("Quantity feed", @"D:\\Inbound", MatchingMode.Quantity);
        rule.ConfigureMatching(MatchingMode.Quantity, expectedCount: 3);
        rule.SetAllowedExtensions(["csv", "xlsx"]);
        rule.ConfigureCalendar(WeekDays.EveryDay, "UTC");
        rule.SetScheduleTimes([new TimeOnly(13, 0)]);

        var expected = new ExpectedFilePlanner().Plan(rule, new DateOnly(2026, 11, 21));

        Assert.Equal(3, expected.Count);
        Assert.True(new ExpectedFilePlanner().MatchesFileName(rule, expected[0], "arbitrary-name.csv"));
        Assert.True(new ExpectedFilePlanner().MatchesFileName(rule, expected[1], "another.xlsx"));
        Assert.False(new ExpectedFilePlanner().MatchesFileName(rule, expected[2], "ignored.pdf"));
    }

    [Fact]
    public void ConfigureMatching_Rejects_Invalid_Regex()
    {
        var rule = new MonitoringRule("Invalid pattern", @"D:\\Inbound", MatchingMode.Pattern);

        Assert.Throws<ArgumentException>(() =>
            rule.ConfigureMatching(MatchingMode.Pattern, "[invalid"));
    }

}
