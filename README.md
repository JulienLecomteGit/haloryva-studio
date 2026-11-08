# CursorFlow Studio

**Rends ton curseur visible. Dessine, explique et présente sur Windows.**

Windows x64 · Version de référence **2.6.6** · Création : **Julien Lecomte**

> **Dépôt privé de préparation.** La documentation de la 2.6.6 est prête à relire.
> Aucun installateur, aucune source applicative et aucune release ne sont disponibles
> dans ce dépôt à cette étape. Ce n'est pas un dépôt permettant de compiler l'application.

## Une application pour montrer ce que tu fais

| Fonction | Usage |
|---|---|
| **Halo intelligent** | Personnaliser le contour du pointeur, son extinction au repos et le centrage sur le curseur texte. |
| **Traînées** | Choisir parmi huit styles, ajuster couleurs, largeur et persistance, ou utiliser un logo. |
| **Clics visibles** | Mettre en évidence les actions de la souris pendant une présentation. |
| **Boutons souris** | Affecter des actions aux boutons, notamment aux appuis courts et longs des boutons latéraux. |
| **Dessin sur le Bureau** | Annoter, tracer des flèches libres et utiliser l'assistance de formes, avec annulation. |
| **Installation graphique** | Installer ou mettre à jour depuis un seul EXE ; désinstaller depuis Windows. |

## Ce qui change en 2.6.6

Le moteur de traînée conserve les points jusqu'à leur expiration, stabilise la surface
transparente et régularise le travail de rendu. Le design, les réglages et les fonctions
approuvées sont conservés. Le moteur B a été apprécié pendant l'essai, puis sa fluidité
retrouvée après la mise à jour installée.

**Il s'agit du retour d'usage sur le poste de l'auteur, pas d'une garantie de 60 FPS sur
chaque ordinateur.** Les mesures détaillées et leur périmètre sont décrits dans
[Validation 2.6.6](docs/VALIDATION_2.6.6.md).

## Commencer

- [Installer, mettre à jour et désinstaller](docs/INSTALLATION.md)
- [Lire les notes de version 2.6.6](releases/2.6.6/NOTES_VERSION.md)
- [Consulter les vérifications et limites](docs/VALIDATION_2.6.6.md)
- [Préparer la diffusion sans publier](docs/DIFFUSION.md)

**Fichier prévu : `CursorFlowSetup.exe`.** Il contient l'application précompilée.
Les archives automatiques « Source code » d'une future release de ce dépôt documentaire
ne seront pas l'installateur et ne contiendront pas les sources C# de CursorFlow.

## Sécurité et compatibilité

La cible de recette est **Windows 11 sur PC x64**. Les autres environnements et la
compatibilité sur d'autres PC ne sont pas encore validés par les essais reçus.
Le setup de référence n'a pas reçu de signature numérique. Une protection Windows
peut donc refuser son exécution : **ne désactive aucune protection** et conserve le
message exact. L'hébergement sur GitHub n'ajoute pas de signature au fichier.

## Suivi

Les modèles de signalement couvrent les anomalies et les propositions d'amélioration.
Indique la version, les étapes et le résultat constaté. Ne joins pas de profil personnel,
de sauvegarde, de secret ni de capture non anonymisée.

## Périmètre du dépôt

Ce dépôt conserve la présentation, les notices et l'identité de la version.
Les sources applicatives, exécutables, réglages et rapports bruts restent hors du dépôt.
La visibilité publique, le dépôt de l'EXE et les conditions de diffusion attendent des
accords distincts. Aucune licence open source n'est choisie automatiquement.
