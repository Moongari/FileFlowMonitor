# FileFlow Monitor

Application de supervision des dépôts de fichiers entrants : répertoires locaux, partages réseau et dossiers d'arrivée alimentés par des transferts SFTP existants.

## Objectif

Détecter de façon fiable les fichiers attendus, identifier précisément les fichiers manquants ou en retard et fournir un tableau de bord avec historique et alertes.

**Principe clé :** les règles définissent ce qui est attendu ; le scan périodique est la source de vérité pour la réconciliation. L'accélération par FileSystemWatcher pourra être ajoutée ensuite.

## Périmètre fonctionnel

- Plusieurs dossiers surveillés simultanément (locaux et UNC).
- Règles indépendantes par source : horaires, jours, délai de grâce, noms exacts ou modèles de nommage. Les modes Motif et Quantité sont prévus pour une évolution ultérieure.
- Formats configurables : CSV, XLS/XLSX, PDF, XML, JSON, TXT, DAT, ZIP, etc.
- États : planifié, en attente, reçu, en retard, manquant, invalide, annulé et dossier indisponible. La qualification des doublons est prévue dans un lot ultérieur.
- Vérification de stabilité avant traitement.
- Tableau de bord Blazor avec indicateurs, règles, fichiers attendus/reçus, incidents et historique des cycles. Actualisation manuelle dans cette première version.
- Surveillance en lecture seule : aucun déplacement ou effacement des fichiers entrants.

Un motif générique comme `trade*.csv` ne permet pas de connaître le nom précis d'un fichier manquant. Pour une identification exacte, la règle doit fournir les noms attendus, un modèle daté/séquencé ou un manifeste. Une règle basée uniquement sur une quantité signale un écart de quantité.

## Stack et architecture

- **Cibles .NET 8 et .NET 10** avec une seule base de code et une seule solution.
- ASP.NET Core Web API et service d'arrière-plan.
- Blazor Web App avec rendu interactif côté serveur.
- API HTTP dédiée aux vues opérationnelles et à la configuration.
- Entity Framework Core avec **SQLite sur les deux cibles**.
- Clean Architecture : Domain, Application, Infrastructure, Api, Web.

Les dépendances EF Core, ASP.NET Core TestHost et Swagger sont sélectionnées selon le framework cible afin de rester compatibles avec chaque runtime.

## Prérequis SDK

- Pour compiler et tester les deux cibles : SDK .NET 10 (qui sait cibler .NET 8 et .NET 10).
- Dans un environnement disposant uniquement du SDK .NET 8, cibler explicitement .NET 8 :

```powershell
dotnet restore FileFlowMonitor.sln -p:TargetFrameworks=net8.0
dotnet build FileFlowMonitor.sln -c Release -p:TargetFrameworks=net8.0
dotnet test FileFlowMonitor.sln -c Release -p:TargetFrameworks=net8.0
```

Avec le SDK .NET 10, valider les deux frameworks :

```powershell
dotnet restore FileFlowMonitor.sln
dotnet build FileFlowMonitor.sln -c Release -f net8.0
dotnet test FileFlowMonitor.sln -c Release -f net8.0
dotnet build FileFlowMonitor.sln -c Release -f net10.0
dotnet test FileFlowMonitor.sln -c Release -f net10.0
```

## Structure du dépôt

```text
src/
  FileFlowMonitor.Domain/
  FileFlowMonitor.Application/
  FileFlowMonitor.Infrastructure/
  FileFlowMonitor.Api/
  FileFlowMonitor.Web/
tests/
  FileFlowMonitor.UnitTests/
  FileFlowMonitor.IntegrationTests/
docs/
```

Documentation : [Architecture](docs/ARCHITECTURE.md), [Conception](docs/DESIGN.md), [API REST et Swagger](docs/API.md), [Roadmap](docs/ROADMAP.md).

## État du développement

Branche de travail : `feature/monitoring-engine`. La branche `main` reste intacte jusqu'à validation explicite.

Implémenté sur cette branche : moteur de réconciliation périodique, génération des occurrences du jour et du lendemain, détection et stabilisation des fichiers, incidents de dossier, annulation des attentes futures lors d'une modification de règle, endpoints de supervision, gestion des règles dans Blazor, tableau de bord et vues opérationnelles. Swagger documente les opérations avec exemples JSON. Les modes Motif/Quantité, les doublons et l'accélération FileSystemWatcher restent à développer. La validation CI complète sur les deux frameworks reste obligatoire avant intégration dans `main`.


## Lancer l'application en local

L'API et l'interface sont deux processus séparés. Depuis la racine du dépôt, ouvrir deux terminaux PowerShell :

**Terminal 1 — API et moteur de surveillance**

```powershell
dotnet run --project src/FileFlowMonitor.Api
```

API : http://localhost:5080 — Swagger : http://localhost:5080/swagger

**Terminal 2 — Interface Blazor**

```powershell
dotnet run --project src/FileFlowMonitor.Web
```

Interface : http://localhost:5081

L'URL de l'API est configurable dans `src/FileFlowMonitor.Web/appsettings.json` via `Api:BaseUrl`. L'intervalle du moteur est configurable dans `src/FileFlowMonitor.Api/appsettings.json` via `Monitoring:PollIntervalSeconds` (30 secondes par défaut).

## Scénario de référence

Dossier Citi, 2 fichiers CSV attendus à 08:00 avec 15 minutes de tolérance. Si un seul fichier est arrivé à 08:15, le tableau de bord doit afficher 2 attendus, 1 reçu et 1 manquant, avec le nom exact du second fichier si la règle permet de le déterminer.
