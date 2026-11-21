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
                var utcNow = DateTime.UtcNow;
                var schedule = new TimeOnly(utcNow.Hour, utcNow.Minute, 0);
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

            // Verify the first cycle persisted today's and tomorrow's occurrences.
            await using (var check = new FileFlowDbContext(options))
            {
                var persisted = await check.ExpectedFiles.Where(x => x.MonitoringRuleId == ruleId).ToListAsync();
                Assert.Equal(2, persisted.Count);
                Assert.Equal(2, persisted.Select(x => x.ExpectedAtUtc.UtcDateTime.Ticks).Distinct().Count());
            }

            // A subsequent cycle confirms stable metadata and links the file to its expectation.
            await using (var db = new FileFlowDbContext(options))
                await new MonitoringEngineCycle(db, new ExpectedFilePlanner()).ExecuteAsync();

            await using (var verify = new FileFlowDbContext(options))
            {
                var executions = await verify.MonitoringExecutions.Where(x => x.MonitoringRuleId == ruleId).ToListAsync();
                Assert.Equal(2, executions.Count);
                Assert.All(executions, x => Assert.Null(x.Error));
                var expectedFiles = (await verify.ExpectedFiles
                    .Where(x => x.MonitoringRuleId == ruleId)
                    .ToListAsync()).OrderBy(x => x.ExpectedAtUtc).ToList();
                var expected = Assert.Single(expectedFiles.Where(x => x.ExpectedAtUtc <= DateTimeOffset.UtcNow));
                var nextOccurrence = Assert.Single(expectedFiles.Where(x => x.ExpectedAtUtc > DateTimeOffset.UtcNow));
                var received = await verify.ReceivedFiles.SingleAsync(x => x.MonitoringRuleId == ruleId);
                Assert.Equal(2, expectedFiles.Count);
                Assert.NotNull(received.StableAtUtc);
                Assert.Equal(ExpectedFileStatus.Received, expected.Status);
                Assert.Equal(ExpectedFileStatus.Scheduled, nextOccurrence.Status);
                Assert.Equal(received.Id, expected.ReceivedFileId);
                Assert.Equal(2, await verify.MonitoringExecutions.CountAsync(x => x.MonitoringRuleId == ruleId));
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
    [Fact]
    public async Task Folder_Unavailable_Incident_Is_Resolved_When_Folder_Returns()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FileFlowMonitor.Tests", Guid.NewGuid().ToString("N"));
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
                var rule = await service.CreateAsync(new MonitoringRuleConfiguration(
                    "Unavailable source", folder, MatchingMode.ExactName, null, 1,
                    ["daily.csv"], ["csv"], [new TimeOnly(8, 0)], WeekDays.EveryDay,
                    TimeZoneInfo.Utc.Id, 15, 0, true));
                ruleId = rule.Id;
            }

            await using (var db = new FileFlowDbContext(options))
                await new MonitoringEngineCycle(db, new ExpectedFilePlanner()).ExecuteAsync();

            await using (var verify = new FileFlowDbContext(options))
            {
                var incident = await verify.MonitoringIncidents.SingleAsync(x =>
                    x.MonitoringRuleId == ruleId && x.Code == "FOLDER_UNAVAILABLE");
                Assert.False(incident.IsResolved);
            }

            Directory.CreateDirectory(folder);
            await using (var db = new FileFlowDbContext(options))
                await new MonitoringEngineCycle(db, new ExpectedFilePlanner()).ExecuteAsync();

            await using (var verify = new FileFlowDbContext(options))
            {
                var incident = await verify.MonitoringIncidents.SingleAsync(x =>
                    x.MonitoringRuleId == ruleId && x.Code == "FOLDER_UNAVAILABLE");
                Assert.True(incident.IsResolved);
            }
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Updating_Rule_Cancels_Obsolete_Future_Expectations()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FileFlowMonitor.Tests", Guid.NewGuid().ToString("N"));
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FileFlowDbContext>().UseSqlite(connection).Options;

        await using var db = new FileFlowDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var service = new MonitoringRuleService(new MonitoringRuleRepository(db));
        var initial = new MonitoringRuleConfiguration(
            "Daily feed", folder, MatchingMode.ExactName, null, 1,
            ["old.csv"], ["csv"], [new TimeOnly(8, 0)], WeekDays.EveryDay,
            TimeZoneInfo.Utc.Id, 15, 5, true);
        var rule = await service.CreateAsync(initial);
        var futureAt = DateTimeOffset.UtcNow.AddHours(3);
        var oldOccurrence = new ExpectedFile(rule.Id, "old.csv", futureAt, futureAt.AddMinutes(15));
        db.ExpectedFiles.Add(oldOccurrence);
        await db.SaveChangesAsync();

        var updated = initial with { ExpectedFileNames = ["new.csv"] };
        await service.UpdateAsync(rule.Id, updated);

        var persisted = await db.ExpectedFiles.SingleAsync(x => x.Id == oldOccurrence.Id);
        Assert.Equal(ExpectedFileStatus.Cancelled, persisted.Status);
    }

}
