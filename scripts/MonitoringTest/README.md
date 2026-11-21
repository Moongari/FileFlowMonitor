# FileFlowMonitor - scénarios de test PowerShell

Ces scripts génèrent des fichiers CSV de test puis permettent de les déposer dans un dossier surveillé à la demande.

Dossier de préparation par défaut : C:\Users\Moon\Temp_test

Les fichiers ne sont jamais déposés automatiquement dans le dossier surveillé.

## 1. Générer une journée complète

Depuis la racine du dépôt :

    .\scripts\MonitoringTest\Generate-TestFiles.ps1

Par défaut, le script génère deux fichiers par heure, de 08h00 à 23h00 inclus, pour la date du jour.

Pour reproduire explicitement le 21 novembre 2026 :

    .\scripts\MonitoringTest\Generate-TestFiles.ps1 -Date "2026-11-21"

Exemples de noms générés avec le format par défaut HHmm0 :

- Trade1_20261121_08000.csv
- Trade2_20261121_08000.csv
- Trade1_20261121_09000.csv
- Trade2_20261121_09000.csv
- ...
- Trade1_20261121_23000.csv
- Trade2_20261121_23000.csv

Le format HHmmss produit 080000 et correspond au jeton {TIME} actuellement implémenté dans le moteur :

    .\scripts\MonitoringTest\Generate-TestFiles.ps1 -Date "2026-11-21" -TimeFormat HHmmss

## 2. Scénarios disponibles

| Scénario | Comportement |
|---|---|
| CompleteDay | Les deux fichiers sont générés pour chaque horaire. |
| MissingTrade1 | Trade1 manque à l'heure indiquée par ScenarioHour. |
| MissingTrade2 | Trade2 manque à l'heure indiquée par ScenarioHour. |
| MissingBoth | Trade1 et Trade2 manquent à l'heure indiquée. |
| Trade1Only | Tous les Trade1 sont générés, aucun Trade2. |
| Trade2Only | Tous les Trade2 sont générés, aucun Trade1. |
| SingleHour | Les deux fichiers sont générés pour un seul horaire. |

### Trade2 manquant à 18h00

    .\scripts\MonitoringTest\Generate-TestFiles.ps1 -Date "2026-11-21" -Scenario MissingTrade2 -ScenarioHour 18

Trade1_20261121_18000.csv sera créé, mais Trade2_20261121_18000.csv sera volontairement absent.

### Créneau isolé à 18h00

    .\scripts\MonitoringTest\Generate-TestFiles.ps1 -Date "2026-11-21" -Scenario SingleHour -ScenarioHour 18

### Générer uniquement Trade1

    .\scripts\MonitoringTest\Generate-TestFiles.ps1 -Date "2026-11-21" -Scenario Trade1Only

## 3. Déposer un fichier dans le dossier surveillé

Le script Send-TestFile.ps1 copie un fichier préparé vers le dossier surveillé.

    .\scripts\MonitoringTest\Send-TestFile.ps1 -FileName "Trade1_20261121_18000.csv" -WatchDirectory "D:\FileFlow\Test\Citi"

Pour simuler un dépôt différé de 20 secondes :

    .\scripts\MonitoringTest\Send-TestFile.ps1 -FileName "Trade2_20261121_18000.csv" -WatchDirectory "D:\FileFlow\Test\Citi" -DelaySeconds 20

Pour tester une réception tardive, laisse expirer le délai de grâce avant de déposer le fichier. DelaySeconds ne fait qu'attendre avant la copie ; il ne garantit pas à lui seul un statut Late.

## 4. Parcours de validation recommandé

1. Réception complète : générer CompleteDay, déposer les fichiers et vérifier les statuts Received.
2. Fichier manquant : générer MissingTrade2 à 18h00 ; vérifier qu'après le délai de grâce, seul Trade2 à 18h00 devient Missing.
3. Réception partielle : générer Trade1Only ; vérifier que les attentes Trade2 sont identifiées séparément.
4. Réception tardive : laisser expirer le délai de grâce pour un fichier attendu, puis déposer ce fichier ; vérifier le statut Late.
5. Créneau isolé : utiliser SingleHour pour tester le diagnostic sans déposer toute la journée.
6. Persistance : redémarrer l'application et vérifier que les fichiers reçus, les attentes et les incidents restent cohérents.
7. Dossier indisponible : rendre inaccessible le partage ou utiliser un chemin de test inexistant, puis vérifier l'incident et le retour à la normale.
8. Fichier instable : copier un fichier volumineux ou le créer progressivement pour vérifier que le moteur attend la fin de l'écriture.
9. Extension non autorisée : déposer un fichier de même base avec une extension interdite et vérifier le comportement de validation.
10. Désactivation : désactiver une règle, vérifier qu'elle n'engendre plus de nouvelles attentes, puis la réactiver.

## 5. Nettoyer les fichiers de préparation

Le nettoyage ne concerne que les fichiers Trade1_*.csv et Trade2_*.csv correspondant à la date demandée, dans le dossier de préparation.

    .\scripts\MonitoringTest\Generate-TestFiles.ps1 -Date "2026-11-21" -CleanGeneratedFiles

Attention : le nettoyage supprime les fichiers correspondants du dossier de préparation. Il ne touche pas au dossier surveillé.

## Remarques

- HHmm0 produit 08000 pour 08h00, comme dans l'exemple demandé.
- HHmmss produit 080000 et correspond au jeton {TIME} actuellement implémenté.
- Les noms Trade1 et Trade2 doivent correspondre exactement aux modèles de nommage configurés dans la règle.
- Les fichiers sont de petits CSV synthétiques, sans données métier réelles.
