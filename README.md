# FileFlow Monitor

Application de supervision des dépôts de fichiers entrants : répertoires locaux, partages réseau et dossiers d'arrivée alimentés par des transferts SFTP existants.

## Objectif

Détecter de façon fiable les fichiers attendus, identifier précisément les fichiers manquants ou en retard et fournir un tableau de bord avec historique et alertes.

**Principe clé :** les règles définissent ce qui est attendu ; les événements du système de fichiers accélèrent la détection, tandis qu'un scan périodique garantit la réconciliation.

## Périmètre fonctionnel

- Plusieurs dossiers surveillés simultanément (locaux et UNC).
- Règles indépendantes par source : horaires, jours, délai de grâce, noms exacts ou modèles, groupes et quantités attendues.
- Formats configurables : CSV, XLS/XLSX, PDF, XML, JSON, TXT, DAT, ZIP, etc.
- États : planifié, en attente, reçu, en retard, manquant, invalide, doublon, dossier indisponible.
- Vérification de stabilité avant traitement.
- Tableau de bord temps réel via SignalR, historique et alertes.
- Surveillance en lecture seule : aucun déplacement ou effacement des fichiers entrants.

Un motif générique comme `trade*.csv` ne permet pas de connaître le nom précis d'un fichier manquant. Pour une identification exacte, la règle doit fournir les noms attendus, un modèle daté/séquencé ou un manifeste. Une règle basée uniquement sur une quantité signale un écart de quantité.

## Stack et architecture

- .NET 10 / C#
- ASP.NET Core Web API et service d'arrière-plan
- Blazor Web App (interface à construire)
- SignalR pour les mises à jour temps réel
- EF Core ; SQLite pour le démarrage
- Clean Architecture : Domain, Application, Infrastructure, Api, Web

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

Documentation : [Architecture](docs/ARCHITECTURE.md), [Conception](docs/DESIGN.md), [Roadmap](docs/ROADMAP.md).

## État du développement

Branche de travail : `feature/initial-architecture`. La branche `main` reste intacte jusqu'à validation explicite.

Socle initial en place : solution et projets .NET, premières entités métier, planification des fichiers attendus, persistance EF Core SQLite, endpoints de santé et premiers tests unitaires. Le workflow GitHub Actions compile et exécute les tests avec .NET 10.

## Scénario de référence

Dossier Citi, 2 fichiers CSV attendus à 08:00 avec 15 minutes de tolérance. Si un seul fichier est arrivé à 08:15, le tableau de bord doit afficher 2 attendus, 1 reçu et 1 manquant, avec le nom exact du second fichier si la règle permet de le déterminer.
