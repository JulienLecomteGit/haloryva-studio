# Historique documentaire

## 2.6.6 — 8 novembre 2026

Version de référence utilisée et appréciée après mise à jour sur le poste de l'auteur.
La publication d'une release est encore à préparer.

### Moteur de traînée

- Conservation des points jusqu'à la fin de leur durée de vie, avec émission bornée.
- Cadrage incluant le tracé encore visible, sans rupture au seuil antérieur.
- Réutilisation des surfaces graphiques et réduction après le repos.
- Cadencement avec au maximum un rendu en attente.
- Réutilisation d'objets de dessin et simplification limitée des segments.

### Installation et continuité

- Setup graphique 2.6.6 contenant le binaire B déjà compilé et testé ; pas de recompilation
  de l'application pour préparer ce setup.
- Conservation des préférences installées ; aucun import implicite du profil d'essai B.
- Mise à jour, lancement, réglages, fluidité et inscription Windows déclarés réussis.
- Sources et setup 2.6.5 conservés séparément comme historique de retour.

### Limites

Pas de nouvelle signature, pas de validation exhaustive sur d'autres PC, pas de test de
redémarrage dans cette recette. La désinstallation 2.6.6 n'a pas été rejouée ; le cycle
Windows déclaré réussi auparavant concernait la 2.6.5.

Détail : [validation et preuves disponibles](docs/VALIDATION_2.6.6.md).
