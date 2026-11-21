using FileFlowMonitor.Application.MonitoringRules;
using FileFlowMonitor.Domain.Enums;
using FileFlowMonitor.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FileFlowMonitor.IntegrationTests;

public sealed class MonitoringRuleCrudTests
{
    [Fact]
    public async Task Create_Read_Update_Enable_And_Delete_Rule_Persisted_In_Sqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<FileFlowDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new FileFlowDbContext(options))
            await setup.Database.EnsureCreatedAsync();

        Guid id;
        await using (var db = new FileFlowDbContext(options))
        {
            var service = new MonitoringRuleService(new Persistence.MonitoringRuleRepository(db));
            var created = await service.CreateAsync(new MonitoringRuleConfiguration(
                "Citi inbound",
                @"D:\\FileFlow\\Citi",
                MatchingMode.ExactName,
                null,
                1,
                ["trades.csv", "positions.csv"],
                [".CSV", "pdf"],
                [new TimeOnly(8, 0), new TimeOnly(12, 0)],
                WeekDays.Weekdays,
                TimeZoneInfo.Utc.Id,
                15,
                5,
                true));

            id = created.Id;
            Assert.Equal(2, created.ExpectedFileNames.Count);
            Assert.Equal(2, created.ScheduleTimes.Count);
        }

        await using (var db = new FileFlowDbContext(options))
        {
            var service = new MonitoringRuleService(new Persistence.MonitoringRuleRepository(db));
            var loaded = await service.GetByIdAsync(id);

            Assert.NotNull(loaded);
            Assert.Equal("Citi inbound", loaded.Name);
            Assert.Equal(["trades.csv", "positions.csv"], loaded.ExpectedFileNames);
            Assert.Equal(["csv", "pdf"], loaded.AllowedExtensions.OrderBy(x => x).ToArray());
            Assert.Equal(2, loaded.ScheduleTimes.Count);

            var updated = await service.UpdateAsync(id, new MonitoringRuleConfiguration(
                "Citi daily",
                @"D:\\FileFlow\\Citi\\Archive",
                MatchingMode.ExactName,
                null,
                1,
                ["trades.csv"],
                ["csv"],
                [new TimeOnly(9, 30)],
                WeekDays.EveryDay,
                TimeZoneInfo.Utc.Id,
                20,
                10,
                true));

            Assert.NotNull(updated);
            Assert.Equal("Citi daily", updated.Name);
            Assert.Single(updated.ScheduleTimes);
            Assert.True(await service.SetEnabledAsync(id, false));
        }

        await using (var db = new FileFlowDbContext(options))
        {
            var service = new MonitoringRuleService(new Persistence.MonitoringRuleRepository(db));
            var loaded = await service.GetByIdAsync(id);
            Assert.NotNull(loaded);
            Assert.False(loaded.IsEnabled);
            Assert.Equal(@"D:\\FileFlow\\Citi\\Archive", loaded.FolderPath);
            Assert.True(await service.DeleteAsync(id));
            Assert.Null(await service.GetByIdAsync(id));
        }
    }
}
