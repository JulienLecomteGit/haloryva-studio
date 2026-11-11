HALORYVA 2.7.1 — CANDIDAT HISTORIQUE, FICHIER 2.7.1.11

Périmètre autorisé : corriger uniquement la hauteur de l'ancien historique.
Nouvelle copie livraison-2.7.1-historique, créée à partir de livraison-2.7.1-preparation.
Base du dépôt : 25f6f03216c4f89988bf82521edc0938c458e35f.
Le candidat précédent (fichier 2.7.1.10) et toutes les préversions restent intacts.

UNE CORRECTION DE COMPORTEMENT
PremiumStudioForm.cs / AboutBlock :
Avant : hauteur fixe PremiumPalette.S(286).
Après : PremiumPalette.S(128) + (Brand.History.Length + 1) * PremiumPalette.S(24) + PremiumPalette.S(14).
Le calcul reprend la position de l'en-tête, le pas réel des lignes et une marge finale.
Avec les 13 lignes conservées et une échelle de 100 %, cela donne 478 pixels.
La dernière ligne finit à 460 pixels ; la marge finale est de 18 pixels.
Aucun texte, dessin, raccourci, réglage ou comportement MSIX n'est changé.

IDENTITE
Version affichée et informative : 2.7.1.
Version de fichier distincte : 2.7.1.11 ; assembly : 2.7.1.0.
Date réelle Europe/Paris renseignée dans la copie temporaire de compilation.
Le catalogue de traduction et les 12 anciennes entrées d'historique sont inchangés.
34 des 36 entrées applicatives réutilisent exactement les objets Git antérieurs.
Les deux copies modifiées sont PremiumStudioForm.cs (une ligne) et Brand.cs (révision de fichier).

RECETTE STRICTEMENT LIMITEE
Une compilation de Haloryva sur GitHub Windows ; métadonnées du nouvel EXE.
Même binaire par SHA-256 dans les trois langues.
Deux captures À propos par langue à 1024×768 :
- haut : informations de version et totalité des 13 lignes ;
- bas : dernière ligne et contrôles sous l'historique, après défilement.
Le pilote vérifie les rectangles réellement affichés avant la revue visuelle des images.
Ces deux vues servent à vérifier l'effet de la nouvelle hauteur sur le défilement.
Pas de clic sur Copier, pas de changement de langue par l'interface ; profils neufs FR/EN/ES.
L'interface réelle du binaire est chargée via CursorController ; Program.Main n'est pas qualifié.
Le Windows GitHub est Windows Server 2025 ; pas une validation de session Windows 11.

INTERDIT / NON LANCE
Sonidos, 195 contrôles, autotests Haloryva, anciennes recettes, MSIX,
installateur, signature, publication, abonnement et intervention sur le PC.
Les scripts refusent l'exécution hors runner GitHub hébergé.
Les captures restent dans les archives de recette ; aucune image copiée dans le dépôt public.
