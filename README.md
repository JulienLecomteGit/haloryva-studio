# Haloryva Studio — Windows

> **2.7.0 C4 : candidat validé en compilation, autotests et essais visuels ciblés ; distribution finale en attente.**

Application Windows x64 de présentation et d'annotation du curseur, développée par Julien Lecomte. Le code C# et ses ressources sont conservés dans `Haloryva_Windows64/`.

## État au 10 novembre 2026

- Compilation Windows x64, autotests de protocole, application et traînée : **OK** ; cinq étapes avec codes 0 dans le rapport `RESULTAT_HALORYVA_C4.json`.
- Installateur graphique C4 : **compilation, autotest et extraction OK** ; binaire candidat conservé sur le Drive du projet. SHA-256 du setup : `8DEB78FDEAD50E24B3844451336C0255D63125AA20B10E64FA13969BD8E26124`.
- Interface C4 **ouverte sous Windows** (captures fournies par Julien) ; **présence dans « Applications installées » confirmée par Julien (1A)** le 10/11/2026.
- Souris physique : **arrière = X1, avant = X2** ; préréglage « X1 dessin · X2 outil suivant » exécuté correctement selon l'essai utilisateur **1A** ; inversion X1/X2 désactivée.
- Traînée : style **Ligne**, souplesse **50 %**, rendu continu et sans points visibles **accepté par Julien (1A)**.
- **Non vérifié** : cycle de désinstallation/restauration, stabilité prolongée des effets, tous les autres styles et niveaux de souplesse. Les essais utilisateur ne constituent pas une certification générale.
- **[Release Haloryva C4 en brouillon](https://github.com/JulienLecomteGit/haloryva-studio/releases/tag/untagged-557ced11c29edd2ca782)** : préversion non publiée, installateur attaché (607 KB), SHA-256 GitHub correspondant exactement au fichier Drive. Le tag prévu `v2.7.0-c4` sera créé seulement lors d'une éventuelle publication. **Aucune publication publique**.
- La release **[CursorFlow Studio 2.6.6](https://github.com/JulienLecomteGit/haloryva-studio/releases/tag/untagged-bf32b31e3f3adab1b563)** reste un **brouillon privé** avec ses trois assets, à conserver pour retour arrière.

## Architecture

- `Haloryva_Windows64/src/` — application Windows Forms, dessin, souris et diagnostics.
- `Haloryva_Windows64/installer/` — assistant graphique d'installation/désinstallation.
- `Haloryva_Windows64/assets/` — icône, manifestes et visuels.
- `Haloryva_Windows64/tests/maintenance/` — tests C# de protocole.

La compilation de l'installateur s'effectue localement avec `csc.exe` de .NET Framework x64 sous Windows PowerShell 5.1. Les résultats Windows et les livrables sont gérés sur le Drive du projet, séparément du code versionné. Les anciens candidats C1–C3 R2 et leurs recettes sont regroupés dans une archive Drive **récupérable**, pas supprimés définitivement.

**Licence :** droits réservés © Julien Lecomte. Ce dépôt est privé ; aucune licence de redistribution publique n'est accordée par défaut.
