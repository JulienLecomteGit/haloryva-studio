HALORYVA STUDIO — COPIE DE LIVRAISON 2.7.1 EN PREPARATION
11 novembre 2026 — F01 à F03 uniquement ; aucune compilation ou exécution.

Cette copie reprend les 36 fichiers d'entrée de l'application preview.9.
Deux fichiers sont retouchés : src/Brand.cs et languages/strings.json.
Les 34 autres entrées, dont 32 fichiers C#, l'icône et le manifeste applicatif, sont identiques par objet Git.
Les sources d'origine haloryva-ci/preview9 et tous les dossiers de preuves restent conservés.

BRANCHE ET BASE
Branche nouvelle : haloryva-preparation-livraison-2.7.1
Base du dépôt : dca613b3714ccc85a2d1930f1a0cb5e23e91388c
Source : haloryva-ci/preview9/
Compilation historique preview.9 : a6c817fc8601b7c5b4f90da46d00fb7f9cd16fd5 / run 43035196363.
L'EXE preview.9 demeure 678DB7DBBAB906A438F01F83A27D8A4D9C1904D1E280880D148FE3A1610522DC.
Aucun nouvel EXE, installateur ou MSIX n'est produit.

LIRE
BILAN-F01-F03.txt : modifications précises et points encore non exécutés.
comparaison-preview9.diff : différences de texte entre les deux fichiers source et leur nouvelle copie.
modifications.json : changements structurés.
source-origin.json : provenance de chaque fichier copié.
preservation.json : périmètre de conservation.

ETAT DE FABRICATION
La version informative est préparée en 2.7.1 ; la version de fichier proposée est 2.7.1.10.
Brand.Version et AssemblyVersion restent 2.7.1 / 2.7.1.0.
BuildDate contient __DATE_FABRICATION__. Ce marqueur est volontaire : aucune date de compilation fictive.
La première entrée d'historique utilise BuildDate pour éviter deux dates divergentes.

AVANT TOUTE FABRICATION ULTERIEURE AUTORISEE
La future recette devra dater sa copie de travail au moment réel de fabrication,
remplacer exactement le marqueur BuildDate et refuser de compiler tant qu'il subsiste.
Le format attendu est jj/MM/aaaa, avec provenance de l'instant et du fuseau utilisés.
Vérifier également qu'aucune révision intermédiaire n'a consommé 2.7.1.10.
Cette recette n'est ni créée ni exécutée dans cette tâche.

Aucun script Build-Preview9.ps1 ni ancienne provenance de compilation n'est recopié comme recette de livraison :
ils ciblent preview.9 et doivent rester historiques. Leur adaptation appartient à l'étape suivante.
Les workflows existants ne ciblent pas cette nouvelle branche ; aucun workflow n'est ajouté ou modifié.
Les helpers et autotests présents dans les sources sont seulement copiés, jamais exécutés.

STATUT
Sources préparées pour revue ; validation par compilation et contrôle visuel non effectuée.
F01–F03 ne sont pas déclarés validés par le simple changement de texte.
F04–F06, démarrage MSIX lors d'une vraie session, certification et autres limites du dossier Store restent distincts.
