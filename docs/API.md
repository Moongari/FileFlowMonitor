# API REST — FileFlow Monitor

## Accès à Swagger

En environnement **Development**, l'interface Swagger UI est disponible à `/swagger` sur l'URL de lancement de l'API. Le document OpenAPI est exposé à `/swagger/v1/swagger.json`. Swagger présente les contrats, paramètres, réponses HTTP, résumés et descriptions des opérations. Les commentaires XML du code sont inclus lorsqu'ils sont disponibles. Swagger n'est pas exposé par défaut hors de Development.

## Règles de surveillance

Toutes les routes CRUD sont regroupées sous `/api/monitoring-rules`.

| Méthode | Route | Description | Réponses principales |
|---|---|---|---|
| GET | `/api/monitoring-rules` | Liste toutes les règles, actives et désactivées | 200 |
| GET | `/api/monitoring-rules/{id}` | Détail d'une règle et de ses horaires | 200, 404 |
| POST | `/api/monitoring-rules` | Crée une règle | 201, 400 |
| PUT | `/api/monitoring-rules/{id}` | Remplace la configuration d'une règle | 200, 400, 404 |
| PATCH | `/api/monitoring-rules/{id}/enabled` | Active ou désactive la règle | 204, 404 |
| DELETE | `/api/monitoring-rules/{id}` | Supprime la règle et ses horaires | 204, 404 |

### Exemple — création

```http
POST /api/monitoring-rules
Content-Type: application/json
```

```json
{
  "name": "Citi inbound",
  "folderPath": "D:\\FileFlow\\Citi",
  "matchingMode": 0,
  "fileNamePattern": null,
  "expectedCount": 1,
  "expectedFileNames": ["trades.csv", "positions.csv"],
  "allowedExtensions": ["csv", "pdf"],
  "scheduleTimes": ["08:00:00", "12:00:00"],
  "days": 31,
  "timeZoneId": "Romance Standard Time",
  "gracePeriodMinutes": 15,
  "stabilityPeriodSeconds": 5,
  "isEnabled": true
}
```

La réponse est `201 Created`, avec la représentation de la règle et un en-tête `Location` pointant vers `/api/monitoring-rules/{id}`.

### Exemple — activer / désactiver

```http
PATCH /api/monitoring-rules/{id}/enabled
Content-Type: application/json

{ "isEnabled": false }
```

La réponse est `204 No Content`. Une règle désactivée est conservée en base et sera exclue des traitements du moteur lorsqu'il sera implémenté.

## Contrat de configuration

- `name` : nom obligatoire (1 à 150 caractères).
- `folderPath` : chemin du dossier local ou UNC, obligatoire.
- `matchingMode` : valeur numérique de l'énumération `MatchingMode`.
- `fileNamePattern` : motif requis pour les modes `Pattern` et `FileNameTemplate`.
- `expectedCount` : quantité attendue, au minimum 1.
- `expectedFileNames` : liste des noms attendus ; requise pour `ExactName`.
- `allowedExtensions` : extensions normalisées en minuscules par le domaine.
- `scheduleTimes` : tableau d'heures `TimeOnly`.
- `days` : valeur numérique combinant les flags `WeekDays`.
- `timeZoneId` : identifiant de fuseau horaire reconnu par le système d'exploitation.
- `gracePeriodMinutes` et `stabilityPeriodSeconds` : valeurs positives ou nulles.
- `isEnabled` : état d'activation.

Les erreurs de validation du contrat ou les violations des invariants métier sont retournées en HTTP 400 au format `ValidationProblemDetails`. Un identifiant inexistant produit HTTP 404.

## Santé de l'API

| Méthode | Route | Description |
|---|---|---|
| GET | `/health` | Vérifie que le processus API répond |
| GET | `/health/database` | Vérifie l'accessibilité de la base SQLite |

## Notes d'implémentation

- La couche API utilise des DTO dédiés ; les entités Domain ne sont pas exposées directement.
- Les opérations métier sont orchestrées par `MonitoringRuleService`.
- La persistance reste EF Core avec SQLite.
- Les commentaires XML et les résumés/descriptions des endpoints alimentent Swagger.
- Le CRUD ne démarre pas encore la surveillance des dossiers : le moteur appartient à une phase ultérieure.