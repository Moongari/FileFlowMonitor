using FileFlowMonitor.Application.ExpectedFiles;
using FileFlowMonitor.Application.MonitoringRules;
using FileFlowMonitor.Domain.Enums;
using FileFlowMonitor.Infrastructure.Monitoring;
using FileFlowMonitor.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FileFlowMonitor.IntegrationTests;

public sealed class MonitoringEngineTests
{
    [Fact]
    public async Task Cycle_Discovers_Stabilizes_And_Matches_Expected_File_Persistently()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FileFlowMonitor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var filePath = Path.Combine(folder, "trades.csv");
        await File.WriteAllTextAsync(filePath, "tradeId,amount\n1,100");

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FileFlowDbContext>().UseSqlite(connection).Options;

        try
        {
            Guid ruleId;
            await using (var setup = new FileFlowDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                var service = new MonitoringRuleService(new MonitoringRuleRepository(setup));
                var schedule = TimeOnly.FromDateTime(DateTime.UtcNow.AddMinutes(-1));
                var rule = await service.CreateAsync(new MonitoringRuleConfiguration(
                    "Citi test feed", folder, MatchingMode.ExactName, null, 1,
                    ["TRADES.csv"], ["csv"], [schedule], WeekDays.EveryDay,
                    TimeZoneInfo.Utc.Id, 15, 0, true));
                ruleId = rule.Id;
                Assert.Single(rule.ScheduleTimes);
                Assert.Single(new ExpectedFilePlanner().Plan(rule, DateOnly.FromDateTime(DateTime.UtcNow)));
            }

            // First cycle creates the expected occurrence and records the new file.
            await using (var db = new FileFlowDbContext(options))
                await new MonitoringEngineCycle(db, new ExpectedFilePlanner()).ExecuteAsync();

            // A subsequent cycle confirms stable metadata and links the file to its expectation.
            await using (var db = new FileFlowDbContext(options))
                await new MonitoringEngineCycle(db, new ExpectedFilePlanner()).ExecuteAsync();

            await using (var verify = new FileFlowDbContext(options))
            {
                var executions = await verify.MonitoringExecutions.Where(x => x.MonitoringRuleId == ruleId).ToListAsync();
                Assert.Equal(2, executions.Count);
                Assert.All(executions, x => Assert.Null(x.Error));
                var expectedFiles = await verify.ExpectedFiles
                    .Where(x => x.MonitoringRuleId == ruleId)
                    .OrderBy(x => x.ExpectedAtUtc)
                    .ToListAsync();
                var expected = Assert.Single(expectedFiles.Where(x => x.ExpectedAtUtc <= DateTimeOffset.UtcNow));
                var nextOccurrence = Assert.Single(expectedFiles.Where(x => x.ExpectedAtUtc > DateTimeOffset.UtcNow));
                var received = await verify.ReceivedFiles.SingleAsync(x => x.MonitoringRuleId == ruleId);
                Assert.Equal(2, expectedFiles.Count);
                Assert.Equal(ExpectedFileStatus.Received, expected.Status);
                Assert.Equal(ExpectedFileStatus.Scheduled, nextOccurrence.Status);
                Assert.Equal(received.Id, expected.ReceivedFileId);
                Assert.NotNull(received.StableAtUtc);
                Assert.Equal(2, await verify.MonitoringExecutions.CountAsync(x => x.MonitoringRuleId == ruleId));
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
