HALORYVA — FABRICATION FINALE DE L'INSTALLATEUR 2.7.1
Périmètre : compilation du setup uniquement, métadonnées et extraction.
Aucune installation, aucun lancement ni recompilation de Haloryva.

APPLICATION FIGEE
Haloryva.exe 2.7.1 / FileVersion 2.7.1.11 / AssemblyVersion 2.7.1.0
Run 43086180391, commit ca14eca6be1e0a19ef9004ba0de2499e00c7c624
Archive Haloryva-2.7.1-file11-history-build, ID 13886896454.
SHA-256 : 1CB026DDBA084A648832FBD91118A67000373E076DAFA72BE2957B47223A6F81
Brand.cs estampillé du même artefact :
6D0083AAE493A420627ED183A0BACC0FA34AB81C7F1A39A6E2FA1ECC8E7E775A
Date affichée provenant de la fabrication de l'application : 11/11/2026.
L'instant propre de fabrication du setup est enregistré séparément.
Le binaire, l'archive et ses sources estampillées ne sont pas modifiés.

RECETTE REUTILISEE
Base : preparation-exe-preview9-20261111/Build-InstallerOnly.prepared.ps1,
déjà exécutée avec succès pour la fabrication preview.9 (run 43060226353).
Les cinq fichiers de l'installateur existant sont copiés sans modification dans
un dossier temporaire du runner. Les sept entrées du dépôt sont verrouillées par objet Git.
Brand.cs et l'icône proviennent de l'archive validée, vérifiés avant utilisation.
Dans la seule copie temporaire de Brand.cs, L.T(" · build ") devient le texte français
" · build " : même adaptation que la recette éprouvée pour l'assistant français autonome.
L'adaptation DisplayVersion propre aux préversions n'est plus nécessaire :
Brand.Version, les métadonnées et le nom du setup sont directement 2.7.1.
Le manifeste temporaire du setup passe de 2.7.0.0 à 2.7.1.11 et conserve asInvoker.
Seules les cinq sources du setup et cette copie de Brand.cs sont compilées.
Aucune source du moteur Haloryva n'est compilée.

SORTIE
HaloryvaSetup_2.7.1.exe, non signé.
Une archive courte contient cet EXE, ses empreintes et sa provenance.
Une seconde archive conserve les preuves, les sources temporaires de l'installateur
et le contenu incorporé avant/après extraction.
Ces archives de travail ne constituent ni une release GitHub ni une publication Store.

CONTROLES AUTORISES
- Identité et empreinte du candidat figé, Brand.cs estampillé et icône d'origine.
- Métadonnées du setup : produit, éditeur, version 2.7.1, fichier 2.7.1.11,
  assembly 2.7.1.0, architecture x64, version/date de Brand, absence de signature,
  présence des trois ressources attendues.
- Le seul lancement est le setup avec --extract dans un dossier temporaire neuf.
  Ce chemin quitte SetupProgram avant le GUI, la relocalisation, l'installation
  et les branches --selftest.
- Code de sortie 0, exactement trois fichiers, empreintes avant/après identiques :
  Haloryva.exe, Haloryva.sha256 et haloryva_demo.png.
- Binaire Haloryva, source estampillée et setup toujours identiques après extraction.

NON EXECUTE
Application, autotests Haloryva/setup, 195 contrôles, Sonidos, anciennes recettes,
installation/désinstallation, mise à jour réelle, MSIX, signature, release, Store.
Aucun logiciel ajouté, abonnement ou modification du PC principal.
Les scripts refusent toute exécution hors runner GitHub hébergé.
Les binaires et branches antérieurs ne sont ni remplacés ni modifiés.

ETAPE SUIVANTE DISTINCTE
La fabrication/extraction ne valide pas le cycle d'installation du nouveau setup.
Une éventuelle recette isolée d'installation/désinstallation devra être autorisée.
Le mode normal de l'installateur lance automatiquement Haloryva --selftest ;
ce contrôle est exclu de la présente fabrication et devra être explicitement
inclus dans l'autorisation d'une éventuelle recette d'installation.
