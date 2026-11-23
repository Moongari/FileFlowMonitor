# Workflow opérationnel des incidents

## Objectif

Transformer les incidents techniques générés par le moteur de surveillance en éléments réellement exploitables par l'équipe support. Le workflow reste interne à FileFlowMonitor : aucune notification externe n'est émise.

## Cycle de vie

| Statut | Signification |
|---|---|
| `Open` | Incident détecté, en attente de prise en charge |
| `InProgress` | Incident pris en charge ; la première transition renseigne la date d'acquittement |
| `Resolved` | Cause traitée ou condition technique rétablie |
| `Closed` | Traitement terminé et clôturé |

Un incident résolu ou clos doit être rouvert en `Open` avant une nouvelle prise en charge. Un incident clos ne peut pas passer directement à `InProgress`.

## Priorités

- `Low` : impact faible
- `Medium` : priorité standard (valeur par défaut)
- `High` : impact important
- `Critical` : impact critique

La priorité est modifiable par l'équipe. La classification automatique selon le type de flux et le SLA viendra dans une étape ultérieure.

## Données persistées

- `MonitoringIncident` reste l'événement technique produit par le moteur.
- `MonitoringIncidentWorkflow` porte le statut opérateur, la priorité, l'assignation, l'acquittement et les dates de résolution/clôture.
- `MonitoringIncidentHistory` conserve les événements de création, les changements opérateur et les résolutions automatiques.

La séparation entre incident technique et workflow évite de casser le moteur de détection existant. Les incidents déjà présents sont rétrocompatibles : ils sont exposés comme `Open` ou `Resolved` tant qu'aucun workflow opérateur n'a été créé.

## API

### Lister les incidents

`GET /api/monitoring/incidents?openOnly=true&limit=200`

La réponse contient notamment le flux, le code, le message, le statut, la priorité, l'assignation et les dates.

### Modifier le workflow

`PATCH /api/monitoring/incidents/{id}/workflow`

Exemple :

```json
{
  "status": "InProgress",
  "severity": "High",
  "assignedTo": "Support N2"
}
```

Les champs omis restent inchangés. Une chaîne vide permet de retirer l'assignation ou d'effacer le commentaire de résolution.

### Consulter l'historique

`GET /api/monitoring/incidents/{id}/history`

## Persistance et compatibilité SQLite

L'application utilise actuellement `EnsureCreated` et ne dispose pas encore d'un historique de migrations EF Core conventionnel. Le démarrage ajoute donc les deux tables du workflow via des instructions SQLite additives `CREATE TABLE IF NOT EXISTS`, après l'initialisation du schéma existant. Aucune table métier existante n'est supprimée ni reconstruite.

La réinitialisation opérationnelle supprime également l'historique et les workflows avant les incidents.

## Limites de cette première livraison

- Pas encore de commentaires multiples : un commentaire de résolution courant est conservé, et chaque modification est auditée dans l'historique.
- Pas encore de pièces jointes, de gestion d'équipe/annuaire, de règles d'escalade ni de notifications.
- Les SLA, leur calcul et les tableaux de bord statistiques restent des étapes séparées de la roadmap.
- Le navigateur doit être validé localement après récupération de la branche ; le dépôt distant ne permet pas de certifier le rendu visuel réel.
