using FileFlowMonitor.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FileFlowMonitor.IntegrationTests;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public async Task Database_CanCreateSchema_AndQueryMonitoringTables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<FileFlowDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new FileFlowDbContext(options);

        var created = await dbContext.Database.EnsureCreatedAsync();

        Assert.True(created);
        Assert.Equal(0, await dbContext.MonitoringRules.CountAsync());
        Assert.Equal(0, await dbContext.ExpectedFiles.CountAsync());
        Assert.Equal(0, await dbContext.ReceivedFiles.CountAsync());
        Assert.Equal(0, await dbContext.MonitoringIncidents.CountAsync());
        Assert.Equal(0, await dbContext.MonitoringExecutions.CountAsync());
    }
}
