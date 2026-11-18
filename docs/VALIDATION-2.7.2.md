# Validation — Haloryva Studio 2.7.2

**État au 18 novembre 2026 : fabrication Windows de qualification réussie ; structure MSIX vérifiée ; paquet non signé et non installé ; Store 2.7.2 non soumis et non certifié.**

## Référence de qualification

- Commit : [`64d97384dfa2c5bfd2618c43b348dff20a863be2`](https://github.com/JulienLecomteGit/haloryva-studio/commit/64d97384dfa2c5bfd2618c43b348dff20a863be2).
- [Fabrication Windows — push, run 44298963756](https://github.com/JulienLecomteGit/haloryva-studio/actions/runs/44298963756) : **réussie**.
- [Contrôle de Pull Request — run 44298972324](https://github.com/JulienLecomteGit/haloryva-studio/actions/runs/44298972324) : **réussi**.
- [Pull Request nº 5](https://github.com/JulienLecomteGit/haloryva-studio/pull/5).
- [Dernières fabrications de la branche main](https://github.com/JulienLecomteGit/haloryva-studio/actions/workflows/haloryva-2.7.2.yml?query=branch%3Amain).

Les résultats et empreintes ci-dessous sont ceux de cette qualification. Les commits documentaires ou fabrications suivants possèdent leurs propres résultats. **Le paquet à déposer au Store doit être celui du dernier commit de diffusion effectivement fabriqué et contrôlé ; les empreintes de qualification ne désignent pas automatiquement ce futur paquet.**

## Étapes et preuves

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

## Empreintes de la qualification

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

## Étapes restantes de diffusion

- Contrôler la fabrication du commit final de diffusion et conserver les empreintes de ses propres artefacts.
- Déposer le paquet concerné dans Microsoft Store, puis observer la certification et la publication effectives.
- Vérifier ensuite la mise à jour de l’installation existante, la version active, les réglages et le démarrage, sans publier de profil ni de journal personnel.

Les captures conservées dans `docs/images/` proviennent de la version 2.7.1. Elles ne servent pas de preuve d’exécution de la 2.7.2.
