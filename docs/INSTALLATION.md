# Installer et utiliser CursorFlow Studio 2.6.6

> Cette notice décrit le parcours de la version de référence. Aucun EXE n'est encore
> joint dans ce dépôt. N'utilise pas une archive de sources comme installateur.

## Avant de commencer

La cible essayée est Windows 11 sur PC x64. Le fichier nécessaire est
**CursorFlowSetup.exe**, contenant l'application précompilée. Aucun kit source,
compilateur, script PowerShell, Python ou Visual Studio n'est nécessaire pour ce parcours.

Vérifie la provenance de l'EXE et son identité dans
[SHA256SUMS.txt](../releases/2.6.6/SHA256SUMS.txt). L'empreinte ne remplace ni une signature
numérique ni une évaluation de sécurité. En cas de refus de Windows ou de ton organisation,
arrête et conserve le premier message : ne désactive aucune protection.

## Première installation

1. Après mise à disposition autorisée, télécharge **CursorFlowSetup.exe**.
2. Ouvre-le normalement par double-clic, sans demander les droits administrateur.
3. Vérifie le nom CursorFlow et la version **2.6.6**. Choisis les options de raccourci
   Bureau et de démarrage avec Windows.
4. Clique sur **Installer**, puis **Terminer**. L'option de lancement immédiat ouvre le Studio.

Ne confonds pas la fin de copie avec le lancement réussi : signale toute erreur à l'une
ou l'autre étape.

## Mettre à jour une version existante

1. Quitte CursorFlow depuis son icône près de l'horloge. Fermer seulement le Studio
   ne suffit pas. Quitte aussi les éventuelles sessions d'essai.
2. Conserve une copie de l'installation et de ses préférences, ainsi que l'ancien setup,
   dans un emplacement de sauvegarde distinct. Ne remplace pas une ancienne sauvegarde.
3. Ouvre le setup **2.6.6**, vérifie **Mise à jour**, puis tes options de raccourci et de démarrage.
4. Utilise **Mettre à jour**, puis **Terminer**. Ne désinstalle pas d'abord pour cette mise à jour.
5. Vérifie la version dans **À propos**, les réglages et le comportement normal.

Le profil d'un essai isolé n'est pas importé automatiquement. Ne remplace pas un fichier
settings.json pour obtenir les réglages d'une démonstration.

## Désinstaller depuis Windows

Ouvre **Paramètres → Applications → Applications installées → CursorFlow Studio → … →
Désinstaller**, puis suis l'assistant graphique.

Pour conserver les préférences, laisse décochée **« Supprimer aussi mes réglages et
sauvegardes »**. Le dossier de données peut alors subsister : c'est volontaire.
Ne supprime pas manuellement le dossier installé pour désinstaller l'application.

Le cycle de désinstallation/réinstallation 2.6.5 avait été déclaré réussi. Il n'a pas
été réexécuté en 2.6.6 dans la recette reçue ; cette distinction est conservée dans
[le bilan de validation](VALIDATION_2.6.6.md).

## En cas de problème

Note la version, le premier message et les étapes. Pour un blocage de souris,
**Ctrl + Alt + R** remet les affectations de boutons à leur fonctionnement normal :
c'est une réinitialisation volontaire de ces réglages. Pour quitter le mode dessin,
utilise Échap ou le clic droit.

Ne fournis pas de données personnelles ou de sauvegardes complètes dans un signalement.
Référence Windows : [désinstaller depuis les paramètres](https://support.microsoft.com/fr-fr/windows/apps/uninstall-or-remove-apps-and-programs-in-windows).
