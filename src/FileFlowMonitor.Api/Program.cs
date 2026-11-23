using FileFlowMonitor.Api.Endpoints;
using FileFlowMonitor.Api.Services;
using FileFlowMonitor.Application.MonitoringRules;
using FileFlowMonitor.Infrastructure;
using FileFlowMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:5080");

// SQLite reste le fournisseur de persistance du MVP.
var connectionString = builder.Configuration.GetConnectionString("FileFlow") ?? "Data Source=fileflowmonitor.db";
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddScoped<MonitoringRuleService>();
builder.Services.AddHostedService<MonitoringBackgroundService>();
builder.Services.AddHostedService<FileArrivalWatcherService>();

// Swagger expose le contrat OpenAPI et les commentaires XML en développement.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
});

var app = builder.Build();

// Le MVP initialise le schéma SQLite au premier démarrage. Lorsque des migrations
// EF Core seront introduites, ce bloc sera remplacé par Database.MigrateAsync().
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FileFlowDbContext>();
    await db.Database.EnsureCreatedAsync();

    // Additive schema upgrade: EnsureCreated does not evolve databases created by earlier releases.
    // CREATE TABLE IF NOT EXISTS preserves all existing monitoring data.
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS MonitoringIncidentWorkflows (
            MonitoringIncidentId TEXT NOT NULL CONSTRAINT PK_MonitoringIncidentWorkflows PRIMARY KEY,
            Status TEXT NOT NULL,
            Severity TEXT NOT NULL,
            AssignedTo TEXT NULL,
            AcknowledgedAtUtc TEXT NULL,
            ResolvedAtUtc TEXT NULL,
            ClosedAtUtc TEXT NULL,
            ResolutionComment TEXT NULL,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_MonitoringIncidentWorkflows_Status_Severity
            ON MonitoringIncidentWorkflows (Status, Severity);
        CREATE TABLE IF NOT EXISTS MonitoringIncidentHistories (
            Id TEXT NOT NULL CONSTRAINT PK_MonitoringIncidentHistories PRIMARY KEY,
            MonitoringIncidentId TEXT NOT NULL,
            Action TEXT NOT NULL,
            Details TEXT NULL,
            OccurredAtUtc TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_MonitoringIncidentHistories_MonitoringIncidentId_OccurredAtUtc
            ON MonitoringIncidentHistories (MonitoringIncidentId, OccurredAtUtc);
        """);

}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "FileFlow Monitor API v1");
        options.RoutePrefix = "swagger";
    });
}

// Santé HTTP : confirme que le processus API répond.
app.MapGet("/health", () => Results.Ok(new { status = "ok", application = "FileFlow Monitor" }))
    .WithTags("Health").WithName("GetHealth")
    .WithSummary("Vérifier que l'API répond")
    .WithDescription("Retourne un statut OK lorsque le processus API est opérationnel.")
    .Produces(StatusCodes.Status200OK);

// Santé SQLite : teste la connexion sans exposer la chaîne de connexion.
app.MapGet("/health/database", async (FileFlowDbContext db, CancellationToken ct) =>
{
    var canConnect = await db.Database.CanConnectAsync(ct);
    return canConnect ? Results.Ok(new { status = "ok" }) : Results.Problem("Database unavailable.");
})
.WithTags("Health").WithName("GetDatabaseHealth")
.WithSummary("Vérifier la connexion à SQLite")
.WithDescription("Teste l'accessibilité de la base de données configurée.")
.Produces(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status500InternalServerError);

app.MapMonitoringRuleEndpoints();
app.MapMonitoringReadEndpoints();
app.MapMonitoringIncidentEndpoints();
app.MapMonitoringAdminEndpoints();
app.Run();

/// <summary>Point d'entrée exposé aux tests d'intégration via WebApplicationFactory.</summary>
public partial class Program { }
