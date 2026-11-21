using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FileFlowMonitor.Infrastructure.Persistence;
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
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
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
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        }
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
