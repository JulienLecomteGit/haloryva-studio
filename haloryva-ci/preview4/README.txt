PREVERSION 2.7.1-preview.4 — CORRECTION CIBLEE DU PLACEMENT

Base conservée : haloryva-tests-preview3, c74f4d66c6a7f742e338408e427e29da3fa02eb5.
Binaire preview.3 conservé : 4170BEFEC193F5049051D73744CFCAD6D300240CE840E791E2C469A7CECAB13C.

Correction :
- Choisir le placement manuel avant InitScale, qui peut créer le handle Windows.
- Après le calcul de taille existant, fixer une fois les coordonnées centrées
  dans la zone de travail du moniteur sous le pointeur, en tenant compte de son origine.
- Ne pas recentrer une fenêtre existante lors de ShowSettings.
- Seuls PremiumStudioForm.cs et les métadonnées Brand.cs changent.
- Tous les autres fichiers source et ressources sont recopiés par SHA Git.
  Build-Preview4.ps1 contrôle ces invariants avant compilation.

Recette autorisée uniquement sur les machines éphémères de GitHub :
- Une compilation preview.4 ; même binaire téléchargé par les deux jobs.
- Deux lancements réels de l'exécutable, profils neufs, sans déplacement de fenêtre
  ni clic injecté : position, centrage, capture et arrêt propre.
- Commandes natives du Studio, sans le recentrage précédemment nécessaire au pilote :
  position initiale, agrandir/rétablir, conservation de l'état agrandi, navigation,
  outils, champs, dessin depuis Studio, historique, export, fermeture/réouverture.
- Contrôle de la version et de l'empreinte avant/après les essais.
- Les fonctions de lissage/formes du pilote commun ne sont pas invoquées :
  Run-Studio.ps1 n'autorise que la suite studio.
- Aucun des 195 contrôles historiques n'est invoqué.
- Pas de modification de main, preview.2, preview.3 ; ni installation locale,
  signature, MSIX ou publication d'une version.

Les résultats ne doivent être déclarés validés qu'après lecture des jobs et artefacts.
Limites : bureau Windows Server 2025 x64 de GitHub ; écrans multiples, DPI variés,
gestes physiques Windows 11 et MSIX de preview.4 restent hors de cette recette.

Idée utilisateur à traiter ensuite, dans une préversion distincte :
choisir les outils de dessin inclus dans le défilement des boutons.
Exemple : deux outils choisis => alternance uniquement entre ces deux outils.
Conserver par défaut la liste actuelle pour les réglages existants ; prévoir
la sélection persistante, les cas un/deux outils, et les changements pendant un trait.
Cette fonction n'est pas implémentée dans preview.4.
