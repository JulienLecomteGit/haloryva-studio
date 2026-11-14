# Sources applicatives 2.7.1.11 — intégration préparée

Les 36 fichiers sous `stamped-source/` de l’archive de fabrication sont repris à l’identique dans `Haloryva_Windows64/` : 33 fichiers C#, un catalogue de langues, l’icône et le manifeste applicatif. Aucun code n’est corrigé pendant cette intégration.

## Provenance

- [Fabrication GitHub 43086180391](https://github.com/JulienLecomteGit/haloryva-studio/actions/runs/43086180391), archive 13886896454.
- Commit de fabrication : `ca14eca6be1e0a19ef9004ba0de2499e00c7c624`.
- Version affichée 2.7.1 ; fichier 2.7.1.11 ; assembly 2.7.1.0 ; date estampillée 11/11/2026.
- [Inventaire SHA256 des 36 fichiers](PROVENANCE-SOURCES-2.7.1.11.json).

L’archive et le binaire qu’elle contient ont les empreintes des preuves conservées. Le binaire est lu comme des données, sans extraction dans le dépôt ni exécution. Les sources ont été comparées octet par octet via SHA256 à leur entrée d’archive. Les attributs Git empêchent la conversion automatique des fins de ligne pour préserver ces empreintes.

## Évolutions reprises depuis 2.7.0

- Hooks souris/clavier et traitement des entrées séparés du rendu, politique AltGr et clics courts/longs.
- Paramètres de dessin figés au début du trait ; choix des outils parcourus et mémorisation.
- Interface FR/EN/ES, catalogue embarqué, pluriels et finitions du Studio et de l’historique.
- Contexte MSIX, migration consentie des profils, gestion du démarrage et actualisation au retour dans le Studio.
- Gestion des erreurs et diagnostics présents dans le binaire final.

## Limites

Cette intégration n’est pas une nouvelle fabrication ni une nouvelle validation fonctionnelle. Aucun test, autotest ou contrôle parmi les 195 n’a été relancé. Les résultats antérieurs restent ceux de leur contexte d’exécution.

L’archive ne contient pas les sources de l’installateur final, les scripts de fabrication ou le manifeste MSIX de production. Les dossiers `installer/` et `tests/maintenance/`, ainsi que les ressources non recensées dans l’inventaire, restent historiques et inchangés. Ils ne doivent pas être utilisés comme preuve de correspondance avec l’installateur final.

Aucune recette de compilation reproductible n’est revendiquée ici. Le catalogue doit notamment être embarqué sous le nom `Haloryva.Localization.json` ; les références Windows utilisées par la fabrication doivent être reprises de sa recette officielle avant une future compilation. Aucun script ni workflow n’est exécuté ou ajouté dans cette préparation.

La version 2.7.0 reste accessible dans l’historique Git. Aucun EXE, DLL, paquet MSIX, archive de fabrication, certificat ou donnée personnelle n’est ajouté. Le téléchargement destiné aux utilisateurs reste Microsoft Store.
