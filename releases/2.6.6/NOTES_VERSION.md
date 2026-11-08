# CursorFlow Studio 2.6.6 — Windows x64

**Notes préparatoires. Aucun tag ni aucune release ne sont créés par ce fichier.**

Cette version améliore le moteur de traînée tout en conservant le design et les
fonctionnalités approuvées : halo, clics, boutons souris et dessin assisté.

## Améliorations

- Conservation du tracé jusqu'à la fin de sa persistance, sans suppression au plafond antérieur de 160 points.
- Cadrage du calque transparent stabilisé et réutilisation des surfaces graphiques.
- Cadencement révisé, avec au maximum un rendu en attente et charge graphique limitée.
- Assistant graphique contenant l'application précompilée déjà essayée, sans compilation chez le destinataire.

## Vérifications reçues

Compilation et auto-tests réussis ; application extraite du setup identique au B testé.
Après mise à jour, l'auteur confirme le lancement, les préférences conservées, une fluidité
comme B et l'inscription Windows en 2.6.6. Ces retours ne certifient pas tous les PC.

Le relevé B avant installation présente un intervalle médian de 16,660 ms et un 95e centile
à 19,322 ms en mouvement pour Lumière. Il s'agit du moteur, pas de FPS écran garantis.
Aucun pourcentage de gain A/B n'est disponible.

## Fichiers prévus après accord

- **CursorFlowSetup.exe** : seul programme nécessaire à l'installation graphique.
- **LIRE_AVANT_INSTALLATION.txt** : procédure et limites.
- **SHA256SUMS.txt** : identité du setup.

SHA-256 du setup : `F4A66366B2AF1CA0A8D131517F78F2566F09C049B20401B3AFA5DA1039515C67`.

## Limites et sécurité

Cible de recette : Windows 11 x64. La version n'est pas signée ; une politique Windows
peut empêcher son lancement. Ne désactive aucune protection pour l'installer.
Le démarrage après redémarrage, les autres PC et la désinstallation 2.6.6 n'ont pas été
validés dans la recette reçue. Le cycle Windows 2.6.5 reste un acquis distinct.

Les paramètres installés sont conservés ; le profil de l'essai B n'est pas importé.
Les choix de raccourci et de démarrage restent explicites dans l'assistant.

Le canal et les conditions de diffusion publique ne sont pas encore décidés.
