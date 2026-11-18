# Fabrication Windows de Haloryva Studio 2.7.2

Le workflow `haloryva-2.7.2.yml` compile le code de ce commit, exécute les suites autonomes et intégrées, puis fabrique le paquet Microsoft Store **non signé**. Aucun installateur EXE n’est fabriqué et aucune installation, signature ou publication n’est exécutée.

## Identités

| Élément | Valeur |
|---|---|
| Produit | 2.7.2 |
| Fichier | 2.7.2.6 |
| Assembly et manifeste applicatif | 2.7.2.0 |
| Paquet Store x64 | 2.7.2.0 |
| Vérificateur | R5, adaptation du R4 testé avec exp4 |

Le manifeste Store conserve l’identité, l’éditeur, les langues, la capacité `runFullTrust`, les bornes Windows et la tâche de démarrage du paquet 2.7.1. Les trois logos sont les octets extraits de l’artefact historique contrôlé ; ils ne sont pas régénérés pendant la fabrication.

## Contrôles obligatoires

1. Lecture de `SHA256SUMS-SOURCES.txt`, contrôle de l’empreinte de `SHA256.json`, puis de chacun des fichiers inventoriés. Les deux fichiers appartiennent au même commit : ils attestent la cohérence des octets, pas une signature externe.
2. Analyse de tous les scripts PowerShell du projet par le parseur Windows PowerShell 5.1, hors résultats précédents.
3. Six suites autonomes : récupération, positions, précision, aperçu/cadence, diagnostic et rendu ; compilation x64 .NET Framework avec un seul ensemble WinRT UnionMetadata ; deux autotests intégrés.
4. Vérification des huit codes de sortie, de l’identité du binaire et de son SHA256 avant packaging. Aucun paquet produit si un contrôle échoue.
5. Validation MakeAppx, décompression de contrôle, comparaison de chaque fichier du contenu et vérification de l’identité, des capacités et des fichiers attendus.

Les suites déjà exécutées dans le binaire ne sont pas comptées comme de nouveaux scénarios distincts des mêmes suites autonomes. Les tests de rendu portent sur des bitmaps synthétiques ; ils ne mesurent pas les FPS présentés par un écran.

## Exécution

GitHub Actions utilise `windows-2025`, Windows PowerShell 5.1 x64 et les outils .NET Framework/SDK déjà présents. Les déclenchements couvrent la branche `release/2.7.2`, les demandes de fusion vers `main`, puis `main`. Un lancement manuel est également déclaré. Les permissions du workflow sont limitées à la lecture du dépôt.

Sur une copie Windows préparée, la commande du workflow est :

```powershell
& .\build\Fabriquer.ps1 -ProjetSources (Get-Location).Path
```

Le dossier doit être celui des sources complètes et scellées. La fabrication requiert Windows PowerShell 5.1 Desktop x64, les références .NET Framework 4.8, UnionMetadata Windows et MakeAppx x64. Elle n’installe aucun prérequis. Le dossier `Livraison\Haloryva-2.7.2` doit être absent ; une nouvelle fabrication utilise une nouvelle copie des sources.

## Résultats

- `Resultats\Verification-R5-…` : compilations, autotests et compte rendu `Resume.json`.
- `Resultats\Preuves-R5-….zip` : preuves sans exécutables ou profils personnels.
- `Livraison\Haloryva-2.7.2` : paquet non signé, manifeste, bilans, preuves et empreintes.
- Artefact Actions `Haloryva-2.7.2-preuves-…` : conservé même si la fabrication échoue, lorsque des preuves existent.
- Artefact Actions `Haloryva-2.7.2-MSIX-…` : produit uniquement après réussite de toute la chaîne.
- Artefact Actions `Haloryva-2.7.2-sources-…` : ZIP complet créé par `git archive` du commit exact, avec chaque fichier comparé à son objet Git, puis empreinte SHA256 et provenance. Les bannières et autres fichiers hérités du dépôt sont inclus. La racine, le commit, la branche et le remote sont contrôlés avant archivage ; aucun dossier `.git` ou identifiant de connexion n’est exporté. L’archive est créée après les tests R5 et avant le packaging ; elle reste disponible si le packaging échoue. Une copie locale hors Git ne produit pas cette archive complète.

Le paquet `HaloryvaStudio_2.7.2.0_x64_STORE_UNSIGNED.msix` est destiné à la soumission Microsoft Store. Sa fabrication ne prouve ni sa certification, ni sa disponibilité publique, ni la mise à jour du PC de l’utilisateur. Ne pas installer ce paquet non signé en modifiant les protections ou les certificats du PC.

## Scellage des sources

Après toutes les modifications, régénérer `Inventaire-sources.json` pour l’application puis `SHA256.json` pour les fichiers préparés et contrôlés. Exclure de ce dernier `SHA256.json` lui-même et `SHA256SUMS-SOURCES.txt`, ainsi que `Resultats`, `Livraison`, `Distribution` et les dossiers de contrôle de version. Écrire enfin une seule ligne dans `SHA256SUMS-SOURCES.txt` : l’empreinte SHA256 du JSON, deux espaces et `SHA256.json`. Cette exclusion des deux fichiers de contrôle évite une boucle d’empreintes. Les fichiers historiques hérités de main sont conservés dans l’arbre Git et présents dans le ZIP complet ; leur présence ne signifie pas que leurs anciennes recettes sont exécutées par cette compilation.

## Provenance du packaging

- Dépôt historique : `JulienLecomteGit/haloryva-studio`.
- Commit : `bfd0fc69a4224d0b35a1380623bef72998ea44bf`.
- Workflow réussi : [43115017982](https://github.com/JulienLecomteGit/haloryva-studio/actions/runs/43115017982).
- Artefact : `13898131858`, archive SHA256 `7F6555577357272E43666584F4767F0017617E97493D6B3057B3E8FF5298FC1F`.
- Les fichiers du moteur sont issus d’exp4. La compilation utilise la sélection WinRT corrigée du vérificateur R4, pas le mélange historique UnionMetadata/contrats du premier script de fabrication 2.7.1.
