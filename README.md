# CursorFlow Studio

**Rends ton curseur visible. Dessine, explique et présente sur Windows.**

Windows x64 · Version préparée : **2.6.6** · Création : **Julien Lecomte**

> **Dépôt actuellement privé — préparation de diffusion.** La release `v2.6.6` existe en **brouillon privé** avec l'installateur contrôlé et deux documents d'accompagnement. **Elle n'est pas publiée et aucun téléchargement public n'est ouvert.**

## Fonctions

| Fonction | Description |
|---|---|
| Halo intelligent | Contour personnalisable, extinction au repos, centrage sur curseur texte |
| Traînées | Huit styles, couleurs, persistance, largeur et logos |
| Clics visibles | Mise en évidence des actions lors des démonstrations |
| Boutons souris | Actions personnalisables, appuis courts/longs |
| Dessin sur le Bureau | Stylo, flèches libres, formes assistées et annulation |
| Installation graphique | Un EXE précompilé, mise à jour graphique et désinstallation depuis Windows |

## Nouveauté 2.6.6

Le moteur de traînée préserve les points jusqu'à leur expiration, stabilise le cadrage et régularise les rendus. Lors d'un essai local du style Lumière, le moteur a enregistré **16,66 ms d'intervalle médian en mouvement**. Ce résultat ne constitue pas une garantie de fréquence d'affichage sur tous les écrans.

## Installer et utiliser

L'installateur à terme est **`CursorFlowSetup.exe`**, actuellement dans une release non publiée. Il contient l'application précompilée et ne demande ni compilateur ni script PowerShell sur le poste destinataire. La cible de recette actuelle est **Windows 11 x64**.

- [Installation, mise à jour et désinstallation](docs/INSTALLATION.md)
- [Validation de la version 2.6.6](docs/VALIDATION_2.6.6.md)
- [Notes de version](releases/2.6.6/NOTES_VERSION.md)
- [Préparation de diffusion](docs/DIFFUSION.md)

## Gratuit, mais propriétaire — décision de principe

La distribution gratuite en code propriétaire est **retenue comme orientation**. Le projet de conditions d'utilisation reste à valider avant publication et n'est pas encore déposé dans le dépôt. Aucune licence open source n'est accordée par ce dépôt : les sources C# ne sont pas fournies.

## Confidentialité et sécurité

CursorFlow fonctionne localement. L'examen des sources de la version 2.6.6 n'a révélé aucun envoi réseau intégré, mais le logiciel utilise des hooks globaux souris/clavier et certaines options peuvent capturer localement une zone d'écran. Une notice de confidentialité est en préparation hors du dépôt et doit encore être validée.

La version préparée est **non signée** : les protections de Windows peuvent bloquer son exécution. Ne les désactive pas pour lancer le logiciel. Le canal de remontée des problèmes de sécurité et son document de politique restent à finaliser hors du dépôt.

## Signaler un problème

Les formulaires GitHub sont préparés pour les anomalies et améliorations. Ils ne deviennent accessibles au public qu'après décision sur la visibilité. Ne joins jamais de secrets, profils complets, données sensibles ou captures non anonymisées.

## Périmètre du dépôt

Ce dépôt contient uniquement la documentation de diffusion, des métadonnées, et les formulaires de signalement. Les **sources applicatives restent privées**. Le binaire est joint **à la release en brouillon**, et non aux fichiers `main`.

La publication publique, la revue des droits tiers, les conditions et le canal de sécurité restent à autoriser séparément.
