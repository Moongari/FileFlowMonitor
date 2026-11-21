using System.ComponentModel.DataAnnotations;
using FileFlowMonitor.Application.MonitoringRules;
using FileFlowMonitor.Domain.Enums;

namespace FileFlowMonitor.Api.Contracts;

/// <summary>
/// Représente les paramètres transmis pour créer ou modifier une règle de surveillance.
/// </summary>
public sealed class MonitoringRuleRequest
{
    /// <summary>Nom lisible de la règle.</summary>
    [Required, StringLength(150, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Chemin local ou UNC du dossier à surveiller.</summary>
    [Required, StringLength(2048, MinimumLength = 1)]
    public string FolderPath { get; init; } = string.Empty;

    /// <summary>Mode de reconnaissance des fichiers attendus.</summary>
    public MatchingMode MatchingMode { get; init; }

    /// <summary>Modèles de noms séparés par des points-virgules. Variables disponibles : {DATE} (yyyyMMdd), {YYYY}, {MM}, {DD}, {TIME} (HHmmss), {HH}, {MINUTES}, {SECONDS}. Chaque modèle est développé pour chaque horaire configuré.</summary>
    public string? FileNamePattern { get; init; }

    /// <summary>Nombre de fichiers attendu lorsque la règle fonctionne par quantité.</summary>
    [Range(1, int.MaxValue)]
    public int ExpectedCount { get; init; } = 1;

    /// <summary>Liste des noms exacts attendus, notamment pour le mode ExactName.</summary>
    public List<string> ExpectedFileNames { get; init; } = [];

    /// <summary>Extensions autorisées, avec ou sans point (ex. csv, pdf).</summary>
    public List<string> AllowedExtensions { get; init; } = [];

    /// <summary>Heures locales auxquelles les fichiers sont attendus, au format HH:mm:ss ou HH:mm.</summary>
    public List<TimeOnly> ScheduleTimes { get; init; } = [];

    /// <summary>Jours d'exécution sous forme de valeur numérique de l'énumération WeekDays.</summary>
    public WeekDays Days { get; init; } = WeekDays.Weekdays;

    /// <summary>Identifiant du fuseau horaire utilisé pour interpréter les horaires.</summary>
    [Required]
    public string TimeZoneId { get; init; } = TimeZoneInfo.Utc.Id;

    /// <summary>Délai de grâce en minutes avant de considérer un fichier en retard ou manquant.</summary>
    [Range(0, 1440)]
    public int GracePeriodMinutes { get; init; } = 15;

    /// <summary>Durée en secondes pendant laquelle taille et date de modification doivent rester stables.</summary>
    [Range(0, 3600)]
    public int StabilityPeriodSeconds { get; init; } = 5;

    /// <summary>Indique si la règle est active dès sa création ou après modification.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Convertit le contrat HTTP en configuration destinée à la couche Application.</summary>
    public MonitoringRuleConfiguration ToConfiguration() => new(
        Name,
        FolderPath,
        MatchingMode,
        FileNamePattern,
        ExpectedCount,
        ExpectedFileNames,
        AllowedExtensions,
        ScheduleTimes,
        Days,
        TimeZoneId,
        GracePeriodMinutes,
        StabilityPeriodSeconds,
        IsEnabled);
}
