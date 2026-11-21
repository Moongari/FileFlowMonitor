using Xunit;
using FileFlowMonitor.Domain.Entities;
using FileFlowMonitor.Domain.Enums;

namespace FileFlowMonitor.UnitTests;

public sealed class ExpectedFileTests
{
    [Fact]
    public void RefreshStatus_UsesExpectedTimeAndGraceDeadline()
    {
        var expectedAt = new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);
        var file = new ExpectedFile(Guid.NewGuid(), "trade.csv", expectedAt, expectedAt.AddMinutes(15));

        file.RefreshStatus(expectedAt.AddMinutes(-1));
        Assert.Equal(ExpectedFileStatus.Scheduled, file.Status);

        file.RefreshStatus(expectedAt.AddMinutes(10));
        Assert.Equal(ExpectedFileStatus.Waiting, file.Status);

        file.RefreshStatus(expectedAt.AddMinutes(16));
        Assert.Equal(ExpectedFileStatus.Missing, file.Status);
    }

    [Fact]
    public void MarkReceived_ClassifiesLateArrival()
    {
        var expectedAt = new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);
        var file = new ExpectedFile(Guid.NewGuid(), "trade.csv", expectedAt, expectedAt.AddMinutes(15));

        file.MarkReceived(Guid.NewGuid(), expectedAt.AddMinutes(25));

        Assert.Equal(ExpectedFileStatus.Late, file.Status);
    }
    [Fact]
    public void RefreshStatus_DoesNotOverride_ReceivedFile()
    {
        var expectedAt = new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);
        var file = new ExpectedFile(Guid.NewGuid(), "trade.csv", expectedAt, expectedAt.AddMinutes(15));
        file.MarkReceived(Guid.NewGuid(), expectedAt.AddMinutes(5));

        file.RefreshStatus(expectedAt.AddDays(1));

        Assert.Equal(ExpectedFileStatus.Received, file.Status);
    }

    [Fact]
    public void MarkReceived_Classifies_OnTimeArrival()
    {
        var expectedAt = new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);
        var file = new ExpectedFile(Guid.NewGuid(), "trade.csv", expectedAt, expectedAt.AddMinutes(15));

        file.MarkReceived(Guid.NewGuid(), expectedAt.AddMinutes(10));

        Assert.Equal(ExpectedFileStatus.Received, file.Status);
    }

}
