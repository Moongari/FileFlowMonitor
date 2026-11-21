using FileFlowMonitor.Api.Endpoints;
using FileFlowMonitor.Application.MonitoringRules;
using FileFlowMonitor.Infrastructure;
using FileFlowMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// SQLite reste le fournisseur de persistance du MVP.
var connectionString = builder.Configuration.GetConnectionString("FileFlow") ?? "Data Source=fileflowmonitor.db";
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddScoped<MonitoringRuleService>();

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
app.Run();

/// <summary>Point d'entrée exposé aux tests d'intégration via WebApplicationFactory.</summary>
public partial class Program { }
