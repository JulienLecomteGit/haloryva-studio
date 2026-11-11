HALORYVA preview.7 — essai MSIX ciblé, aucun test acquis relancé

Binaire figé : B16B6AC65FA12F30DB35109190E65B8A0ADF86C2A8E5C2863770BEE28A18F604
Compilation GitHub : 42957075701, commit 466c8addab0de12de47d6a22c53773152b91d323.
Profils natifs FR/EN/ES : run 43001652792 ; leurs empreintes sont figées dans le pilote.
Aucune recompilation d'Haloryva, aucun changement des sources ni des préversions.

Windows 11 ARM64 hébergé par GitHub, exécutable x64 via émulation.
Paquet x64 avec identité de test Preview7UnsignedTest, version 2.7.1.7.
MakeAppx déjà présent sur le runner. Aucun certificat, aucune signature,
aucun changement des protections Windows ou du mode développeur.
Méthode officielle d'essai non signé :
https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package

Sept lancements par AppsFolder :
- premier lancement neuf, langue française et dix outils par défaut ;
- pour chaque langue FR/EN/ES : profil natif validé, puis redémarrage sans
  recopier les réglages entre les deux lancements.
Chaque lancement confirme l'identité de paquet du processus, la langue
réellement visible et la disponibilité de l'arrêt normal de l'application.
Arrêt par le mécanisme propre à Haloryva, code retour zéro exigé.
Tous les champs et octets du profil sont comparés après chaque arrêt ;
Stylo et Rectangle doivent rester les deux outils sélectionnés.
Le binaire est contrôlé avant empaquetage, dans le MSIX, après installation
sur le runner et après chaque lancement. Le paquet est retiré à la fin.

Aucun geste clavier/souris rejoué, aucune suite historique, aucun des
195 contrôles. Garde interdisant le PC principal et les runners auto-hébergés.
Aucune release, aucun tag, aucune fusion dans main, aucune soumission Store.
Cet essai ne valide ni signature de production ni certification Microsoft Store.
