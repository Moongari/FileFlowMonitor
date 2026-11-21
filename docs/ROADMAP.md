# Roadmap

## Phase 1 — Fondations et modèle métier
- [x] Dépôt GitHub créé.
- [x] Branche `feature/initial-architecture` créée depuis `main`.
- [ ] Solution .NET et projets Clean Architecture.
- [ ] Entités, enums, contrats et tests du domaine.
- [ ] Documentation d'architecture et de conception.

## Phase 2 — Gestion des règles
- CRUD des sources et règles.
- Validation des horaires, tolérances, formats et modèles de nommage.
- Génération des occurrences attendues.

## Phase 3 — Moteur de surveillance
- Watcher et scan périodique.
- Contrôle de stabilité et déduplication.
- Matching, retards, absences et indisponibilité des dossiers.
- Persistance et reprise après redémarrage.

## Phase 4 — Tableau de bord temps réel
- API et hub SignalR.
- Vue globale et détail par dossier/règle.
- Liste exacte des fichiers manquants, échéances et arrivées.

## Phase 5 — Alertes et exploitation
- Notifications configurables.
- Historique, recherche et export.
- Tests d'intégration, robustesse réseau et documentation de déploiement.

## Critères de validation MVP
- Plusieurs dossiers surveillés simultanément.
- Une règle peut attendre plusieurs fichiers à plusieurs horaires par jour.
- Un fichier partiellement copié n'est pas déclaré reçu prématurément.
- Les absences et retards sont détectés même si un événement watcher est perdu.
- Un redémarrage ne perd ni règles, ni attentes, ni incidents.
- Le dashboard reflète les changements sans rechargement manuel.
