Haloryva 2.7.1-preview.9 — correction de l'actualisation du démarrage uniquement.
Sources basées sur preview.8 finale CFB5322F128EBAA7D1BBC6A4F8892CC853C9B6B9ED7F78DBDC4C2FB3B0CDA512.
Les notifications Activated et WinEvent n'ont pas déclenché la relecture dans les essais initiaux.
Le candidat courant échantillonne le premier plan toutes les 100 ms si le Studio empaqueté
est visible. Les transitions déclenchent des lectures bornées ; l'état StartupTask
n'est pas interrogé continuellement. Une lecture demandée pendant une autre est conservée.
Aucune modification Windows ni du profil depuis une simple lecture.
Diagnostic --startup-refresh-trace optatif ; absent en lancement normal.
Les autres sources, langues, assets et comportements restent hérités de preview.8.
Compilation et seul scénario S3 ciblé sur GitHub. Aucune suite acquise rejouée.
Aucune exécution locale, signature, release ou soumission Store.
