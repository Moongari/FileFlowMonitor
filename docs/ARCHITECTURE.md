# Architecture technique

## Vue logique

```mermaid
flowchart TB
  subgraph Sources
    A[Landing SFTP existant]
    B[Partages réseau UNC]
    C[Dépôts manuels]
  end
  subgraph Runtime
    W[FileSystemWatcher]
    S[Scan périodique de réconciliation]
    ST[Contrôle de stabilité]
    M[Moteur de correspondance]
    G[Générateur des fichiers attendus]
    BG[Service de surveillance en arrière-plan]
  end
  subgraph Core
    D[Domain]
    AP[Application]
  end
  DB[(Base de données)]
  API[ASP.NET Core API]
  HUB[SignalR Hub]
  UI[Blazor Dashboard]
  AL[Alertes et historique]
  A --> W
  B --> W
  C --> W
  A --> S
  B --> S
  C --> S
  W --> BG
  S --> BG
  BG --> ST --> M
  G --> M
  M --> AP --> D
  AP --> DB
  DB --> API
  API --> HUB --> UI
  API --> AL
```

## Projets

- **Domain** : entités, états, règles métier et invariants.
- **Application** : cas d'usage, interfaces, génération des attentes, matching et orchestration.
- **Infrastructure** : EF Core, accès fichiers, watcher, scan périodique, notifications et persistance.
- **Api** : endpoints, configuration, hébergement du worker et hub SignalR.
- **Web** : interface Blazor et tableau de bord.
- **UnitTests / IntegrationTests** : règles métier, moteur, persistance et scénarios de bout en bout.

## API REST et documentation

- Les endpoints HTTP sont regroupés sous `/api/monitoring-rules` et délèguent les opérations au `MonitoringRuleService`.
- Les contrats HTTP (`MonitoringRuleRequest` / `MonitoringRuleResponse`) isolent l'API des entités Domain.
- Swagger UI est exposé en environnement Development à `/swagger` ; le document OpenAPI est disponible à `/swagger/v1/swagger.json`.
- Les résumés/descriptions des endpoints et les commentaires XML des contrats alimentent la documentation OpenAPI. Le détail des routes et des payloads figure dans `docs/API.md`.

## Principes de fiabilité

1. Le watcher n'est jamais la seule source de vérité : un scan périodique rattrape les événements perdus.
2. Les événements sont idempotents : un même fichier détecté plusieurs fois ne crée pas plusieurs réceptions.
3. Un fichier n'est considéré reçu qu'après stabilité de sa taille et de sa date de modification pendant une durée configurable.
4. L'indisponibilité d'un dossier est un état explicite, distinct d'une absence de fichier.
5. Les attentes et incidents sont persistés ; le redémarrage de l'application ne réinitialise pas le suivi.
6. Les fichiers entrants sont observés en lecture seule.

## Flux de traitement

1. Générer les occurrences attendues à partir des règles et du calendrier.
2. Observer les créations/modifications et effectuer des scans de réconciliation.
3. Attendre la stabilité du fichier.
4. Normaliser le chemin, le nom, l'extension, la taille et les horodatages.
5. Associer le fichier à une attente selon les règles de correspondance.
6. Mettre à jour l'état, enregistrer l'événement et publier la mise à jour via SignalR.
7. Déclencher les alertes selon la politique configurée.

## Déploiement

Le moteur doit fonctionner indépendamment de l'interface utilisateur. Pour le développement, l'API héberge le BackgroundService. Une évolution possible sera un Worker Service séparé pour déployer la surveillance au plus près des répertoires.
