# Sources applicatives 2.7.2 — provenance et promotion

## Identité de la préparation

- Version affichée et informationnelle : **2.7.2**.
- Version de fichier : **2.7.2.6**.
- Assembly et manifeste applicatif : **2.7.2.0**.
- Paquet Microsoft Store cible : **2.7.2.0 x64**.
- Date de préparation : **18 novembre 2026**.

Les identités du binaire et du paquet fabriqués doivent être contrôlées dans les résultats de compilation et de packaging. Ce document n’atteste pas une soumission ou une certification Microsoft.

## Origine

La base historique publique est le commit `8fa015f798f6cc9803acf40972d2bcac8f28d434`, qui intègre les sources applicatives 2.7.1.11. Sa [provenance](SOURCES-2.7.1.11.md) et son [inventaire](PROVENANCE-SOURCES-2.7.1.11.json) sont conservés.

Les travaux ciblés sur les entrées de souris, la traînée et la récupération des effets ont produit la copie **2.7.2-exp4**. Son archive de sources a pour SHA256 :

`DA1FAB3B7B4F3413FA9E306196DBFF4951990404D1045D70330C26316AB2F8C4`

Cette copie a été compilée et testée sur Windows, puis acceptée visuellement pendant la recette du 18 novembre. Les données brutes et profils de cette recette ne sont pas publiés dans le dépôt. La synthèse et les limites sont consignées dans [VALIDATION-2.7.2.md](VALIDATION-2.7.2.md).

## Promotion vers 2.7.2

La copie applicative issue du kit exp4 comporte **62 fichiers : 56 conservés octet pour octet, six modifiés** :

| Fichier | Modification de promotion |
|---|---|
| `src/Brand.cs` | Identité 2.7.2 / 2.7.2.6 et résumé de version ; historique public sans entrées de préversions internes |
| `src/Program.cs` | Retrait du refus de lancement normal propre au kit expérimental ; modes d’essai et de diagnostic conservés comme options |
| `languages/strings.json` | Résumé À propos traduit en français, anglais et espagnol |
| `src/SelfTest.cs` | Contrôle du format de version stable, sans exception exp4 |
| `src/TrailRenderingSelfTest.cs` | Étiquette de version 2.7.2 dans le rapport de test ; scénarios conservés |
| `src/Config.cs` | Deux commentaires actualisés ; lignes de code inchangées |

Les chemins sont relatifs à `Haloryva_Windows64/`. Ce décompte concerne la copie applicative issue d’exp4, pas l’ensemble des fichiers historiques ou de packaging du dépôt.

Le moteur, les hooks, la cadence, la géométrie, la récupération, les writers, la migration et les chemins de profil restent identiques à exp4. La comparaison des octets, le catalogue de 517 entrées et le XML ont été vérifiés pendant la préparation. Cela ne remplace pas la compilation et les essais du binaire final.

## Lancement et données

Le lancement normal utilise les réglages existants du canal courant. Le canal Store conserve son dossier local `Haloryva` sous les données du paquet. `--trail-session` conserve un profil de test indépendant dans un dossier neuf ; `--effect-diagnostics` reste facultatif. Aucune collecte détaillée n’est forcée par un lancement normal.

Le journal technique local de récupération est actif et borné à 262 144 octets. Les mesures détaillées et instantanés sont facultatifs. Aucun nouvel envoi réseau n’est ajouté par cette promotion.

## Fabrication et limites

Les résultats de la fabrication native 2.7.2.6, du paquet 2.7.2.0, des tests et de la publication doivent être enregistrés dans [VALIDATION-2.7.2.md](VALIDATION-2.7.2.md). À ce stade, les sources sont préparées ; aucune réussite finale n’est déduite des seuls contrôles statiques.

Les anciens fichiers `installer/`, `tests/maintenance/` et les ressources historiques du dépôt restent identifiés comme tels. Ils ne constituent pas un installateur EXE final 2.7.2. Le téléchargement utilisateur prévu reste Microsoft Store. Les droits réservés de Julien Lecomte sont inchangés.
