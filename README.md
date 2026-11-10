# Haloryva Studio — Windows

> **2.7.0 C4 : candidat technique, non publié et non validé en installation réelle.**

Application Windows x64 de présentation et d'annotation du curseur, développée par Julien Lecomte. Le code C# et ses ressources sont conservés dans `Haloryva_Windows64/`.

## État au 10 novembre 2026

- Compilation Windows x64 : OK.
- Tests de protocoles et autotests de l'application et de la traînée : OK, cinq étapes avec code 0 dans `RESULTAT_HALORYVA_C4.json`.
- Installation graphique, X1/X2 sur souris physique, rendu réel des effets et désinstallation : **à valider**.
- Aucun installateur Haloryva C4 n'est publié en release.
- La release CursorFlow Studio **v2.6.6** reste un **brouillon** préservé comme référence de retour arrière.

## Architecture

- `Haloryva_Windows64/src/` — application Windows Forms, dessin, souris et diagnostics.
- `Haloryva_Windows64/installer/` — assistant graphique d'installation/désinstallation.
- `Haloryva_Windows64/assets/` — icône, manifestes et visuels.
- `Haloryva_Windows64/tests/maintenance/` — tests C# de protocole.

La compilation de l'installateur s'effectue localement avec `csc.exe` de .NET Framework x64 sous Windows PowerShell 5.1. Les résultats Windows et les livrables sont gérés sur le Drive du projet, séparément du code versionné. Les versions historiques restent dans l'historique Git et les copies de sauvegarde.

**Licence :** droits réservés © Julien Lecomte. Ce dépôt est privé ; aucune licence de redistribution publique n'est accordée par défaut.
