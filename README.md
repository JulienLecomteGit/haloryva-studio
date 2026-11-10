# Haloryva Studio

<p align="center">
  <img src="docs/Haloryva_Banniere_V01.png" alt="Bannière Haloryva Studio : curseur lumineux et halo" width="900">
</p>

**Version 2.7.0 — Windows x64**  
Créé par Julien Lecomte.

Haloryva Studio accompagne les présentations, démonstrations et cours sur Windows : effets de halo autour du pointeur, traînées réglables, annotations et outils de dessin, ainsi que raccourcis configurables sur les boutons de la souris.

## Télécharger

- [Version 2.7.0 — GitHub Releases](https://github.com/JulienLecomteGit/haloryva-studio/releases/tag/v2.7.0)
- **Installateur :** `HaloryvaSetup_2.7.0.exe` — Windows 11 x64 testé.
- **SHA-256 :** `A60215938B82EFBAD1B744E4E85911256426103F4D18E7F1B2213E9C4BC6178A`.

L'application est précompilée : son installation normale ne nécessite ni Python, ni Visual Studio, ni compilation manuelle. Consulte la [notice d'installation](docs/INSTALLATION.md) avant la première utilisation.

## Fonctionnalités

- Halo du curseur et traînées personnalisables.
- Dessin, annotations, présentation et raccourcis clavier.
- Affectation des boutons de souris, dont X1 (arrière) et X2 (avant), avec appuis courts et longs selon les options disponibles.
- Profils locaux exportables et importables ; paramètres visuels et fluidité configurables.
- Usage local sur Windows.

## État des vérifications — 10 novembre 2026

- La fabrication Windows de **Haloryva Studio 2.7.0** a passé **8 étapes** : compilation de l'application et des tests, protocoles, autotests de l'application et des traînées, compilation de l'installateur, autotest de l'installateur, extraction et comparaison de l'EXE extrait.
- **108 assertions de protocole** et **63 contrôles de traînée** réussis ; les sorties de contrôle et les empreintes sont conservées dans le dossier de preuves du projet.
- L'installateur final téléchargé depuis Google Drive a été comparé à l'empreinte du rapport de fabrication : **identique**.
- La préversion précédente a été lancée sous Windows et soumise à des essais ciblés par Julien : boutons X1/X2 et traînée Ligne à 50 %. Le passage à 2.7.0 stable ne change que les libellés de marque/version, sans modification revendiquée du moteur souris.
- **Limites :** le cycle de désinstallation/réinstallation du nouvel installateur, un essai prolongé et exhaustif des effets, les autres styles de traînée et un second PC Windows restent à vérifier. La version est déclarée stable par son auteur ; ces tests ne constituent pas une certification extérieure.

## Code et historique

- `Haloryva_Windows64/src/` : application Windows Forms en C#.
- `Haloryva_Windows64/installer/` : assistant graphique d'installation.
- `Haloryva_Windows64/tests/maintenance/` : tests C#.
- `Haloryva_Windows64/assets/` : ressources de l'application.

L'historique technique est décrit dans le [CHANGELOG](CHANGELOG.md). Les préversions antérieures sont conservées comme archives de travail et ne sont pas des versions de téléchargement recommandées.

## Identité visuelle

<img src="docs/Haloryva_Logo_V01.png" alt="Logo Haloryva Studio : pointeur et halo lumineux" width="160">

Logo et bannière de présentation V01 : ils ne remplacent pas encore les ressources intégrées à l'installateur Windows 2.7.0 contrôlé.

## Licence et sécurité

**© 2026 Julien Lecomte — tous droits réservés.** La visibilité publique de ce dépôt n'en fait pas un projet sous licence open source. Les droits de consultation et de duplication sur GitHub suivent les conditions de GitHub ; aucune licence générale de redistribution ou de modification du code n'est accordée par ce README.

L'installateur n'est **pas signé numériquement**. Une empreinte SHA-256 vérifie l'identité du fichier, pas son innocuité. Si Windows ou une politique d'organisation bloque l'installation, ne désactive aucune protection. Voir [diffusion et limites](docs/DIFFUSION.md).
