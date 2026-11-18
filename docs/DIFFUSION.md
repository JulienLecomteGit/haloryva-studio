# Diffusion et transparence — Haloryva Studio 2.7.2

## État au 18 novembre 2026

| Élément | État vérifié ou documenté |
|---|---|
| Code de diffusion 2.7.2 | Issu d’exp4 validée, promotion contrôlée |
| Compilation et tests natifs du binaire 2.7.2.6 | Réussis sur le commit de qualification `64d97384…` |
| Paquet MSIX 2.7.2.0 x64 | 52 contrôles de structure réussis ; non signé et non installé |
| Archive des sources de qualification | 125 fichiers conformes au commit |
| WACK local et interface graphique 2.7.2 | Non exécutés pendant la CI |
| Soumission, certification et publication Store 2.7.2 | Non effectuées à ce stade |
| Installation de la 2.7.2 sur un PC | Non établie |
| Dernière diffusion publique documentée | 2.7.1 / binaire 2.7.1.11 / paquet 2.7.1.0 x64 |

La [fabrication Windows de qualification](https://github.com/JulienLecomteGit/haloryva-studio/actions/runs/44298963756) et le [contrôle de PR](https://github.com/JulienLecomteGit/haloryva-studio/actions/runs/44298972324) ont réussi sur le commit `64d97384dfa2c5bfd2618c43b348dff20a863be2`. Le binaire extrait du MSIX correspond au binaire testé. Les preuves et empreintes sont rattachées à ce run dans [VALIDATION-2.7.2.md](VALIDATION-2.7.2.md).

Le paquet à déposer au Store doit être celui de la fabrication réussie du commit final de diffusion. Les [derniers jobs de main](https://github.com/JulienLecomteGit/haloryva-studio/actions/workflows/haloryva-2.7.2.yml?query=branch%3Amain) fournissent leurs propres bilans et artefacts. Les empreintes du run de qualification ne sont pas annoncées comme celles d’un futur paquet déposé.

Un binaire compilé, un paquet structurellement vérifié, un paquet certifié et une installation effective restent des étapes distinctes.

[Fiche Microsoft Store](https://apps.microsoft.com/detail/9NJ14SZJP55B) · [Installation](INSTALLATION.md) · [Validation 2.7.2](VALIDATION-2.7.2.md) · [Confidentialité](confidentialite/README.md)

## Canal retenu

Microsoft Store reste le téléchargement principal gratuit. Aucun EXE 2.7.2 autonome n’est proposé aux utilisateurs par cette préparation. La certification d’un paquet Store ne signerait pas un EXE distribué séparément. Les anciens fichiers de releases restent historiques.

Les sources et la documentation GitHub peuvent être publiées avant que la mise à jour soit disponible dans le Store. Le lien Store renvoie vers la version réellement proposée par Microsoft ; sa présence dans le README ne prouve pas que la 2.7.2 est disponible.

## Base validée : exp4

Le 18 novembre 2026, la copie expérimentale 2.7.2-exp4 a compilé sur Windows, passé ses suites automatisées et terminé normalement une recette locale d’environ quatre minutes. Le retour visuel confirme une traînée Ligne / Lumière sans maillons perçus, un aspect agréable et aucune des trois anomalies d’affichage signalées pendant cette session.

Les mesures ne montrent ni perte d’entrée connue ni échec de publication du calque, et les journaux sont complets. Elles ne constituent pas une mesure des FPS effectivement présentées à l’écran. Aucune garantie permanente de fluidité ni de disparition des incidents rares n’est annoncée ; aucune réparation autonome réelle n’a été exercée pendant cette recette. La qualification native du binaire 2.7.2.6 et du MSIX est désormais acquise sur le commit cité ; elle reste distincte de la recette visuelle d’exp4, de la certification Microsoft et d’une installation réelle. Voir [VALIDATION-2.7.2.md](VALIDATION-2.7.2.md).

## Référence historique : 2.7.1

La version 2.7.1 a été certifiée et sa publication Store a été clôturée selon le bilan conservé du 14 novembre 2026. Sa fiche publique a été constatée accessible en France. Les sept marchés n’ont pas été vérifiés individuellement.

La signature Store et l’état OK du paquet installé ont été lus ; le binaire 2.7.1.11 a été rapproché de l’empreinte du binaire validé. Des essais utilisateur ont confirmé l’installation, les langues, la reprise de réglages, le démarrage automatique et leur conservation après reconnexion. Un autre essai utilisateur sur un second PC a été rapporté séparément.

Ces constats historiques ne certifient pas la 2.7.2. Ils ne qualifient pas tous les systèmes, matériels, profils ou fichiers PNG/WAV. Aucun contrôle WACK local de la 2.7.1 n’a été déclaré ; sa certification provenait de la soumission Store. Aucun support Windows 10 ou ARM64 natif n’est annoncé.

## Sources, droits et données

La [provenance 2.7.2](SOURCES-2.7.2.md) identifie les sources reprises et les modifications de promotion. La [provenance 2.7.1.11](SOURCES-2.7.1.11.md) et son inventaire restent conservés comme références historiques.

© 2026 Julien Lecomte — tous droits réservés. Aucune nouvelle licence open source n’est accordée.

Les tickets [GitHub Issues](https://github.com/JulienLecomteGit/haloryva-studio/issues) sont publics. Aucun profil utilisateur, sauvegarde personnelle, journal brut ou donnée de compte n’est ajouté à la documentation de diffusion. Si Windows bloque un fichier, conservez les protections actives et relevez le message exact pour l’assistance.

[État historique 2.7.0](archives/2.7.0/DIFFUSION.md)
