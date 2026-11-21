using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using FileFlowMonitor.Api.Contracts;
using FileFlowMonitor.Application.MonitoringRules;

namespace FileFlowMonitor.Api.Endpoints;

/// <summary>Déclare les routes HTTP de gestion des règles de surveillance.</summary>
public static class MonitoringRuleEndpoints
{
    /// <summary>Enregistre les endpoints CRUD sous /api/monitoring-rules.</summary>
    public static IEndpointRouteBuilder MapMonitoringRuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/monitoring-rules").WithTags("Monitoring rules");

        // Liste toutes les règles, actives ou désactivées.
        group.MapGet("/", async (MonitoringRuleService service, CancellationToken ct) =>
            Results.Ok((await service.GetAllAsync(ct)).Select(MonitoringRuleResponse.FromEntity)))
            .WithName("GetMonitoringRules").WithSummary("Lister les règles de surveillance")
            .WithDescription("""
            Retourne toutes les règles configurées, actives ou désactivées.
            Exemple de réponse :
            [
              {
                "id": "00000000-0000-0000-0000-000000000001",
                "name": "Citi - fichiers de trading",
                "isEnabled": true,
                "scheduleTimes": ["08:00:00", "12:00:00"]
              }
            ]
            """)
            .Produces<IReadOnlyList<MonitoringRuleResponse>>(StatusCodes.Status200OK);

        // Recherche une règle par identifiant.
        group.MapGet("/{id:guid}", async (Guid id, MonitoringRuleService service, CancellationToken ct) =>
        {
            var rule = await service.GetByIdAsync(id, ct);
            return rule is null ? Results.NotFound() : Results.Ok(MonitoringRuleResponse.FromEntity(rule));
        })
        .WithName("GetMonitoringRuleById").WithSummary("Consulter une règle")
        .WithDescription("Retourne la configuration complète d'une règle, y compris ses horaires, jours et formats autorisés. Renseigner {id} avec le GUID retourné par GET /api/monitoring-rules.")
        .Produces<MonitoringRuleResponse>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        // Valide le contrat, applique les invariants métier et crée la ressource.
        group.MapPost("/", async (MonitoringRuleRequest request, MonitoringRuleService service, CancellationToken ct) =>
        {
            var validation = Validate(request);
            if (validation is not null) return validation;
            try
            {
                var rule = await service.CreateAsync(request.ToConfiguration(), ct);
                return Results.Created($"/api/monitoring-rules/{rule.Id}", MonitoringRuleResponse.FromEntity(rule));
            }
            catch (Exception exception) when (IsValidationException(exception)) { return ValidationError(exception); }
        })
        .WithName("CreateMonitoringRule").WithSummary("Créer une règle de surveillance")
        .WithDescription("""
            Crée une règle de surveillance.
            Exemple métier : surveiller le dossier D:\\FileFlow\\Citi du lundi au vendredi à 08:00 et 12:00.
            Deux fichiers sont attendus à chaque horaire : trades.csv et positions.csv.
            Exemple de corps JSON :
            {
              "name": "Citi - fichiers de trading",
              "folderPath": "D:\\FileFlow\\Citi",
              "matchingMode": 0,
              "expectedCount": 2,
              "expectedFileNames": ["trades.csv", "positions.csv"],
              "allowedExtensions": ["csv"],
              "scheduleTimes": ["08:00:00", "12:00:00"],
              "days": 31,
              "timeZoneId": "Europe/Paris",
              "gracePeriodMinutes": 15,
              "stabilityPeriodSeconds": 5,
              "isEnabled": true
            }
            matchingMode : 0=ExactName, 1=FileNameTemplate, 2=Pattern, 3=Quantity.
            Dans le moteur actuel, seuls ExactName et FileNameTemplate sont exécutés.
            days=31 correspond à lundi-vendredi (1+2+4+8+16).
            """)
        .Produces<MonitoringRuleResponse>(StatusCodes.Status201Created).ProducesValidationProblem();

