# Roadmap produit — Supervision d'équipe, incidents, statistiques et SLA

**Positionnement retenu :** outil de supervision interne pour une équipe IT.  
**Notifications externes :** hors périmètre actuel (pas d'e-mail, Teams ou webhook).  
**Branche de livraison :** `feature/monitoring-engine`.

Ce document complète la roadmap technique générale. Il décrit une trajectoire incrémentale fondée sur l'état du dépôt audité le 23 novembre 2026.

## 1. État constaté dans le dépôt

| Domaine | État constaté | Conséquence |
|---|---|---|
| Règles et horaires | Entités et API CRUD présentes | À conserver |
| Occurrences attendues | `ExpectedFile` porte horaire, échéance, réception et statut | Base des KPI |
| Réceptions | `ReceivedFile` est persisté | Base de l'historique de réception |
| Moteur | Cycle périodique et watcher présents | Ne pas réécrire sans tests de non-régression |
| Incidents | `MonitoringIncident` contient code, message, occurrence et résolution | Incident technique simple, pas encore un workflow d'équipe |
| Historique d'incident | Pas d'entité d'historique dédiée constatée | À ajouter |
| Affectation / prise en charge | Non présents dans le modèle d'incident actuel | À ajouter |
| SLA configurable | Pas de politique SLA distincte constatée | À concevoir séparément de la période de grâce |
| Statistiques historiques | Vue de supervision actuelle centrée sur une journée | Nouvel endpoint et nouvel écran nécessaires |
| Interface | La page d'accueil affiche déjà des KPI journaliers, les flux, les attentes, les incidents récents et les cycles | Faire évoluer l'existant, éviter une réécriture complète |
| Persistance | SQLite initialisé avec `EnsureCreated` | Introduire une stratégie de migrations avant toute modification de schéma |

## 2. Principes d'architecture

1. Le moteur de surveillance reste la source de vérité pour les statuts des occurrences.
2. Les règles de calcul métier (incident, éligibilité statistique, SLA) résident dans Application, pas dans Razor.
3. Les endpoints exposent des DTO stables et documentés ; l'interface ne recalcule pas les KPI.
4. Les incidents sont idempotents : une réconciliation répétée ne crée pas plusieurs incidents pour la même anomalie active.
5. Les événements et changements d'état doivent être historisés ; un incident résolu reste consultable.
6. Les indicateurs distinguent les occurrences futures, en attente, reçues à l'heure, reçues tardivement et manquantes.
7. Ne pas assimiler la deadline actuelle à un SLA contractuel. Le délai de grâce et la cible SLA sont deux paramètres différents.
8. Aucun canal de notification externe n'est développé dans cette trajectoire.

## 3. Lots priorisés

### Lot 0 — Sécurisation du socle et stratégie de données

- [ ] Ajouter des tests de non-régression sur réconciliation répétée, réception partielle, retard et résolution.
- [ ] Définir une stratégie EF Core migrations SQLite ; remplacer `EnsureCreated` uniquement après validation de la migration initiale et du scénario de mise à niveau d'une base existante.
- [ ] Ajouter des index adaptés aux recherches par règle, date d'occurrence, statut et date de réception.
- [ ] Définir les conventions UTC et fuseau horaire métier pour tous les calculs.

**Sortie attendue :** une base existante peut être mise à niveau sans perte de données et les calculs sont déterministes.

### Lot 1 — Workflow d'incidents d'équipe

Étendre le modèle actuel sans perdre la compatibilité avec les incidents déjà enregistrés.

Champs métier à introduire :
- numéro lisible et type d'incident ;
- sévérité (Low, Medium, High, Critical) ;
- statut (Open, InProgress, Resolved, Closed) ;
- responsable / équipe affectée ;
- date de prise en charge, résolution et clôture ;
- commentaire de résolution.

Créer un historique append-only des actions : création, changement de statut, affectation, commentaire, résolution automatique et clôture.

API cible :
- liste paginée avec filtres (statut, sévérité, règle, période, responsable) ;
- détail d'un incident et chronologie ;
- prise en charge / réaffectation ;
- ajout de commentaire ;
- résolution et clôture.

Règles :
- une seule occurrence d'incident actif par anomalie métier ;
- la réception tardive met à jour l'incident lié, sans effacer l'historique ;
- la résolution automatique est explicitement identifiée comme telle ;
- la clôture opérationnelle reste distincte de la résolution technique.

**Tests de sortie :** idempotence, transitions interdites, concurrence de réconciliation, incident historique conservé après résolution.

### Lot 2 — Refonte de la supervision

Faire évoluer la page d'accueil existante en centre de contrôle d'équipe.

Zone synthèse :
- occurrences éligibles ;
- reçues à l'heure ;
- reçues en retard ;
- manquantes ;
- en attente ;
- incidents ouverts et critiques.

Vue par flux :
- état de santé ;
- attendus / reçus / en retard / manquants ;
- taux de réception ;
- dernière réception ;
- prochaine échéance ;
- incidents ouverts.

Vue détaillée d'un flux :
- timeline des occurrences de la journée ;
- détail par fichier attendu ;
- réception partielle clairement visible ;
- accès direct à l'incident associé ;
- historique des cycles et erreurs de dossier.

Filtres :
- date / période ;
- flux ;
- statut ;
- gravité et statut d'incident.

UX :
- couleurs de flux stables et distinctes des couleurs de statut ;
- badges sémantiques pour les anomalies ;
- pagination ou virtualisation si les volumes l'exigent ;
- états vides, erreurs API et chargement explicitement traités.

**Tests de sortie :** égalité des compteurs avec les listes détaillées, filtres combinés, affichage multi-flux et vérification du rendu dans un navigateur.

### Lot 3 — Politique SLA

Créer une politique SLA explicite, distincte de `GracePeriodMinutes`.

Paramètres proposés par règle :
- activation du SLA ;
- délai cible en minutes ;
- mode de calcul (calendaire en V1) ;
- fuseau horaire de référence ;
- version / date d'effet de la politique.

Définitions :
- heure planifiée : moment attendu ;
- délai de grâce : fenêtre utilisée par le moteur pour qualifier une absence ;
- échéance SLA : limite contractuelle de réception ;
- respect SLA : réception enregistrée au plus tard à l'échéance SLA.

Conserver une photographie de la politique appliquée à l'occurrence, ou sa version, afin qu'une modification ultérieure de la règle ne réécrive pas les résultats historiques.

V1 : minutes calendaires.  
V2 : calendrier ouvré, jours fériés, fenêtres de maintenance et exceptions.

**Tests de sortie :** limites exactes, changement d'heure, fuseaux, modification de politique, occurrence sans réception et réception après SLA.

### Lot 4 — Statistiques et reporting

Nouvel écran avec période sélectionnable (jour, semaine, mois, intervalle personnalisé).

KPI :
- taux de réception ;
- taux de respect SLA ;
- taux de retard ;
- taux de manquants ;
- retard moyen et médian ;
- délai moyen de réception ;
- temps moyen de résolution (MTTR) ;
- incidents par sévérité et par flux.

Ventilations :
- par flux ;
- par jour / semaine / mois ;
- par type de fichier ;
- par sévérité d'incident.

Règles de calcul :
- exclure les occurrences futures et celles dont la fenêtre de réception n'est pas échue ;
- afficher séparément les occurrences annulées ;
- distinguer réception à l'heure de réception dans le SLA ;
- expliciter le dénominateur de chaque pourcentage ;
- conserver les heures en UTC en base et convertir uniquement pour l'affichage et les calendriers.

Export CSV en première version ; Excel ensuite si le besoin est confirmé.

**Tests de sortie :** jeux de données de référence avec résultats attendus, périodes sans données, bornes inclusives/exclusives et cohérence des agrégats.

## 4. Ordre de livraison

| Ordre | Lot | Critère de validation |
|---:|---|---|
| 1 | Socle / migrations / tests | Base existante mise à niveau sans perte |
| 2 | Workflow incidents | Prise en charge, commentaires, résolution, historique |
| 3 | Supervision | Vue équipe et détail flux cohérents |
| 4 | SLA | Politique versionnée et calcul testé |
| 5 | Statistiques | KPI vérifiables et export CSV |

## 5. Definition of Done

Chaque lot doit :
- compiler en Release sur `net8.0` et `net10.0` ;
- réussir les tests existants et nouveaux ;
- inclure les tests API / persistance nécessaires ;
- mettre à jour Swagger et la documentation ;
- conserver les scénarios de réconciliation actuels ;
- être validé sur l'interface réelle avant de considérer l'UX terminée.

Aucun déploiement sur `main` ni aucune activation de notification externe ne fait partie de cette livraison sans décision explicite.
