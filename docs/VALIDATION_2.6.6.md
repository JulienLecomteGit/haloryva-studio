# Validation de la référence 2.6.6

État documentaire au **8 novembre 2026**. Les rapports détaillés restent conservés hors
GitHub pour éviter de publier des chemins locaux ou des préférences.

## Trois niveaux de preuve

| Contrôle | Résultat reçu | Portée |
|---|---|---|
| Fabrication A et B + tests | Sept étapes avec code 0 | Rapport Windows RESULTAT_FLUIDITE_BUILD.json fourni par l'auteur |
| Géométrie | 16 familles de tests réussies sur A et B | Journaux d'auto-tests, pas reconnaissance parfaite de tout geste |
| Traînée du candidat | 44 contrôles C# réussis | Logique de conservation, géométrie, surfaces et cadence |
| Protocoles | 68 assertions C# réussies | États d'entrée, cadrage et restauration de fichiers |
| Setup 2.6.6 | Compilation, autotest et extraction : trois codes 0 | Rapport RESULTAT_SETUP_2_6_6.json |
| Application embarquée | Empreinte identique au B essayé | Comparaison automatique par SHA-256 |
| Mise à jour installée | Cinq observations OK, fluidité comme B | Compte rendu déclaratif R11, pas inspection distante du PC |
| Captures reçues | À propos 2.6.6 et page Traînées | Confirment les éléments visibles, pas la fluidité en mouvement |

Les cinq observations R11 sont : mise à jour graphique, lancement en 2.6.6, préférences
conservées, fluidité installée, inscription Windows en 2.6.6. Aucun défaut n'est signalé
dans ce compte rendu. Les captures montrent Lumière, 6 px, 269 ms et 100 % d'opacité.

## Mesure de l'essai B avant installation

Relevé de **1 325 rendus**, style **Lumière**, Studio détecté visible/non réduit.
L'état « visible » ne signifie pas que la fenêtre n'était jamais recouverte.

| Indicateur | Valeur |
|---|---:|
| Intervalles en mouvement | 1 107 |
| Intervalle médian en mouvement | 16,660 ms |
| 95e centile de ces intervalles | 19,322 ms |
| Intervalle maximal en mouvement | 89,716 ms |
| Temps moyen de rendu | 2,650 ms |
| Publications graphiques échouées | 0 |
| Images en rendu GDI de secours | 0 |

Ces mesures portent sur le moteur, **pas sur les images présentées à l'écran**.
Aucun relevé A n'a été reçu : ne pas annoncer de pourcentage de gain A/B.
Le ressenti positif de B, puis la fluidité retrouvée après installation, sont des
observations de l'auteur. Quelques retards isolés restent visibles dans le relevé.

## Identité figée

Application B : `B9BD52DCA5BB119A188782CC6AE9EB61B82097F66F09BE79438CB5218301F362`

Installateur : `F4A66366B2AF1CA0A8D131517F78F2566F09C049B20401B3AFA5DA1039515C67`

Le setup contient trois ressources : l'application, son empreinte et l'image de démonstration.
Aucun profil personnel n'y est embarqué, selon le contrôle d'extraction reçu.

## Ce qui n'est pas validé

- Démarrage à une nouvelle session ou après redémarrage de Windows.
- Compatibilité et fluidité sur d'autres PC, autres paramètres ou tous les écrans/DPI.
- Désinstallation 2.6.6, purge totale des données et tous les scénarios d'interruption.
- Acceptation par toutes les politiques de sécurité Windows ; aucune signature ajoutée.

La recette 2.6.5 de désinstallation avec conservation puis réinstallation reste un acquis
historique distinct. Aucun nouveau binaire n'est à produire uniquement pour supprimer
le terme « candidat » de l'historique affiché par l'application.
