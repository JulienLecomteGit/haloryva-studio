# Historique — Haloryva Studio

## 2.7.2 — certification et mise à jour Store contrôlées — 18 novembre 2026

**Fabrication finale réussie, certification Microsoft acquise et mise à jour depuis la 2.7.1 contrôlée sur le PC testé.** Le paquet Store 2.7.2.0 x64 contient le binaire 2.7.2.6 livré ; les 93 valeurs du profil et le fichier de réglages sont restés identiques. La fin de publication du portail, la disponibilité générale et la relance visuelle finale restent à confirmer. La [fiche de validation](docs/VALIDATION-2.7.2.md) distingue livraison, qualification antérieure et installation.

- Traînée Ligne / Lumière continue, courbes adaptatives et extinction progressive ; réglages accompagnés d’explications et aperçu partagé.
- Échantillons d’entrée horodatés et conservation de leur ordre dans le pipeline de traînée.
- Composition du ruban limitée aux zones utiles et copie bitmap regroupée, sans nouvelle modification du moteur lors de la promotion finale.
- Récupération conditionnelle de l’ordre du calque des effets, avec journal technique local borné.
- Écriture des journaux d’effets et de récupération en arrière-plan ; diagnostics détaillés facultatifs, sans nouvel envoi réseau.
- Identité finale 2.7.2 / fichier 2.7.2.6 ; démarrage normal rétabli après le garde-fou propre à la copie expérimentale.
- Base exp4 compilée et testée sur Windows ; recette visuelle positive sur les cinq observations demandées, sans garantie d’absence définitive d’incidents rares.
- Qualification sur le commit `64d97384dfa2c5bfd2618c43b348dff20a863be2` : fabrication Windows réussie, 52 contrôles de structure du paquet réussis et archive de sources de 125 fichiers vérifiée. Le premier passage avait réussi compilation/tests puis échoué à la découverte de Git ; ce point de fabrication a été corrigé.
- Livraison finale : commit `32234c0410e080add61742b29ee794b5a566fa2e`, run `44300357140` réussi ; huit suites natives, 52 contrôles MSIX et 125 fichiers sources vérifiés. Les artefacts et empreintes de ce run restent la référence du paquet installé.
- Documentation FR/EN/ES actualisée ; captures 2.7.1 conservées et explicitement identifiées comme historiques.
- Microsoft Store reste le canal principal. Aucun installateur EXE 2.7.2 public n’est proposé ; les mises à jour documentaires ne remplacent pas le paquet livré.

## 2.7.1 — Microsoft Store, fiche publique vérifiée le 14 novembre 2026

- Interface français, anglais et espagnol avec langue mémorisée.
- Sélection mémorisée des outils parcourus par les boutons de souris.
- Améliorations des clics courts, du traitement des entrées et du placement initial du Studio ; outil, couleur et épaisseur fixés au début du trait.
- Ajustements des textes, indicateurs et historique.
- Canal Store certifié, reprise consentie des anciens réglages et gestion du démarrage à la connexion.
- Essais utilisateur réussis de reprise des réglages, langues, démarrage automatique et reconnexion avec préférences conservées.
- Fiche publique Microsoft Store accessible en France le 14 novembre 2026 ; téléchargement principal gratuit. Aucun EXE 2.7.1 proposé ici.
- La publication documentaire initiale conservait les sources historiques 2.7.0. Les 36 fichiers applicatifs estampillés 2.7.1.11 ont ensuite été intégrés sur la branche principale ; l’installateur reste historique. Voir [la provenance et les limites](docs/SOURCES-2.7.1.11.md).

## 2.7.0 — 10 novembre 2026 — version stable déclarée par l'auteur

- Identité de l'application et de l'installateur simplifiée : **Haloryva Studio 2.7.0** et `HaloryvaSetup_2.7.0.exe`. Les mentions « candidat » et « C4 » sont retirées des libellés d'identité actuels ; les archives techniques conservent leurs noms historiques.
- Conservation du moteur de la préversion C4 : effets de halo, traînées, dessin, profils et routage des boutons X1/X2.
- Fabrication sur Windows PowerShell 5.1 : **8 étapes de compilation/autotest/extraction réussies**, **108 assertions de protocole** et **63 contrôles de traînée**.
- Empreinte SHA-256 de l'installateur vérifiée à nouveau sur sa copie Google Drive : `A60215938B82EFBAD1B744E4E85911256426103F4D18E7F1B2213E9C4BC6178A`.
- Travaux restant distincts de cette validation : désinstallation/réinstallation de la 2.7.0, essai longue durée, ensemble des styles de traînée, second PC et signature du binaire.

## 2.7.0 C4 — préversion du 10 novembre 2026 (archive)

- Build de travail avec les réglages de fluidité et l'orientation X1/X2.
- Interface ouverte et essais physiques ciblés validés par Julien ; la fiche de préversion C4 a été retirée de GitHub avant l’ouverture publique, tandis que le tag historique `v2.7.0-c4` reste conservé.
- Binaire et script C4 d'origine classés en archive récupérable, sans suppression.

## 2.6.6 — CursorFlow Studio (secours historique)

- Installateur de retour arrière conservé sur le Drive du projet, distinct de l'installateur stable Haloryva.

