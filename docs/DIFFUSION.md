# Préparer la diffusion — sans publier automatiquement

## Périmètre actuel

Le dépôt **JulienLecomteGit/cursorflow-studio** est préparé en privé. Seuls les documents
et métadonnées y sont versionnés. Aucun EXE ni source applicative n'y est envoyé à cette étape.
Les autorisations de publication, de visibilité et d'envoi de fichiers restent distinctes.

## Les trois fichiers prévus pour les destinataires

| Fichier | Origine |
|---|---|
| CursorFlowSetup.exe | Copie du setup 2.6.6 déjà construit et contrôlé sur le PC de l'auteur |
| LIRE_AVANT_INSTALLATION.txt | [Notice](../releases/2.6.6/LIRE_AVANT_INSTALLATION.txt) |
| SHA256SUMS.txt | [Empreinte](../releases/2.6.6/SHA256SUMS.txt) |

Empreinte du setup attendu : `F4A66366B2AF1CA0A8D131517F78F2566F09C049B20401B3AFA5DA1039515C67`.

Ne pas reconstruire B pour l'héberger. Ne pas prendre le setup 2.6.5 ou un ancien
exécutable portant le même nom. Les chemins personnels sont exclusivement dans le guide
local privé, jamais dans ces documents.

## Prochaine décision : un brouillon privé

Après accord explicite de Julien pour créer le brouillon et y déposer le binaire :

1. Vérifier que le dépôt est toujours privé et que les fichiers à joindre sont les trois
   éléments autorisés ci-dessus. Contrôler l'empreinte de la copie locale du setup.
2. Dans GitHub, ouvrir **Releases → Draft a new release**.
3. Préparer le tag **v2.6.6**, ciblant la branche **main**, et le titre
   **CursorFlow Studio 2.6.6 — Windows x64**.
4. Utiliser [les notes préparées](../releases/2.6.6/NOTES_VERSION.md), ajouter uniquement
   les pièces autorisées, puis **Save draft**. Ne pas cliquer sur **Publish release**.
5. Vérifier le brouillon, ses fichiers et la visibilité avec l'auteur avant toute suite.

Ce texte décrit un parcours futur : il ne signifie pas qu'un tag ou une release existe.
Un dépôt privé n'est pas un lien public de téléchargement. La mention « pre-release »
n'est pas un contrôle de confidentialité. Toute publication ou ouverture du dépôt
nécessite un accord séparé et une revue de son contenu.

## Exclusions

Pas de sources C#, de kit de fabrication, de profil, de sauvegarde, de journaux bruts,
de guide HTML privé ni de chemin utilisateur. Le fichier .gitignore aide aux ajouts
locaux mais ne remplace pas une revue avant envoi. Ne pas ajouter de licence open source,
de service payant, de signature ou de workflow sans décision appropriée.

## Sécurité et conditions

Le setup actuel est non signé. En cas de blocage Windows, demander le message sans
recommander la désactivation des protections. Héberger sur GitHub n'ajoute aucune signature.
Les conditions d'utilisation et les droits sur les éléments tiers restent à vérifier
avant diffusion publique ; cette préparation n'attribue aucune licence automatiquement.

## Sources officielles

- [GitHub : releases, pièces jointes et brouillons](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)
- [Microsoft : fonctionnement et limites de Smart App Control](https://support.microsoft.com/en-US/Windows/Security/Threat-Malware-Protection/smart-app-control-frequently-asked-questions)

Références consultées le 8 novembre 2026. Aucun programme ni workflow n'est lancé par ces documents.
