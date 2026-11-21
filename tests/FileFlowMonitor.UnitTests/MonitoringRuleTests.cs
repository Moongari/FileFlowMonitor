using Xunit;
using FileFlowMonitor.Domain.Entities;
using FileFlowMonitor.Domain.Enums;

namespace FileFlowMonitor.UnitTests;

public sealed class MonitoringRuleTests
{
    [Fact]
    public void Rule_RequiresNameAndFolder()
    {
        Assert.Throws<ArgumentException>(() => new MonitoringRule(" ", @"D:\Inbound", MatchingMode.ExactName));
        Assert.Throws<ArgumentException>(() => new MonitoringRule("Citi", " ", MatchingMode.ExactName));
    }

    [Fact]
    public void Rule_StoresExpectedNamesWithoutDuplicates()
    {
        var rule = new MonitoringRule("Citi", @"D:\Inbound", MatchingMode.ExactName);
        rule.SetExpectedFileNames(["trades.csv", "TRADES.csv", "positions.csv"]);

        Assert.Equal(2, rule.ExpectedFileNames.Count);
    }
}
