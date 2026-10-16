---
id: thermal
title: Imprimantes d'étiquettes thermiques
category: Printing
order: 4
keywords: thermal, zebra, tsc, xprinter, zpl, tspl, roll, bluetooth, serial, com port, network, wifi, darkness, speed, gap, black mark, tear off, peel, cut, thermique, rouleau, port série, réseau, densité, vitesse, écart, repère noir, déchirer, décoller, couper
---
# Imprimantes d'étiquettes thermiques

Les imprimantes thermiques (Zebra, TSC, Xprinter et autres) impriment des étiquettes depuis un rouleau. Commencez par `Fichier › Nouvelle étiquette thermique…` pour régler la taille de l'étiquette, combien d'étiquettes tiennent en largeur sur le rouleau et l'écart entre elles. `Imprimer` ouvre ensuite la fenêtre d'impression thermique (aussi `Imprimer › Impression thermique (ZPL / TSPL)…`).

## Imprimante

- **Langage d'imprimante :** ZPL pour Zebra et les imprimantes « compatibles ZPL » ; TSPL pour TSC, Xprinter et beaucoup d'imprimantes de bureau.
- **Envoyer vers :**
  - **Imprimante Windows :** une imprimante installée dans Windows.
  - **Réseau (adresse IP) :** une imprimante sur le réseau ou en Wi-Fi. `Rechercher des imprimantes…` parcourt votre réseau ; `Tester` vérifie que l'adresse répond.
  - **Bluetooth ou port série (COM) :** voir ci-dessous.
  - **Fichier :** enregistre les commandes d'imprimante dans un fichier .zpl / .prn.
- **Résolution :** 203, 300 ou 600 dpi, comme sur l'étiquette de l'imprimante.

## Imprimantes Bluetooth

1. Allumez l'imprimante et appairez-la dans **Paramètres Windows › Bluetooth et appareils**.
2. Windows attribue un port COM à une imprimante appairée. Dans la fenêtre d'impression thermique, choisissez **Bluetooth ou port série (COM)** et prenez le port marqué **Bluetooth** (`↻ Chercher à nouveau` actualise la liste après l'appairage).
3. Appuyez sur `Tester` ; quand le port s'ouvre, l'imprimante est prête.

> Remarque : certaines petites imprimantes d'étiquettes pilotées par une application de téléphone utilisent leur propre système Bluetooth au lieu de ZPL ou TSPL ; elles ne peuvent pas être utilisées depuis un programme sur PC.

## Support et qualité

- **Détection de l'étiquette :** écart entre étiquettes, repère noir ou rouleau continu.
- **Après l'impression :** déchirer, décoller ou couper.
- **Décaler l'impression :** déplace un peu l'impression vers la droite/le bas pour corriger une imprimante ; **Imprimer à l'envers** pour les rouleaux qui sortent dans l'autre sens.
- **Densité** et **vitesse :** plus foncé et plus lent pour des codes-barres nets.
- **Les gris deviennent :** net pour le texte et les codes-barres, ou nuancé pour les photos.

L'aperçu à droite montre exactement les points que l'imprimante recevra.

## Image ou polices de l'imprimante

**Envoyer les étiquettes** (sous Langage d'imprimante) choisit comment chaque étiquette arrive à l'imprimante :

- **En image (exactement comme à l'écran) :** chaque étiquette part sous forme d'une seule image de points, exactement comme l'aperçu. Toutes les polices, le texte arabe, les photos et les dégradés s'impriment comme prévu. Le meilleur choix au quotidien.
- **Polices et codes-barres de l'imprimante (le plus rapide) :** pour les longues séries. Les parties qui ne changent jamais (cadres, logos, texte fixe) sont enregistrées une seule fois dans l'imprimante au début de la tâche ; chaque étiquette n'envoie ensuite que son texte et ses codes-barres variables, que l'imprimante dessine elle-même.
  - Les codes-barres connus de l'imprimante (Code 128, GS1-128, EAN-13, EAN-8, UPC-A, Code 39, Code 93, Interleaved 2 of 5, Codabar, QR et Data Matrix) sont dessinés par l'imprimante, avec les mêmes barres que LabelAxis.
  - Le texte variable utilise la police de l'imprimante : il est donc un peu différent de l'écran. Les lignes se coupent au même endroit qu'à l'écran.
  - Ce que l'imprimante ne sait pas dessiner (texte arabe et autres écritures de droite à gauche, lettres hors de l'Europe de l'Ouest, texte clair sur fond sombre, texte incliné, tableaux variables) part sous forme d'une petite image de cette zone seulement.
  - La fenêtre indique combien d'éléments l'imprimante dessine elle-même. L'aperçu montre l'étiquette telle que conçue.

> Astuce : imprimez d'abord quelques étiquettes. Si le texte dans la police de l'imprimante dépasse de sa zone, élargissez la zone ou réduisez le texte.
