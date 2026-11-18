# Validation — Haloryva Studio 2.7.2

**État au 18 novembre 2026 : livraison finale fabriquée et contrôlée, certification Microsoft Store réussie, mise à jour 2.7.1 → 2.7.2 vérifiée sur le PC testé avec réglages conservés. Julien confirme la relance manuelle, le halo, la traînée et les réglages visibles. La disponibilité est établie pour ce poste ; le statut final de publication du portail et le démarrage après reconnexion restent à confirmer.**

## Livraison finale de référence

- Commit livré : [`32234c0410e080add61742b29ee794b5a566fa2e`](https://github.com/JulienLecomteGit/haloryva-studio/commit/32234c0410e080add61742b29ee794b5a566fa2e), après fusion de la [PR nº 5](https://github.com/JulienLecomteGit/haloryva-studio/pull/5).
- [Run Windows final 44300357140](https://github.com/JulienLecomteGit/haloryva-studio/actions/runs/44300357140) : **réussi**.
- Huit suites natives à code 0 et compilation réussie ; 102 fichiers scellés contrôlés ; quatre scripts PowerShell analysés sans erreur ; 52 contrôles de structure MSIX ; 125 fichiers du ZIP sources comparés à l’arbre Git exact.
- Le binaire extrait du paquet est celui testé pendant cette fabrication. Son empreinte correspond aussi au binaire de l’installation Store contrôlée ci-dessous.

| Artefact livré du run 44300357140 | SHA256 |
|---|---|
| Binaire Haloryva.exe, fichier 2.7.2.6, 744 448 octets | `A88FC82D352D0B69EFC8229210C63C88A559586D3A36656011959BB3DB1BEDEE` |
| Paquet de dépôt MSIX 2.7.2.0 x64, 365 098 octets | `906F1D915ECF65316D3713F215CDACF9DF84473A6DEDCF9FEEBD40AC5D344021` |
| Archive des sources, 125 fichiers, 4 019 587 octets | `79B15E5CBB64B487250DF211165715FF2F769B59A8DE15784E6A3F09099018E5` |

Le MSIX de dépôt est produit non signé pour Partner Center. L’installation reçue par Microsoft Store est signée Store. Les mises à jour documentaires ultérieures et les artefacts de leurs éventuelles fabrications ne remplacent pas ce commit ni ces empreintes de livraison.

## Certification et mise à jour Store contrôlées

| Contrôle | Résultat | Portée |
|---|---|---|
| Soumission nº 3 | Envoyée et certification réussie | Captures Partner Center du 18 novembre |
| Publication | Commencée | Dernier écran observé encore en cours ; statut final du portail non relu |
| Installation cible | `2.7.2.0`, x64, `SignatureKind = Store`, état `Ok` | Disponibilité et mise à jour confirmées sur le PC testé |
| Identité et binaire installé | Produit 2.7.2, fichier 2.7.2.6, SHA256 identique au binaire livré | Vérification des rapports Avant/Après et de l’empreinte finale |
| Profil après mise à jour depuis 2.7.1 | 93 valeurs identiques ; aucun champ modifié, manquant ou ajouté | Fichier de réglages également identique octet pour octet entre les deux contrôles |
| Vérificateur Après | `VERSION_CIBLE_INSTALLEE`, comparaison `VALEURS_EXISTANTES_IDENTIQUES`, code 0 | Application fermée pendant le contrôle |
| Relance manuelle, halo, traînée et réglages visibles | Confirmés par Julien | Retour utilisateur après la mise à jour, sans nouvelle mesure de cadence |
| Démarrage après reconnexion | Non encore confirmé | Le contrôle de version et du profil ne démontre pas le lancement automatique |

Les rapports complets ont été relus pour cette synthèse. Aucun profil, chemin personnel, donnée de compte ou journal brut n’est publié ici. Le contrôle sur ce PC ne démontre ni la disponibilité dans tous les marchés, ni le comportement sur tous les appareils. WACK n’a pas été exécuté localement ou dans notre CI ; la certification Microsoft est une preuve distincte.

Après la mise à jour, Julien confirme : **« Haloryva relancé — halo et traînée OK — réglages OK »**. Ce retour valide la relance manuelle et ces observations. Il ne constitue pas une nouvelle recette de l’ensemble des anciens symptômes, un test après lasso, une mesure de cadence ou un essai du démarrage après reconnexion.

## Qualification intermédiaire conservée

- Commit : [`64d97384dfa2c5bfd2618c43b348dff20a863be2`](https://github.com/JulienLecomteGit/haloryva-studio/commit/64d97384dfa2c5bfd2618c43b348dff20a863be2).
- [Fabrication Windows — push, run 44298963756](https://github.com/JulienLecomteGit/haloryva-studio/actions/runs/44298963756) : **réussie**.
- [Contrôle de Pull Request — run 44298972324](https://github.com/JulienLecomteGit/haloryva-studio/actions/runs/44298972324) : **réussi**.
- [Pull Request nº 5](https://github.com/JulienLecomteGit/haloryva-studio/pull/5).
- [Dernières fabrications de la branche main](https://github.com/JulienLecomteGit/haloryva-studio/actions/workflows/haloryva-2.7.2.yml?query=branch%3Amain).

Les résultats et empreintes de cette section décrivent la qualification intermédiaire, antérieure à la fabrication finale et à la mise à jour Store. **Ses artefacts ne sont pas ceux du paquet livré : utiliser les références du run `44300357140` ci-dessus pour la livraison et l’installation.**

### État lors de la qualification intermédiaire

| Étape | État | Portée |
|---|---|---|
| Base expérimentale exp4 | Réussie sur Windows | Compilation, suites et recette visuelle du 18 novembre |
| Promotion des sources vers 2.7.2 | Contrôlée | Identité, différences limitées, catalogue JSON et manifeste XML |
| Compilation du binaire 2.7.2.6 | Réussie | `BUILD_OK`, produit 2.7.2, assembly 2.7.2.0 |
| Suites natives sur les sources de diffusion | Réussies | Huit codes de sortie à 0 ; résultats distincts de ceux d’exp4 |
| Scripts et intégrité | Réussis | 102 fichiers contrôlés ; quatre scripts PowerShell analysés sans erreur de syntaxe |
| Packaging MSIX 2.7.2.0 x64 | Structure vérifiée | 52 contrôles réussis ; binaire extrait identique au binaire testé |
| Archive des sources de qualification | Vérifiée | 125 fichiers correspondant exactement à l’arbre du commit |
| WACK local et interface graphique 2.7.2 | Non exécutés | Les suites natives ne sont pas une recette d’installation interactive |
| Certification Microsoft Store | Non acquise | La certification 2.7.1 ne s’étend pas au nouveau paquet |
| Publication Store 2.7.2 | Non effectuée | Disponibilité à vérifier après publication |
| Mise à jour d’une installation existante | Non établie | Version active, conservation des réglages et démarrage à contrôler |

Bilans de fabrication : `FABRICATION_OK` et `PAQUET_STRUCTURE_OK_NON_SIGNE_NON_INSTALLE`. Le paquet n’a pas été signé ou installé par la CI. La structure vérifiée ne remplace pas la certification Microsoft. L’indication `AdaptateurWin32=COMPILE_NON_EXECUTE` concerne le contrôle isolé, pas une preuve de récupération autonome d’un incident réel.

### Empreintes de la qualification intermédiaire

| Artefact du run 44298963756 | SHA256 |
|---|---|
| Binaire Haloryva.exe, fichier 2.7.2.6 | `9542D96679F2264E3DAD7F5CFB3ADCFB188E9E7666ABCF96BBE42DDCE0DFD1B3` |
| Paquet MSIX 2.7.2.0 x64, 365 092 octets | `9C530E014C149C669E6309796FA96EA943E2F4168000C8FFA7BE1F4AEE5EE974` |
| Archive des sources, 125 fichiers | `E29D89BC35BBB7EB6DB9B1EB1B62DF8B64998440351AF3B0E376A05AB04329A0` |

Le manifeste conserve l’identité `JulienLecomte.HaloryvaStudio`, l’architecture x64, le minimum déclaré `10.0.26200.0` et la tâche de démarrage `HaloryvaStudioStartup`. Le démarrage reste volontaire ; le lancement automatique utilise `--silent`. Le contrôle d’identité et du binaire ne démontre pas à lui seul la conservation des réglages d’une installation réelle.

## Historique de fabrication de la 2.7.2

Le [premier passage 44298109288](https://github.com/JulienLecomteGit/haloryva-studio/actions/runs/44298109288), sur le commit `d948328c920a066b36f65eb45042674e25ec3eca`, avait déjà réussi la compilation et les huit suites. La fabrication complète avait ensuite échoué lors de la découverte de plusieurs commandes Git, avant de terminer le packaging et l’archive de sources. Ce passage n’est donc pas présenté comme une fabrication complète réussie.

La correction minimale de découverte de Git est portée par le commit de qualification `64d97384…`. Les deux exécutions citées ci-dessus ont terminé avec succès. Les suites rejouées dans plusieurs jobs ne sont pas additionnées comme des tests distincts.

## Référence expérimentale : exp4

La compilation Windows d’exp4 a abouti et les suites autonomes ont réussi :

| Suite | Réussites | Échecs |
|---|---:|---:|
| Récupération | 50 | 0 |
| Positions horodatées | 28 | 0 |
| Précision | 40 | 0 |
| Aperçu / cadence | 37 | 0 |
| Diagnostic asynchrone | 52 | 0 |
| Rendu de la traînée | 34 | 0 |

Les contrôles intégrés, dont 16 familles géométriques, ont également réussi. La recette visuelle d’environ quatre minutes a terminé normalement avec 12 901 rendus enregistrés, sans perte d’entrée connue ni publication du calque échouée, et avec deux journaux complets.

Retour reçu : maillons Ligne / Lumière non perçus, aspect agréable, aucune variation de luminosité de l’écran droit, aucune disparition du halo ou de la traînée, aucun scintillement de l’horloge après le lasso pendant cette session.

Ces observations ne garantissent ni zéro saccade dans toutes les situations ni 60 FPS effectivement présentées à l’écran. Les comparaisons de temps entre sessions libres ne sont pas des mesures contrôlées de gain. Les incidents rares n’ont pas été déclenchés puis réparés pendant cette recette : aucune récupération autonome réelle n’y est démontrée. Cette recette ne remplace pas une ouverture de l’interface du binaire final 2.7.2.6.

## Contrôles de promotion

La copie applicative issue d’exp4 comporte 62 fichiers : 56 byte-identiques et six modifiés pour l’identité, les textes, le lancement normal et les étiquettes de tests. Les lignes exécutables de `Config.cs` sont identiques ; seuls deux commentaires changent. Le moteur, les entrées, la migration et les chemins de profil ne sont pas modifiés par cette promotion.

Le catalogue contient 517 clés uniques avec le nouveau résumé FR/EN/ES ; le manifeste XML et les identités sont cohérents. Le refus expérimental du démarrage normal a été retiré. Le mode d’essai isolé et les diagnostics facultatifs sont conservés. Détails : [SOURCES-2.7.2.md](SOURCES-2.7.2.md).

## Vérifications restantes

- Observer le statut final de la soumission nº 3 dans Partner Center ; la disponibilité sur le PC testé est déjà établie par l’installation Store.
- Vérifier le démarrage après reconnexion si cette option est utilisée ; conserver les résultats distincts des contrôles déjà réussis.

Les captures conservées dans `docs/images/` proviennent de la version 2.7.1. Elles ne servent pas de preuve d’exécution de la 2.7.2.