        // Remplace la configuration complète d'une règle existante.
        group.MapPut("/{id:guid}", async (
            Guid id,
            MonitoringRuleRequest request,
            MonitoringRuleService service,
            ILogger<MonitoringRuleEndpoints> logger,
            CancellationToken ct) =>
        {
            var validation = Validate(request);
            if (validation is not null) return validation;
            try
            {
                var rule = await service.UpdateAsync(id, request.ToConfiguration(), ct);
                return rule is null ? Results.NotFound() : Results.Ok(MonitoringRuleResponse.FromEntity(rule));
            }
            catch (DbUpdateConcurrencyException exception)
            {
                var affectedEntries = exception.Entries.Select(entry =>
                {
                    var key = entry.Properties
                        .Where(property => property.Metadata.IsPrimaryKey())
                        .Select(property => $"{property.Metadata.Name}={property.CurrentValue ?? property.OriginalValue}");
                    return $"{entry.Metadata.ClrType.Name} [{string.Join(", ", key)}] ({entry.State})";
                }).ToArray();

                logger.LogError(exception,
                    "Concurrency conflict while updating monitoring rule {RuleId}. Affected EF entries: {Entries}",
                    id,
                    string.Join("; ", affectedEntries));

                return Results.Conflict(new ProblemDetails
                {
                    Title = "Conflit lors de l'enregistrement de la règle",
                    Detail = "Une donnée liée à cette règle a changé ou a été supprimée pendant l'enregistrement. Consultez les journaux de l'API pour identifier l'entité concernée, puis réessayez.",
                    Status = StatusCodes.Status409Conflict
                });
            }
            catch (Exception exception) when (IsValidationException(exception)) { return ValidationError(exception); }
        })
        .WithName("UpdateMonitoringRule").WithSummary("Modifier une règle de surveillance")
        .WithDescription("""
            Remplace la configuration complète d'une règle existante. Le corps JSON est identique à POST.
            Exemple de modification de valeurs dans le corps complet :
            {
              "scheduleTimes": ["09:00:00"],
              "gracePeriodMinutes": 20
            }
            Les autres champs obligatoires de la règle doivent également être fournis.
            Les occurrences futures de l'ancienne configuration sont annulées ; le moteur planifie les nouvelles au cycle suivant.
            """)
        .Produces<MonitoringRuleResponse>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).ProducesValidationProblem();

        // Change uniquement l'état actif/inactif.
        group.MapPatch("/{id:guid}/enabled", async (Guid id, SetMonitoringRuleEnabledRequest request, MonitoringRuleService service, CancellationToken ct) =>
        {
            var updated = await service.SetEnabledAsync(id, request.IsEnabled, ct);
            return updated ? Results.NoContent() : Results.NotFound();
        })
        .WithName("SetMonitoringRuleEnabled").WithSummary("Activer ou désactiver une règle")
        .WithDescription("""
            Modifie uniquement IsEnabled ; les autres paramètres restent inchangés.
            Exemple de corps JSON pour suspendre temporairement la surveillance :
            { "isEnabled": false }
            Envoyer { "isEnabled": true } pour reprendre la surveillance.
            Les occurrences futures sont annulées à la désactivation et replanifiées à la réactivation si la configuration est inchangée.
            """)
        .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        // Supprime la règle ; EF Core supprime également ses horaires associés en cascade.
        group.MapDelete("/{id:guid}", async (Guid id, MonitoringRuleService service, CancellationToken ct) =>
        {
            var deleted = await service.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteMonitoringRule").WithSummary("Supprimer une règle")
        .WithDescription("Supprime définitivement la règle et ses horaires associés. Les attentes futures sont conservées dans l'historique avec le statut Cancelled. Exemple : DELETE /api/monitoring-rules/{id}. Retourne 404 si elle n'existe pas.")
        .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Valide les annotations du contrat HTTP et retourne un problème HTTP 400 en cas d'erreur.</summary>
    private static IResult? Validate(object request)
    {
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(request, new ValidationContext(request), results, true)
            ? null
            : Results.ValidationProblem(results
                .SelectMany(x => x.MemberNames.DefaultIfEmpty("request").Select(member => (member, message: x.ErrorMessage ?? "Valeur invalide.")))
                .GroupBy(x => x.member)
                .ToDictionary(group => group.Key, group => group.Select(x => x.message).ToArray()));
    }

    /// <summary>Détermine si l'exception correspond à une donnée fournie invalide.</summary>
    private static bool IsValidationException(Exception exception) =>
        exception is ArgumentException or InvalidTimeZoneException or TimeZoneNotFoundException or ValidationException;

    /// <summary>Convertit une erreur métier en réponse HTTP 400 standardisée.</summary>
    private static IResult ValidationError(Exception exception) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [exception.Message] });
}
