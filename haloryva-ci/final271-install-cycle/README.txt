HALORYVA 2.7.1 — RECETTE REELLE INSTALLATION / DESINSTALLATION
Périmètre autorisé : tester l'installateur existant issu du run 43088395238.
La seule exécution de Haloryva autorisée est l'autotest lancé automatiquement par l'installation.
Aucune compilation de l'application, de l'installateur ou d'un auxiliaire de pilotage.

FICHIERS FIGES
HaloryvaSetup_2.7.1.exe : BC782C5DD43A795E1F5666A08621F7176F2F4DF1663F844C8BD83A8E55BFFDB1
Haloryva.exe, version fichier 2.7.1.11 : 1CB026DDBA084A648832FBD91118A67000373E076DAFA72BE2957B47223A6F81
Archive d'entrée : HaloryvaSetup-2.7.1-preuves-fabrication, ID 13886898481,
run 43088395238, commit 4da5e2c9f55b51ef7f1b7963133dc7c2ccf95bb4.
Les empreintes sont vérifiées avant, sur les fichiers installés, puis après désinstallation.

RECETTE REUTILISEE
Base : haloryva-ci/preview9-install-cycle/Run-InstallCycle.ps1, cycle réussi
le 11 novembre 2026 dans le run 43065284869.
Même profil fictif préexistant, mêmes boutons, mêmes vérifications de maintien
des réglages et de suppression des éléments installés.
Adaptations : identités du setup final, dossier de preuves distinct et registre 2.7.1.
L'auxiliaire C# précédemment chargé par Add-Type n'est pas utilisé, car son
chargement recompilait du code. SetupUi.cs n'est ni compilé ni chargé.

PILOTAGE SANS COMPILATION
- Méthodes déjà compilées de System.dll pour les messages de fenêtre, résolues
  avant lancement par signature et sans chargement de code Haloryva en mémoire du pilote.
- UIAutomationClient / UIAutomationTypes / WindowsBase déjà présents dans Windows
  pour vérifier le propriétaire et les dimensions de la seule fenêtre de setup.
- Événements WMI déjà fournis par PowerShell pour observer les processus Haloryva.
- Accès Process.Handle conservé avant les commandes pour lire la sortie après fermeture.
- Composant Windows FileSystemObject pour comparer les noms courts/longs des chemins.
- Aucun Add-Type source, csc, compilation C# ou téléchargement de logiciel.
- Aucune capture nouvelle demandée ; preuves textuelles, fichiers, registre et événements.
Les méthodes SendMessage/PostMessage existantes sont documentées dans les sources Microsoft :
https://github.com/microsoft/referencesource/blob/main/System/compmod/microsoft/win32/UnsafeNativeMethods.cs
Le pilote refuse de continuer si ces composants sont indisponibles ; aucun repli compilé.

CE QUI EST TESTE
Windows GitHub windows-11-arm, Windows 11 ARM64 avec émulation du programme x64.
Installation réelle dans le profil jetable du runner et inscription dans Windows.
Le bouton Installer est commandé sur la seule fenêtre identifiée par PID, titre,
chemin de maintenance et empreinte. Cela ne valide pas le clic physique ni le focus.
La fermeture normale de l'assistant n'ouvre pas l'application après installation.
Désinstallation réelle depuis le désinstalleur installé, suppression des réglages décochée.
13 réglages conservés ; settings.json.bak et un fichier témoin sans données personnelles.
Comparaison des valeurs après installation, puis des fichiers à l'octet près après désinstallation.
Observation d'un seul processus Haloryva, enfant de l'installateur, terminé avec code 0.
La ligne --selftest est lue si le processus existe encore lors de la réception WMI ;
si sa lecture manque, le parent, la sortie et le chemin unique du code installateur sont distingués.

LIMITES
Profil synthétique préexistant, pas une véritable installation 2.7.0 ni sa mise à jour.
Pas de lancement normal de Haloryva, de rechargement visuel, de redémarrage/session réelle.
Pas de validation sur PC x64 physique ni de compte standard non élevé.
Pas de nouveau test MSIX, Sonidos, gestes ou 195 contrôles.
Pas de signature, publication, abonnement ou intervention sur le PC principal.
Les branches, sources, binaires et preuves antérieurs restent intacts.
