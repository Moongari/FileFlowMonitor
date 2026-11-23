using FileFlowMonitor.Application.ExpectedFiles;
using FileFlowMonitor.Application.MonitoringRules;
using FileFlowMonitor.Domain.Enums;
using FileFlowMonitor.Domain.Entities;
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
                var currentOccurrence = Assert.Single(persisted, x => x.ExpectedAtUtc <= DateTimeOffset.UtcNow);
                Assert.Equal(ExpectedFileStatus.Received, currentOccurrence.Status);
                Assert.NotNull(currentOccurrence.ReceivedFileId);
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
                var expected = Assert.Single(expectedFiles, x => x.ExpectedAtUtc <= DateTimeOffset.UtcNow);
                var nextOccurrence = Assert.Single(expectedFiles, x => x.ExpectedAtUtc > DateTimeOffset.UtcNow);
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
    public async Task Stable_File_Is_Reconciled_When_Expectation_Was_Already_Missing()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FileFlowMonitor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        const string fileName = "trade2_20261121_180000.csv";
        var path = Path.Combine(folder, fileName);
        await File.WriteAllTextAsync(path, "data");

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FileFlowDbContext>().UseSqlite(connection).Options;
        try
        {
            Guid ruleId;
            Guid expectedId;
            await using (var setup = new FileFlowDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                var service = new MonitoringRuleService(new MonitoringRuleRepository(setup));
                var rule = await service.CreateAsync(new MonitoringRuleConfiguration(
                    "Citi stable reconciliation", folder, MatchingMode.ExactName, null, 1,
                    [fileName], ["csv"], [TimeOnly.FromDateTime(DateTime.UtcNow.AddHours(6))], WeekDays.EveryDay,
                    TimeZoneInfo.Utc.Id, 0, 0, true));
                ruleId = rule.Id;

                var expectedAt = DateTimeOffset.UtcNow.AddHours(-2);
                var expected = new ExpectedFile(rule.Id, fileName, expectedAt, expectedAt.AddMinutes(1));
                expected.RefreshStatus(expectedAt.AddMinutes(2));
                expectedId = expected.Id;
                setup.ExpectedFiles.Add(expected);
                setup.MonitoringIncidents.Add(new MonitoringIncident(rule.Id, expected.Id,
                    "EXPECTED_FILE_MISSING", "Test missing expected file", DateTimeOffset.UtcNow.AddMinutes(-1)));

                var info = new FileInfo(path);
                var received = new ReceivedFile(rule.Id, path, fileName, info.Length,
                    new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), DateTimeOffset.UtcNow.AddMinutes(-1));
                received.MarkStable(DateTimeOffset.UtcNow.AddSeconds(-30));
                setup.ReceivedFiles.Add(received);
                await setup.SaveChangesAsync();
            }

            await using (var cycle = new FileFlowDbContext(options))
                await new MonitoringEngineCycle(cycle, new ExpectedFilePlanner()).ExecuteAsync();

            await using (var verify = new FileFlowDbContext(options))
            {
                var expected = await verify.ExpectedFiles.SingleAsync(x => x.Id == expectedId);
                Assert.Equal(ExpectedFileStatus.Late, expected.Status);
                Assert.NotNull(expected.ReceivedFileId);
                var incidents = await verify.MonitoringIncidents.Where(x =>
                    x.MonitoringRuleId == ruleId && x.ExpectedFileId == expectedId &&
                    x.Code == "EXPECTED_FILE_MISSING").ToListAsync();
                Assert.NotEmpty(incidents);
                Assert.All(incidents, x => Assert.NotNull(x.ResolvedAtUtc));
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

    [Fact]
    public async Task ReEnabling_Unchanged_Rule_Reschedules_Cancelled_Occurrence()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FileFlowMonitor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FileFlowDbContext>().UseSqlite(connection).Options;

        try
        {
            await using var db = new FileFlowDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var service = new MonitoringRuleService(new MonitoringRuleRepository(db));
            var scheduledAt = DateTimeOffset.UtcNow.AddMinutes(10);
            var schedule = new TimeOnly(scheduledAt.Hour, scheduledAt.Minute, 0);
            var businessDate = DateOnly.FromDateTime(scheduledAt.UtcDateTime);
            var rule = await service.CreateAsync(new MonitoringRuleConfiguration(
                "Re-enable feed", folder, MatchingMode.ExactName, null, 1,
                ["future.csv"], ["csv"], [schedule], WeekDays.EveryDay,
                TimeZoneInfo.Utc.Id, 15, 0, true));
            var occurrence = Assert.Single(new ExpectedFilePlanner().Plan(rule, businessDate));
            db.ExpectedFiles.Add(occurrence);
            await db.SaveChangesAsync();

            await service.SetEnabledAsync(rule.Id, false);
            Assert.Equal(ExpectedFileStatus.Cancelled, occurrence.Status);

            await service.SetEnabledAsync(rule.Id, true);
            await new MonitoringEngineCycle(db, new ExpectedFilePlanner()).ExecuteAsync();

            Assert.Equal(ExpectedFileStatus.Scheduled, occurrence.Status);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }


    [Fact]
    public async Task Missing_Expected_File_Creates_One_Linked_Open_Incident()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FileFlowMonitor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FileFlowDbContext>().UseSqlite(connection).Options;

        try
        {
            Guid ruleId;
            Guid expectedId;
            await using (var setup = new FileFlowDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                var service = new MonitoringRuleService(new MonitoringRuleRepository(setup));
                var rule = await service.CreateAsync(new MonitoringRuleConfiguration(
                    "Missing file test", folder, MatchingMode.ExactName, null, 1,
                    ["missing.csv"], ["csv"], [new TimeOnly(0, 0)], WeekDays.EveryDay,
                    TimeZoneInfo.Utc.Id, 0, 0, true));
                ruleId = rule.Id;
                var expectedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
                var expected = new ExpectedFile(rule.Id, "missing.csv", expectedAt, expectedAt.AddMinutes(1));
                expectedId = expected.Id;
                setup.ExpectedFiles.Add(expected);
                await setup.SaveChangesAsync();
            }

            await using (var cycleDb = new FileFlowDbContext(options))
                await new MonitoringEngineCycle(cycleDb, new ExpectedFilePlanner()).ExecuteAsync();

            await using (var verify = new FileFlowDbContext(options))
            {
                var expected = await verify.ExpectedFiles.SingleAsync(x => x.Id == expectedId);
                Assert.Equal(ExpectedFileStatus.Missing, expected.Status);
                var incident = await verify.MonitoringIncidents.SingleAsync(x =>
                    x.ExpectedFileId == expectedId && x.Code == "EXPECTED_FILE_MISSING");
                Assert.Equal(ruleId, incident.MonitoringRuleId);
                Assert.Null(incident.ResolvedAtUtc);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Cycle_Reconciles_Legacy_HHmm0_FileName_From_Template()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FileFlowMonitor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FileFlowDbContext>().UseSqlite(connection).Options;

        try
        {
            var scheduledAt = DateTimeOffset.UtcNow.AddMinutes(-2);
            var dateToken = scheduledAt.ToString("yyyyMMdd");
            var schedule = new TimeOnly(scheduledAt.Hour, scheduledAt.Minute);
            var fileName = $"Trade1_{dateToken}_{schedule:HHmm}0.csv";
            await File.WriteAllTextAsync(Path.Combine(folder, fileName), "tradeId,amount\n1,100");

            Guid ruleId;
            await using (var setup = new FileFlowDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                var service = new MonitoringRuleService(new MonitoringRuleRepository(setup));
                var rule = await service.CreateAsync(new MonitoringRuleConfiguration(
                    "Citi template feed", folder, MatchingMode.FileNameTemplate,
                    "Trade1_{DATE}_{TIME}.csv", 1, [], ["csv"], [schedule],
                    WeekDays.EveryDay, TimeZoneInfo.Utc.Id, 15, 0, true));
                ruleId = rule.Id;
            }

            await using (var cycle = new FileFlowDbContext(options))
                await new MonitoringEngineCycle(cycle, new ExpectedFilePlanner()).ExecuteAsync();

            await using (var verify = new FileFlowDbContext(options))
            {
                var expected = Assert.Single((await verify.ExpectedFiles
                    .Where(x => x.MonitoringRuleId == ruleId)
                    .ToListAsync()).Where(x => x.ExpectedAtUtc <= DateTimeOffset.UtcNow));
                Assert.Equal(fileName.Replace($"{dateToken}_{schedule:HHmm}0", $"{dateToken}_{schedule:HHmmss}"),
                    expected.ExpectedFileName);
                Assert.Equal(ExpectedFileStatus.Received, expected.Status);
                Assert.NotNull(expected.ReceivedFileId);
                Assert.Single(await verify.ReceivedFiles.Where(x => x.MonitoringRuleId == ruleId).ToListAsync());
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

}
