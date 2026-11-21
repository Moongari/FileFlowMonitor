using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FileFlowMonitor.Infrastructure.Persistence;
using FileFlowMonitor.Domain.Entities;
using FileFlowMonitor.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FileFlowMonitor.IntegrationTests;

/// <summary>Valide les routes HTTP réelles et leur persistance sur SQLite en mémoire.</summary>
public sealed class MonitoringRuleApiTests
{
    [Fact]
    public async Task Monitoring_Rule_Endpoints_Expose_Crud_And_Swagger_Documentation()
    {
        await using var factory = new MonitoringRuleApiFactory();
        using var client = factory.CreateClient();

        // Initialise le schéma SQLite de test avant d'appeler les endpoints.
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<FileFlowDbContext>().Database.EnsureCreatedAsync();

        var swagger = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        var swaggerDocument = JsonDocument.Parse(await swagger.Content.ReadAsStringAsync());
        var paths = swaggerDocument.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/monitoring/overview", out _));
        Assert.True(paths.TryGetProperty("/api/monitoring/expected-files", out _));
        Assert.Contains("generatedAtUtc", paths.GetProperty("/api/monitoring/overview")
            .GetProperty("get").GetProperty("description").GetString());
        Assert.Contains("trades.csv", paths.GetProperty("/api/monitoring/expected-files")
            .GetProperty("get").GetProperty("description").GetString());
        var createDescription = paths.GetProperty("/api/monitoring-rules").GetProperty("post")
            .GetProperty("description").GetString();
        Assert.Contains("Citi - fichiers de trading", createDescription);
        Assert.Contains("expectedFileNames", createDescription);

        var payload = new
        {
            name = "Citi API test",
            folderPath = @"C:\FileFlow\Citi",
            matchingMode = 0,
            fileNamePattern = (string?)null,
            expectedCount = 1,
            expectedFileNames = new[] { "trades.csv", "positions.csv" },
            allowedExtensions = new[] { "csv", "pdf" },
            scheduleTimes = new[] { "08:00:00", "12:00:00" },
            days = 31,
            timeZoneId = "UTC",
            gracePeriodMinutes = 15,
            stabilityPeriodSeconds = 5,
            isEnabled = true
        };

        var create = await client.PostAsJsonAsync("/api/monitoring-rules", payload);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var createdJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = createdJson.RootElement.GetProperty("id").GetGuid();

        var get = await client.GetAsync($"/api/monitoring-rules/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var updatePayload = new
        {
            name = "Citi API test updated", payload.folderPath, payload.matchingMode, payload.fileNamePattern,
            payload.expectedCount, payload.expectedFileNames, payload.allowedExtensions,
            scheduleTimes = new[] { "09:30:00" }, payload.days, payload.timeZoneId,
            gracePeriodMinutes = 20, payload.stabilityPeriodSeconds, payload.isEnabled
        };
        var update = await client.PutAsJsonAsync($"/api/monitoring-rules/{id}", updatePayload);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var invalid = await client.PostAsJsonAsync("/api/monitoring-rules", new { name = "", folderPath = "" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var patch = await client.PatchAsJsonAsync($"/api/monitoring-rules/{id}/enabled", new { isEnabled = false });
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/monitoring-rules");
        Assert.Equal(1, list.GetArrayLength());
        Assert.False(list[0].GetProperty("isEnabled").GetBoolean());

        var delete = await client.DeleteAsync($"/api/monitoring-rules/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var missing = await client.GetAsync($"/api/monitoring-rules/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Monitoring_Read_Endpoints_Return_Usable_Empty_Collections()
    {
        await using var factory = new MonitoringRuleApiFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<FileFlowDbContext>().Database.EnsureCreatedAsync();

        var overview = await client.GetAsync("/api/monitoring/overview");
        Assert.True(overview.IsSuccessStatusCode, $"overview returned {(int)overview.StatusCode}: {await overview.Content.ReadAsStringAsync()}");
        using var overviewJson = JsonDocument.Parse(await overview.Content.ReadAsStringAsync());
        Assert.Equal(0, overviewJson.RootElement.GetProperty("rulesTotal").GetInt32());

        foreach (var endpoint in new[]
        {
            "/api/monitoring/expected-files",
            "/api/monitoring/received-files",
            "/api/monitoring/incidents",
            "/api/monitoring/executions"
        })
        {
            var response = await client.GetAsync(endpoint);
            Assert.True(response.IsSuccessStatusCode, $"{endpoint} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        }
    }


    [Fact]
    public async Task Expected_Files_Are_Filtered_By_Rule_Local_Business_Date()
    {
        await using var factory = new MonitoringRuleApiFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FileFlowDbContext>();
        await db.Database.EnsureCreatedAsync();

        var rule = new MonitoringRule("Paris midnight feed", @"C:\\Inbound", MatchingMode.ExactName);
        rule.SetExpectedFileNames(["midnight.csv"]);
        rule.ConfigureCalendar(WeekDays.EveryDay, "Europe/Paris");
        rule.SetScheduleTimes([new TimeOnly(0, 30)]);
        rule.SetEnabled(false);
        db.MonitoringRules.Add(rule);
        db.ExpectedFiles.Add(new ExpectedFile(
            rule.Id, "midnight.csv",
            new DateTimeOffset(2026, 11, 20, 23, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 20, 23, 45, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var response = await client.GetAsync("/api/monitoring/expected-files?date=2026-11-21");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal("midnight.csv", rows[0].GetProperty("expectedFileName").GetString());
    }


    [Fact]
    public async Task Overview_Returns_Daily_Summary_Per_Rule_And_Expected_File_Details()
    {
        await using var factory = new MonitoringRuleApiFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FileFlowDbContext>();
        await db.Database.EnsureCreatedAsync();

        var rule = new MonitoringRule("Citi daily", @"C:\\Inbound\\Citi", MatchingMode.ExactName);
        rule.SetExpectedFileNames(["trade1_20261121_180000.csv", "trade2_20261121_180000.csv"]);
        rule.ConfigureCalendar(WeekDays.EveryDay, "Europe/Paris");
        rule.SetScheduleTimes([new TimeOnly(18, 0)]);
        rule.SetEnabled(true);
        db.MonitoringRules.Add(rule);

        var expectedAt = new DateTimeOffset(2026, 11, 21, 17, 0, 0, TimeSpan.Zero);
        var received = new ExpectedFile(rule.Id, "trade1_20261121_180000.csv", expectedAt, expectedAt.AddMinutes(15));
        received.MarkReceived(Guid.NewGuid(), expectedAt.AddMinutes(2));
        var missing = new ExpectedFile(rule.Id, "trade2_20261121_180000.csv", expectedAt, expectedAt.AddMinutes(15));
        missing.RefreshStatus(expectedAt.AddMinutes(16));
        db.ExpectedFiles.AddRange(received, missing);
        await db.SaveChangesAsync();

        var response = await client.GetAsync("/api/monitoring/overview?date=2026-11-21");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(2, root.GetProperty("expectedToday").GetInt32());
        Assert.Equal(1, root.GetProperty("receivedToday").GetInt32());
        Assert.Equal(1, root.GetProperty("missingToday").GetInt32());

        var summary = Assert.Single(root.GetProperty("ruleSummaries").EnumerateArray());
        Assert.Equal("Citi daily", summary.GetProperty("name").GetString());
        Assert.Equal(1, summary.GetProperty("missing").GetInt32());
        Assert.Equal("Attention", summary.GetProperty("healthStatus").GetString());

        var rows = root.GetProperty("expectedFiles");
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Contains(rows.EnumerateArray(), row =>
            row.GetProperty("expectedFileName").GetString() == "trade2_20261121_180000.csv" &&
            row.GetProperty("status").GetString() == "Missing");
    }

    /// <summary>Remplace la connexion SQLite de l'application par une base en mémoire partagée.</summary>
    private sealed class MonitoringRuleApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            _connection.Open();

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<FileFlowDbContext>();
                services.RemoveAll<DbContextOptions<FileFlowDbContext>>();
                services.AddSingleton(_connection);
                services.AddDbContext<FileFlowDbContext>(options => options.UseSqlite(_connection));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }
    }
}
