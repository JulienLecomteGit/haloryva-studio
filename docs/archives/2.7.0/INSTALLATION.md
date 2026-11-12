# Installer Haloryva Studio 2.7.0 sur Windows

Cette notice concerne **Haloryva Studio 2.7.0**, Windows x64. Le parcours a été préparé pour Windows 11 x64 ; la compilation et l'extraction du setup ont été vérifiées, mais une nouvelle installation complète de cette fabrication reste à confirmer.

## 1. Télécharger et vérifier

1. Ouvre la [release GitHub v2.7.0](https://github.com/JulienLecomteGit/haloryva-studio/releases/tag/v2.7.0).
2. Télécharge **`HaloryvaSetup_2.7.0.exe`** dans `Assets`.
3. Dans **Windows PowerShell 5.1**, vérifie son empreinte sans lancer le fichier :

   ```powershell
   Get-FileHash -LiteralPath "$env:USERPROFILE\Downloads\HaloryvaSetup_2.7.0.exe" -Algorithm SHA256
   ```

4. Le résultat doit être exactement : `A60215938B82EFBAD1B744E4E85911256426103F4D18E7F1B2213E9C4BC6178A`. Si différent, arrête et supprime uniquement cette copie suspecte après vérification ; ne l'exécute pas.

## 2. Installer ou mettre à jour

1. Si Haloryva fonctionne déjà, quitte l'application depuis son icône près de l'horloge (fermer la fenêtre du Studio ne suffit pas toujours).
2. Conserve une copie de tes préférences et de l'ancien installateur dans un emplacement sûr.
3. Ouvre `HaloryvaSetup_2.7.0.exe` par double-clic et vérifie le nom et la version affichés.
4. Choisis les options souhaitées, puis **Installer** ou **Mettre à jour**, selon l'écran proposé. Termine l'assistant.
5. Vérifie dans **À propos** : `2.7.0` sans « candidat »/« C4 ». Contrôle ensuite le halo, la traînée et les affectations X1/X2 selon ta souris. Le préréglage courant est personnalisable.

Les profils ne doivent pas être écrasés manuellement : utilise l'import/export intégré si nécessaire.

## 3. Désinstaller

Dans **Paramètres Windows → Applications → Applications installées**, recherche **Haloryva Studio**, puis ouvre **Désinstaller**. Si l'assistant propose de conserver les réglages, garde-les pour faciliter un retour arrière. Le cycle complet de désinstallation/réinstallation de cette fabrication 2.7.0 n'est pas encore documenté comme réussi.

## 4. Sécurité et dépannage

- L'installateur n'est pas signé numériquement. Un contrôle SHA-256 ne remplace pas une signature ni une analyse de sécurité.
- Si Microsoft Defender, Smart App Control ou une règle de ton organisation bloque l'exécution, **ne contourne pas cette protection** ; conserve le message exact.
- En cas de comportement inattendu des boutons, **Ctrl + Alt + R** rétablit les actions Windows normales selon le mécanisme prévu par l'application.
- Ne transmets ni profil privé, ni chemin personnel complet, ni données sensibles dans un rapport public.
