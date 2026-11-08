# Distribution de CursorFlow Studio 2.6.6 — plan avant publication

> **Documentation mise à jour dans le dépôt privé.** Aucun changement de visibilité ni publication de release n’est autorisé par ce document.

## État vérifié le 8 novembre 2026

- Dépôt `JulienLecomteGit/cursorflow-studio` : **privé**, branche `main`.
- Release `v2.6.6` : **brouillon privé**, id `438533321`, `published_at=null`.
- Trois pièces : `CursorFlowSetup.exe` (446 976 octets), `LIRE_AVANT_INSTALLATION.txt`, `SHA256SUMS.txt`.
- L'empreinte GitHub du setup est **identique** à celle du binaire 2.6.6 testé : `F4A66366B2AF1CA0A8D131517F78F2566F09C049B20401B3AFA5DA1039515C67`.
- Aucune signature Authenticode sur cette fabrication ; les sources applicatives ne figurent pas dans `main`.
- **Aucun canal de téléchargement public autorisé pour l'instant.**

## Modèle retenu

CursorFlow est envisagé comme une application **gratuite, à code propriétaire**. Une licence d'utilisation explicite doit autoriser l'installation et l'usage du binaire sans autoriser, par défaut, la redistribution du code source, la revente ni la republication de fichiers modifiés.

Avant d'afficher des conditions définitives : vérifier les droits sur les éléments tiers, l'identité/contact public de l'éditeur, la portée des garanties obligatoires et l'applicabilité du règlement européen sur la cyberrésilience.

## Avant toute publication

1. Réaliser un essai volontaire sur un **autre PC Windows 11 x64**, si possible non administré ; conserver le premier message en cas de blocage sécurité. **Ne pas désactiver Smart App Control, Defender ni une stratégie d'organisation.**
2. Valider les conditions d'utilisation, la notice de confidentialité et la politique de signalement de sécurité, actuellement préparées **hors du dépôt** (pas encore publiées).
3. Maintenir README et manifeste cohérents avec le brouillon ; leur actualisation documentaire fait partie de la présente validation.
4. Contrôler le dépôt et **son historique Git complet** avant un passage en public, même si seul le contenu courant semble propre.
5. Déterminer si l'on publie **le dépôt documentaire existant** ou si l'on crée plus tard un dépôt public séparé ; **un dépôt public rend son historique lisible**.
6. Choisir une voie de distribution et de confiance Windows : GitHub avec EXE non signé (risque de blocage), signature CA reconnue (payante), ou empaquetage MSIX Store (autre produit de distribution à tester). Aucun achat ni migration décidés.
7. Obtenir un accord explicite, distinct, pour rendre le dépôt public et/ou cliquer sur « Publish release ».

## Interpréter correctement l'état du brouillon

Le tag **nommé** `v2.6.6` apparaît sur l'objet release ; les URL de téléchargement du brouillon peuvent utiliser un identifiant `untagged-...`. Ne pas promettre qu'un lien public stable existe avant publication. Le dossier `releases/2.6.6` de la branche `main` contient des **documents**, pas les actifs binaires d'une release.

## Compatibilité et sécurité

- Environnement réellement éprouvé : un poste Windows 11 x64 de référence, avec essais de fluidité et mise à jour.
- À contrôler séparément : installation sur un deuxième PC, échelles d'affichage, absence de régression des boutons et du dessin, désinstallation de la **2.6.6** et démarrage après reconnexion.
- Installateur non signé : **ne pas conseiller un contournement** des protections Windows. Une empreinte SHA-256 sert seulement à contrôler l'identité du fichier.
- GitHub Releases héberge le binaire mais ne lui confère aucune signature numérique.

## Documents d'aide

- [Validation antérieure 2.6.6](VALIDATION_2.6.6.md)
- Conditions d’utilisation proposées : **en cours de validation hors GitHub**, non publiées.
- Notice de confidentialité : **en cours de validation hors GitHub**, non publiée.
- Politique de signalement de sécurité : **à finaliser hors GitHub**, non publiée.
- Plan de pilote sur second PC : à conserver d'abord hors dépôt, jusqu'à validation.

## Références officielles

- [GitHub : licences et droits par défaut](https://docs.github.com/fr/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)
- [GitHub : changer la visibilité](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/setting-repository-visibility)
- [Microsoft : options de signature](https://learn.microsoft.com/fr-fr/windows/apps/package-and-deploy/code-signing-options)
- [Microsoft : Smart App Control](https://support.microsoft.com/fr-fr/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions)
- [Commission européenne : CRA et signalement](https://digital-strategy.ec.europa.eu/en/policies/cra-reporting)

**Ce texte n'est pas une autorisation de publier.**
