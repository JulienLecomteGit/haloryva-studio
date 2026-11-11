# Candidat Haloryva 2.7.1 — fabrication et captures limitées

Autorisation : compilation sur GitHub, métadonnées et quatre captures F01-F03 seulement.

- Copie de livraison figée : `9482b046fa6fc096fd5942482c3ddff8516dcbcc`, 36 entrées vérifiées.
- Date de fabrication réelle du runner, convertie en Europe/Paris, inscrite seulement dans une copie temporaire de Brand.cs ; copie préparée inchangée.
- Une compilation de Haloryva ; trois pilotes FR/EN/ES réutilisent exactement cet exécutable par SHA-256.
- Métadonnées attendues : produit 2.7.1, fichier 2.7.1.10, assembly 2.7.1.0, sans signature.
- Quatre captures du véritable écran 1024×768 : Sonidos ES, version et première ligne d’historique FR/EN/ES. Mesures des zones réellement peintes et conservation des PNG pour revue humaine.
- Le pilote charge l’interface compilée via CursorController ; ce n’est ni une installation ni un lancement par Program.Main. Profils synthétiques neufs et navigation native limitée aux deux pages utiles.
- Windows GitHub hébergé `windows-2025` (Windows Server, pas une qualification de connexion réelle Windows 11).
- Aucun autotest, ancien scénario, lot des 195 contrôles, installateur, MSIX, signature, release ou abonnement.
- Aucun fichier existant ni branche antérieure modifié. Pas d’intervention sur le PC utilisateur.

Les scripts refusent toute exécution hors runner GitHub hébergé. Ne pas les lancer localement.
