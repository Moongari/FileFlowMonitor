# Roadmap

## Phase 1 — Fondations et modèle métier
- [x] Dépôt GitHub et branche de travail créés depuis `main`.
- [x] Solution .NET multi-cible .NET 8 / .NET 10.
- [x] Projets Clean Architecture et persistance SQLite.
- [x] Entités, enums, contrats et tests du domaine.
- [x] Documentation d'architecture et de conception.

## Phase 2 — Gestion des règles et API REST
- [x] CRUD des règles dans Domain / Application / Infrastructure.
- [x] Endpoints REST de consultation, création, modification, activation/désactivation et suppression.
- [x] Validation des requêtes et réponses HTTP documentées.
- [x] Swagger / OpenAPI avec exemples JSON, résumés et commentaires XML.
- [x] Endpoints de supervision : vue d'ensemble, attentes, fichiers détectés, incidents et exécutions.
- [x] Génération idempotente des occurrences attendues pour aujourd'hui et le lendemain.
- [x] Annulation des occurrences futures lors d'une modification, désactivation ou suppression ; réactivation si la règle est remise en service sans changement.

## Phase 3 — Moteur de surveillance
- [x] Scan périodique des dossiers locaux et UNC (source de vérité).
- [x] Contrôle de stabilité de la taille et de la date de modification.
- [x] Matching des noms exacts et des modèles de nommage.
- [x] Détection des retards et absences après délai de grâce.
- [x] Détection de dossier indisponible et résolution de l'incident au rétablissement.
- [x] Historique des exécutions et persistance SQLite après redémarrage.
- [ ] FileSystemWatcher en accélération facultative du scan périodique.
- [ ] Qualification métier des doublons et calcul des écarts de quantité.
- [ ] Validation du contenu des fichiers (CSV/XLSX/PDF/XML/JSON), checksum et manifeste.
- [ ] Calendriers de jours fériés et règles de date métier avancées.

## Phase 4 — Interface de supervision Blazor
- [x] Tableau de bord global avec indicateurs, règles actives et incidents.
- [x] Gestion des règles : création, modification, activation, suspension et suppression confirmée.
- [x] Consultation des fichiers attendus/reçus, incidents et cycles du moteur.
- [x] Actualisation automatique des vues opérationnelles toutes les 30 secondes.
- [ ] Détail approfondi d'une règle et chronologie complète par dossier.
- [ ] Filtres avancés, recherche, pagination et export.
- [ ] SignalR pour les mises à jour événementielles sans attendre le prochain rafraîchissement.

## Phase 5 — Alertes et exploitation
- [ ] Notifications configurables (e-mail, Teams ou webhook).
- [ ] Recherche et export de l'historique.
- [ ] Tests de robustesse sur partages réseau instables, accès refusés et fichiers verrouillés.
- [ ] Documentation de déploiement Windows Service / IIS / conteneur.
- [ ] Observabilité : métriques, logs structurés et rétention.

## Critères de validation MVP
- [x] Plusieurs dossiers peuvent être surveillés simultanément.
- [x] Une règle peut attendre plusieurs fichiers à plusieurs horaires par jour.
- [x] Un fichier partiellement copié n'est pas qualifié avant sa période de stabilité.
- [x] Les absences et retards sont détectés par scan périodique, sans dépendre d'un événement watcher.
- [x] Les règles, attentes, incidents et exécutions sont persistés dans SQLite.
- [x] Le tableau de bord se rafraîchit automatiquement.

## Validation automatisée actuelle

La CI GitHub Actions compile et exécute les tests sur les deux cibles .NET 8 et .NET 10. Les scénarios couvrent le planificateur, les transitions de statut, le matching et la stabilité, les incidents de dossier, l'annulation/réactivation des attentes, les endpoints de supervision, le fuseau métier et les exemples Swagger.

## Roadmap supervision d'équipe, incidents et SLA

La trajectoire détaillée validée pour le positionnement outil d'équipe IT est documentée dans [`ROADMAP_TEAM_SUPERVISION.md`](ROADMAP_TEAM_SUPERVISION.md). Elle couvre l'audit de l'existant, la stratégie de migrations SQLite, le workflow d'incidents, la refonte de la supervision, les politiques SLA et les statistiques historiques. Les notifications externes restent hors périmètre.
