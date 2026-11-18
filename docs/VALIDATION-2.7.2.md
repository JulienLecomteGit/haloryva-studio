# Validation — Haloryva Studio 2.7.2

**État au 18 novembre 2026 : sources de diffusion préparées ; résultats natifs du binaire final à confirmer ; Store 2.7.2 non soumis et non certifié.**

## Étapes et preuves

| Étape | État | Portée |
|---|---|---|
| Base expérimentale exp4 | Réussie sur Windows | Compilation, suites et recette visuelle du 18 novembre |
| Promotion des sources vers 2.7.2 | Contrôlée localement | Identité, différences limitées, catalogue JSON et manifeste XML |
| Compilation du binaire final 2.7.2.6 | À confirmer | Nécessite le résultat du job Windows et l’empreinte du binaire |
| Suites natives sur les sources de diffusion | À confirmer | Ne pas remplacer par les résultats d’exp4 |
| Packaging MSIX 2.7.2.0 x64 | À confirmer | Identité, contenu, ressources et manifeste à vérifier |
| Certification Microsoft Store | Non acquise | La certification 2.7.1 ne s’étend pas au nouveau paquet |
| Publication Store 2.7.2 | Non effectuée | Disponibilité à vérifier après la publication |
| Mise à jour d’une installation existante | Non établie | Version active, conservation des réglages et démarrage à contrôler |

Les cases restent inchangées tant que les preuves correspondantes ne sont pas reçues. Le nom d’un fichier ou l’ajout des sources dans GitHub ne valide pas son exécution.

## Référence expérimentale : exp4

La compilation Windows a abouti et les suites autonomes ont réussi :

| Suite | Réussites | Échecs |
|---|---:|---:|
| Récupération | 50 | 0 |
| Positions horodatées | 28 | 0 |
| Précision | 40 | 0 |
| Aperçu / cadence | 37 | 0 |
| Diagnostic asynchrone | 52 | 0 |
| Rendu de la traînée | 34 | 0 |

Les contrôles intégrés, dont 16 familles géométriques, ont également réussi. Les suites réexécutées dans plusieurs contextes ne sont pas additionnées comme des tests distincts.

La recette d’environ quatre minutes a terminé normalement, avec 12 901 rendus enregistrés. Aucune perte d’entrée connue ni publication du calque échouée n’est enregistrée. Les deux journaux sont complets, sans perte ni erreur.

Retour visuel reçu : maillons Ligne / Lumière non perçus, aspect agréable, aucune variation de luminosité de l’écran droit, aucune disparition du halo ou de la traînée, aucun scintillement de l’horloge après le lasso pendant cette session.

Ces observations ne garantissent ni zéro saccade dans toutes les situations ni 60 FPS effectivement présentées à l’écran. Les comparaisons de temps entre sessions libres ne sont pas des mesures contrôlées de gain. Les incidents rares n’ont pas été déclenchés puis réparés pendant cette recette : aucune récupération autonome réelle n’y est démontrée.

## Contrôles de promotion réalisés

La copie applicative de diffusion comporte 62 fichiers issus d’exp4 : 56 byte-identiques et six modifiés pour l’identité, les textes, le lancement normal et les étiquettes de tests. Les lignes exécutables de `Config.cs` sont identiques ; seuls deux commentaires changent. Le moteur, les entrées, la migration et les chemins de profil ne sont pas modifiés par cette promotion.

Le catalogue contient 517 clés uniques avec le nouveau résumé FR/EN/ES ; le manifeste XML et les identités sont cohérents. Le refus expérimental du démarrage normal a été retiré. Le mode d’essai isolé et les diagnostics facultatifs sont conservés. Détails : [SOURCES-2.7.2.md](SOURCES-2.7.2.md).

## Données à compléter après fabrication

- URL et commit exact du job de fabrication Windows.
- Résultat et codes de sortie des suites exécutées sur cette source.
- Version et SHA256 du binaire final, puis du paquet MSIX.
- Résultats de contrôle du contenu et de l’identité du paquet.
- État daté de la soumission, de la certification et de la disponibilité Store.
- Résultat de la mise à jour de l’installation existante, sans publier de profil, de chemin personnel ou de journal brut.

Les captures de présentation conservées dans `docs/images/` proviennent de la version 2.7.1. Elles ne servent pas de preuve d’exécution de la 2.7.2.
