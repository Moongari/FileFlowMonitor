using System.ComponentModel.DataAnnotations;
using FileFlowMonitor.Api.Contracts;
using FileFlowMonitor.Application.MonitoringRules;

namespace FileFlowMonitor.Api.Endpoints;

/// <summary>
/// Déclare les routes HTTP de gestion des règles de surveillance.
/// Les opérations délèguent les règles métier au service Application.
/// </summary>
public static class MonitoringRuleEndpoints
{
    /// <summary>Enregistre l'ensemble des endpoints CRUD sous /api/monitoring-rules.</summary>
    public static IEndpointRouteBuilder MapMonitoringRuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/monitoring-rules")
            .WithTags("Monitoring rules");

        // Retourne toutes les règles, actives ou non, triées par nom par le repository.
        group.MapGet("/", async (MonitoringRuleService service, CancellationToken ct) =>
            Results.Ok((await service.GetAllAsync(ct)).Select(MonitoringRuleResponse.FromEntity)))
            .WithName("GetMonitoringRules")
            .WithSummary("Lister les règles de surveillance")
            .WithDescription("Retourne toutes les règles configurées, qu'elles soient actives ou désactivées.")
            .Produces<IReadOnlyList<MonitoringRuleResponse>>(StatusCodes.Status200OK);

        // Retourne une règle précise ou HTTP 404 si son identifiant n'existe pas.
        group.MapGet("/{id:guid}", async (Guid id, MonitoringRuleService service, CancellationToken ct) =>
        {
            var rule = await service.GetByIdAsync(id, ct);
            return rule is null
                ? Results.NotFound()
                : Results.Ok(MonitoringRuleResponse.FromEntity(rule));
        })
        .WithName("GetMonitoringRuleById")
        .WithSummary("Consulter une règle")
        .WithDescription("Retourne la configuration complète d'une règle, y compris ses horaires, jours et formats autorisés.")
        .Produces<MonitoringRuleResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // Crée une règle après validation du contrat HTTP et des invariants métier.
        group.MapPost("/", async (MonitoringRuleRequest request, MonitoringRuleService service, CancellationToken ct) =>
        {
            try
            {
                var rule = await service.CreateAsync(request.ToConfiguration(), ct);
                return Results.Created($"/api/monitoring-rules/{rule.Id}", MonitoringRuleResponse.FromEntity(rule));
            }
            catch (Exception exception) when (IsValidationException(exception))
            {
                return ValidationError(exception);
            }
        })
        .WithName("CreateMonitoringRule")
        .WithSummary("Créer une règle de surveillance")
        .WithDescription("Crée une règle avec son dossier, son mode de matching, ses horaires, son calendrier et ses tolérances. Retourne l'URL de la ressource créée.")
        .Produces<MonitoringRuleResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        // Remplace la configuration d'une règle existante ; ne crée jamais implicitement de nouvelle règle.
        group.MapPut("/{id:guid}", async (Guid id, MonitoringRuleRequest request, MonitoringRuleService service, CancellationToken ct) =>
        {
            try
            {
                var rule = await service.UpdateAsync(id, request.ToConfiguration(), ct);
                return rule is null
                    ? Results.NotFound()
                    : Results.Ok(MonitoringRuleResponse.FromEntity(rule));
            }
            catch (Exception exception) when (IsValidationException(exception))
            {
                return ValidationError(exception);
            }
        })
        .WithName("UpdateMonitoringRule")
        .WithSummary("Modifier une règle de surveillance")
        .WithDescription("Met à jour la configuration complète d'une règle existante, y compris ses horaires et ses extensions.")
        .Produces<MonitoringRuleResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .ProducesValidationProblem();

        // Modifie uniquement l'état actif/inactif sans toucher aux autres paramètres.
        group.MapPatch("/{id:guid}/enabled", async (Guid id, SetMonitoringRuleEnabledRequest request, MonitoringRuleService service, CancellationToken ct) =>
        {
            var updated = await service.SetEnabledAsync(id, request.IsEnabled, ct);
            return updated ? Results.NoContent() : Results.NotFound();
        })
        .WithName("SetMonitoringRuleEnabled")
        .WithSummary("Activer ou désactiver une règle")
        .WithDescription("Change uniquement le booléen IsEnabled. Une règle désactivée est conservée mais ne doit pas être exécutée par le futur moteur.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        // Supprime la règle ; la suppression en cascade de ses horaires est configurée dans EF Core.
        group.MapDelete("/{id:guid}", async (Guid id, MonitoringRuleService service, CancellationToken ct) =>
        {
            var deleted = await service.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteMonitoringRule")
        .WithSummary("Supprimer une règle")
        .WithDescription("Supprime la règle identifiée et ses horaires associés. Retourne 404 si elle n'existe pas.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Identifie les erreurs de validation attendues provenant du contrat ou du domaine.</summary>
    private static bool IsValidationException(Exception exception) =>
        exception is ArgumentException or InvalidTimeZoneException or TimeZoneNotFoundException or ValidationException;

    /// <summary>Transforme une erreur de validation métier en réponse HTTP 400 standardisée.</summary>
    private static IResult ValidationError(Exception exception) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = [exception.Message]
        });
}
