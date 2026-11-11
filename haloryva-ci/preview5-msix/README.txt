HALORYVA PREVIEW.5 — RECETTE MSIX UNIQUEMENT SUR GITHUB
Base conservée : haloryva-tests-preview5, 67b7efebe4efcfc24168d6f92bd0e1f2277aa58f.
Branche séparée : haloryva-tests-preview5-msix.

Réutilise l'exécutable déjà compilé dans le run 42944347258 :
2.7.1-preview.5
SHA256 E259BC782D8ED909912A397F83F508AC254AC6483DBF0787B7917A6C68F90B40.
Pas de compilation, modification ou signature du binaire.

Le pilote reprend celui de preview.3, conservé intégralement :
haloryva-ci/preview3-validation/Run-Msix.ps1.
Il exige un runner GitHub hébergé et Windows 11 ; refuse toute exécution locale.

Scénario :
- vérifier l'origine du binaire et du profil enregistré lors des clics UI preview.5 ;
- copier la structure MSIX existante dans le dossier temporaire du runner ;
- identité distincte HaloryvaStudio.Preview5UnsignedTest, version du paquet 2.7.1.5 ;
- mécanisme de test non signé documenté par Microsoft (pas de certificat,
  changement de protection, mode développeur ou contournement de blocage) :
  https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package
- MakeAppx valide la structure ; contrôle du binaire contenu et de l'absence de signature ;
- installation uniquement sur la machine éphémère GitHub ;
- trois activations réelles via AppsFolder, identité du processus vérifiée,
  attente bornée de disponibilité du signal d'arrêt et trois sorties normales ;
- premier lancement : profil MSIX neuf avec dix outils par défaut ;
- application arrêtée : copie exacte du JSON produit par la recette UI réussie,
  sélection Stylo/Rectangle/Auto-formes et affectations des boutons ;
- deux relancements : lecture puis sauvegarde par l'application ; sélection,
  affectations et style conservés ; contenu complet du JSON inchangé ;
- le troisième lancement réutilise directement le fichier produit par le deuxième,
  sans modification de contenu par le pilote ;
- désinstallation et vérification de l'absence d'enregistrement pour l'utilisateur d'essai.

Il ne s'agit pas d'une nouvelle recette de gestes : aucun clic ni raccourci injecté.
L'environnement Windows 11 ARM exécute le binaire x64 émulé.
Le test ne valide pas un Windows 11 x64 physique, une mise à jour depuis la stable,
une identité Store finale ni une signature de production.
Les préversions, main et le PC utilisateur sont préservés.
Aucune invocation des 195 contrôles, aucune release, aucun envoi Store.

Lire le bilan à la racine après exécution : les étapes ci-dessus sont des
préparatifs tant que les résultats du run n'ont pas été inspectés.
