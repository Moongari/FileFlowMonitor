using FileFlowMonitor.Infrastructure;
using Microsoft.EntityFrameworkCore;
using FileFlowMonitor.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("FileFlow")
    ?? "Data Source=fileflowmonitor.db";

builder.Services.AddInfrastructure(connectionString);

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok", application = "FileFlow Monitor" }));
app.MapGet("/health/database", async (FileFlowDbContext db, CancellationToken ct) =>
{
    var canConnect = await db.Database.CanConnectAsync(ct);
    return canConnect ? Results.Ok(new { status = "ok" }) : Results.Problem("Database unavailable.");
});

app.Run();

public partial class Program { }
