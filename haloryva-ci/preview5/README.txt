PREVIEW.5 — CHOIX DES OUTILS PARCOURUS PAR LES BOUTONS

Base : preview.4, e7eefcf5aa8d1f1df97567fbff56e40965d8b69f.
Nouvelle branche : haloryva-tests-preview5. Aucun fichier antérieur modifié.

Fonction :
- Dessin > Défilement : 10 interrupteurs, au moins un actif.
- Suivant/précédent ne parcourt que les outils activés, dans l'ordre existant.
- Tous les outils restent disponibles dans Dessiner.
- Si l'outil actif est hors sélection, suivant rejoint le premier sélectionné,
  précédent le dernier.
- Choix sauvegardé automatiquement dans DrawingToolCycle, exportable/importable.
- Profils anciens : tous les outils inclus ; liste vide/invalide : retour à tous.
- Noms inconnus et doublons éliminés, ordre canonique conservé.
- Choix d'un seul outil : les boutons restent sur cet outil.
- L'outil d'un trait en cours reste celui capturé au début ; le suivant utilise
  le nouvel outil. Le code des hooks et du rendu est intégralement préservé.

Seuls 4 fichiers source diffèrent de preview.4 :
Config.cs, Controller.cs, PremiumStudioForm.cs, Brand.cs.
La compilation vérifie les SHA Git de toutes les sources preview.4 conservées
et interdit toute autre différence dans le nouveau snapshot.

Recette prévue et lancée uniquement sur GitHub hébergé :
- compilation unique, identité/empreinte du même binaire contrôlées dans les jobs ;
- sélection native dans le Studio et affectations par clics/clavier ;
- cycles complets par défaut, un/deux/trois outils, deux sens, bouclage,
  outil direct hors sélection, molette/X1/X2, clic court/appui long ;
- indicateur d'outil et changement par bouton pendant un trait ;
- réouverture, autosauvegarde, export/import, nouveau processus rechargeant le profil ;
- deux variantes : avec et sans hook observateur dans l'application cible ;
- contrôle du centrage hérité de preview.4 dans ces ouvertures.

Le pilote charge l'assemblage réel et instancie le contrôleur de production.
Il utilise l'introspection pour l'observation et quelques réglages de préparation ;
les actions fonctionnelles passent par les contrôles du Studio ou SendInput.
Le rechargement se fait dans un nouveau processus de pilote, sans réinitialiser
le profil, en réinstanciant le contrôleur et ConfigStore.Load de l'application.
Ce n'est pas une installation ni un test MSIX du point d'entrée empaqueté.

Les fonctions héritées d'autres suites restent présentes dans le pilote commun,
mais Functional ne peut lancer que CycleRecipe, puis ReloadCycle.
Aucun des 195 contrôles, aucune ancienne suite complète n'est invoqué.
Pas de lancement local, signature, publication/release, MSIX ou modification système.

Résultats : à consulter dans le bilan à la racine une fois les runs terminés.

MISE À JOUR APRÈS RECETTE DU 11/11/2026
Fonction : 215 assertions réussies par variante dans le run 42944347258.
Le premier rechargement demandait à tort un dossier TrailTrial déjà existant :
échec du pilote avant chargement de l'application, conservé dans les preuves.
Le run 42944896117 valide le rechargement corrigé : 45 assertions par variante,
sur le même binaire E259BC782D8ED909912A397F83F508AC254AC6483DBF0787B7917A6C68F90B40.
Les profils originaux sont copiés octet pour octet vers un nouveau dossier d'essai ;
le contrôleur de production charge les réglages avec ConfigStore.Load.
Le pilote corrigé testé et le passage du chemin enregistré sont reportés ici
pour les futures exécutions combinées. Ce raccordement complet features+reload
n'a pas été relancé après correction : les deux phases ont été validées dans
les deux runs distincts, sans recompiler preview.5 ni répéter les 215 assertions.
Le commit de bilan utilise [skip ci] pour conserver l'unique binaire déjà testé.
