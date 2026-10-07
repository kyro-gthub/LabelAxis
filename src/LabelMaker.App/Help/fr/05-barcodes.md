---
id: barcodes
title: Codes-barres et codes QR
category: Designing
order: 3
keywords: barcode, qr, qr code, ean, upc, code 128, code 39, gs1, datamatrix, pdf417, aztec, itf, isbn, human readable, bulk barcode, gs1 builder, udi, medical device, expiry, batch, sscc, gtin, check digit, verify, verifier, grade, scan quality, camera, webcam, code-barres, codes en série, générateur GS1, dispositif médical, péremption, lot, clé de contrôle, vérifier, note, caméra
---
# Codes-barres et codes QR

`Code-barres` et `QR Code` dans la barre d'outils en ajoutent un à l'étiquette. Ses réglages sont dans `Propriétés › Paramètres du code-barres`.

## Types

| Type | Usage |
|---|---|
| EAN-13, EAN-8, UPC-A, UPC-E, ISBN-13 | Produits de vente au détail |
| Code 128 (A, B, C), GS1-128 | Expédition, stock, numéros de série |
| Code 39, Code 93, Codabar, Interleaved 2 of 5, ITF-14, MSI Plessey | Systèmes plus anciens et industriels |
| QR Code, Data Matrix, PDF417, Aztec | Liens web, texte long, petites pièces |

## Réglages

- **Données :** ce que contient le code-barres. Il peut s'agir d'un champ comme `{Barcode}` pour que chaque étiquette ait le sien.
- **Texte lisible :** les chiffres imprimés sous les barres, avec leur police et leur taille.
- **Couleurs :** gardez des barres sombres sur un fond clair ; les lecteurs ont besoin du contraste.

Si les données ne conviennent pas au type (par exemple des lettres dans un EAN-13), le code-barres est dessiné comme un contour rouge et le panneau explique pourquoi.

> Astuce : ne réduisez pas un code-barres sous la taille dont son type a besoin ; les lecteurs lisent mal les barres trop fines. Lancez `Imprimer › Contrôle avant impression…` avant une grosse tâche pour repérer les problèmes.

## Beaucoup de codes-barres d'un coup

`Données › Générateur de codes-barres en série…` enregistre dans un dossier un fichier image de code-barres pour chaque valeur (avec préfixe, suffixe et remplissage). `Données › Image de code-barres…` enregistre un code-barres en image PNG (150 à 1200 dpi), en dessin SVG ou en PDF.

## Codes-barres GS1 : alimentaire, médicaments, dispositifs médicaux, expédition

`Données › Générateur de codes-barres GS1…` (ou le bouton dans les paramètres du code-barres) crée des codes-barres GS1 dans les règles. Choisissez l'usage de l'étiquette, par exemple un aliment avec date de péremption et lot, un dispositif médical (UDI), une boîte de médicament, un carton ou une palette (SSCC), puis remplissez chaque ligne :

- Les longueurs incorrectes, les clés de contrôle, les dates qui n'existent pas et les données qui en exigent d'autres sont signalées en clair pendant la saisie. `Ajouter une clé de contrôle` la calcule pour vous.
- Les dates peuvent être saisies comme 31/12/2026 ; elles deviennent AAMMJJ.
- Choisissez **GS1-128** (barres, pour cartons et aliments), **GS1 DataMatrix** (petit carré, pour médicaments et dispositifs médicaux) ou **GS1 QR Code**. Pour un code carré, les données peuvent aussi être écrites en texte à côté, comme l'exigent les dispositifs médicaux.
- Une valeur peut être un champ de données comme `{Batch}` : il est rempli pour chaque étiquette à l'impression.

## Vérifier un code-barres imprimé

`Données › Vérifier un code-barres imprimé…` note un code-barres imprimé de A (le meilleur) à F, comme le fait un vérificateur. Démarrez la caméra et tenez l'étiquette devant elle, ou ouvrez une photo ou un scan. Vous voyez :

- ce que dit le code-barres, et s'il est identique à un code-barres de la page que vous concevez ;
- chaque mesure (contraste, modulation, défauts, décodabilité, zones de silence, largeur des barres) avec sa note ;
- quoi changer, par exemple baisser la densité de l'imprimante quand les barres s'impriment trop larges.

C'est une indication fiable, pas un certificat : la lumière et la mise au point changent le résultat, alors vérifiez plusieurs fois.
