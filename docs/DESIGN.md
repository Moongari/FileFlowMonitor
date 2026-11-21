# Conception fonctionnelle

## Types de règles

- **Fichiers nommés** : liste exacte de noms attendus pour chaque occurrence.
- **Nommage dynamique** : modèle avec variables de date/heure, date métier et séquence.
- **Quantité** : nombre de fichiers attendu dans une fenêtre donnée ; signale un manque de quantité sans inventer de nom.
- **Motif** : filtre de découverte (ex. `trade*.csv`) ; seul, il ne permet pas de calculer un nom manquant.
- **Groupe** : ensemble de fichiers attendus, suivi individuellement et agrégé.

Ces modes peuvent être combinés dans une même règle.

## Calendrier et nommage

Variables envisagées : `{DATE}`, `{YYYY}`, `{MM}`, `{DD}`, `{HH}`, `{mm}`, `{BUSINESS_DATE}`, `{SEQUENCE}`.

Chaque règle porte son fuseau horaire, ses jours d'exécution, ses heures prévues et son délai de grâce. Gestion des jours fériés et calendriers métier à intégrer dans une étape ultérieure.

## États d'une occurrence attendue

- **Scheduled** : occurrence future.
- **Waiting** : échéance atteinte, délai de grâce en cours.
- **Missing** : échéance + tolérance dépassées.
- **Received** : fichier reçu dans le délai.
- **Late** : fichier reçu après le délai.
- **Invalid** : fichier associé mais non conforme aux validations configurées.
- **Duplicate** : réception supplémentaire correspondant à une attente déjà satisfaite.

État de santé de la source suivi séparément : disponible / indisponible / accès refusé.

## Scénario de référence

Pour 2 fichiers attendus à 08:00 et une tolérance de 15 minutes :
- avant 08:00 : Scheduled ;
- de 08:00 à 08:15 : Waiting ;
- à partir de 08:15 sans réception : Missing ;
- réception à 08:10 : Received ;
- réception à 08:25 : Late.

Si un seul fichier arrive, l'autre est marqué Missing uniquement si son identité est déductible de la règle.
