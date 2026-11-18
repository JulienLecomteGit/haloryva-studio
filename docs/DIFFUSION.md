# Diffusion et transparence — Haloryva Studio 2.7.2

## État au 18 novembre 2026

| Élément | État vérifié ou documenté |
|---|---|
| Code de diffusion 2.7.2 | Issu d’exp4 validée, promotion contrôlée |
| Compilation et tests natifs du binaire 2.7.2.6 livré | Réussis au commit `32234c…`, run `44300357140` |
| Paquet de dépôt MSIX 2.7.2.0 x64 | 52 contrôles de structure réussis ; produit non signé par la CI pour Partner Center |
| Archive des sources livrées | 125 fichiers conformes au commit `32234c…` |
| WACK local et interface graphique 2.7.2 | Non exécutés pendant la CI |
| Soumission et certification Store 2.7.2 | Soumission nº 3 envoyée et certification réussie |
| Publication Store 2.7.2 | Lancée ; dernier écran Partner Center encore en cours, fin de publication non relue |
| Disponibilité et installation sur le PC testé | Paquet 2.7.2.0 x64 signé Store, état Ok, binaire 2.7.2.6 identique à la livraison |
| Conservation des réglages après mise à jour | 93 valeurs identiques, aucun champ modifié, manquant ou ajouté ; fichier identique octet pour octet |
| Relance, rendu visuel final et démarrage après reconnexion | Non encore confirmés pour cette installation |

La [fabrication finale du run 44300357140](https://github.com/JulienLecomteGit/haloryva-studio/actions/runs/44300357140) a réussi sur le commit `32234c0410e080add61742b29ee794b5a566fa2e`, après fusion de la PR nº 5. Le binaire extrait du MSIX correspond au binaire testé puis à celui retrouvé dans l’installation Store. Les preuves et empreintes finales figurent dans [VALIDATION-2.7.2.md](VALIDATION-2.7.2.md).

La qualification intermédiaire `64d97384…`, run `44298963756`, reste conservée comme historique. La livraison Store reste celle du commit `32234c…` et du run `44300357140`. Les mises à jour documentaires de main et leurs éventuelles fabrications possèdent leurs propres résultats ; elles ne remplacent ni les ZIP scellés ni le paquet déjà livré et installé.

Un binaire compilé, un paquet structurellement vérifié, un paquet certifié et une installation effective restent des étapes distinctes.

[Fiche Microsoft Store](https://apps.microsoft.com/detail/9NJ14SZJP55B) · [Installation](INSTALLATION.md) · [Validation 2.7.2](VALIDATION-2.7.2.md) · [Confidentialité](confidentialite/README.md)

## Canal retenu

Microsoft Store reste le canal principal de distribution. Aucun installateur EXE 2.7.2 autonome n’est proposé ici. La certification du paquet Store ne signe pas un EXE distribué séparément. Les anciens fichiers de releases restent historiques.

La mise à jour 2.7.2 a été reçue et contrôlée sur le PC testé. Ce constat établit sa disponibilité pour ce poste ; il ne confirme pas sa diffusion dans tous les marchés et sur tous les appareils, ni le statut final du portail. Le lien Store renvoie vers la version effectivement proposée par Microsoft.

## Base validée : exp4

Le 18 novembre 2026, la copie expérimentale 2.7.2-exp4 a compilé sur Windows, passé ses suites automatisées et terminé normalement une recette locale d’environ quatre minutes. Le retour visuel confirme une traînée Ligne / Lumière sans maillons perçus, un aspect agréable et aucune des trois anomalies d’affichage signalées pendant cette session.

Les mesures ne montrent ni perte d’entrée connue ni échec de publication du calque, et les journaux sont complets. Elles ne constituent pas une mesure des FPS effectivement présentées à l’écran. Aucune garantie permanente de fluidité ni de disparition des incidents rares n’est annoncée ; aucune réparation autonome réelle n’a été exercée pendant cette recette. Fabrication native, certification Microsoft et contrôle de mise à jour sont désormais établis par leurs preuves respectives ; la relance et la recette visuelle de l’installation finale restent à confirmer. Voir [VALIDATION-2.7.2.md](VALIDATION-2.7.2.md).

## Référence historique : 2.7.1

La version 2.7.1 a été certifiée et sa publication Store a été clôturée selon le bilan conservé du 14 novembre 2026. Sa fiche publique a été constatée accessible en France. Les sept marchés n’ont pas été vérifiés individuellement.

La signature Store et l’état OK du paquet installé ont été lus ; le binaire 2.7.1.11 a été rapproché de l’empreinte du binaire validé. Des essais utilisateur ont confirmé l’installation, les langues, la reprise de réglages, le démarrage automatique et leur conservation après reconnexion. Un autre essai utilisateur sur un second PC a été rapporté séparément.

Ces constats historiques ne certifient pas la 2.7.2. Ils ne qualifient pas tous les systèmes, matériels, profils ou fichiers PNG/WAV. Aucun contrôle WACK local de la 2.7.1 n’a été déclaré ; sa certification provenait de la soumission Store. Aucun support Windows 10 ou ARM64 natif n’est annoncé.

## Sources, droits et données

La [provenance 2.7.2](SOURCES-2.7.2.md) identifie les sources reprises et les modifications de promotion. La [provenance 2.7.1.11](SOURCES-2.7.1.11.md) et son inventaire restent conservés comme références historiques.

© 2026 Julien Lecomte — tous droits réservés. Aucune nouvelle licence open source n’est accordée.

Les tickets [GitHub Issues](https://github.com/JulienLecomteGit/haloryva-studio/issues) sont publics. Aucun profil utilisateur, sauvegarde personnelle, journal brut ou donnée de compte n’est ajouté à la documentation de diffusion. Si Windows bloque un fichier, conservez les protections actives et relevez le message exact pour l’assistance.

[État historique 2.7.0](archives/2.7.0/DIFFUSION.md)
