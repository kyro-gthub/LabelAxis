---
id: export
title: Exporter et vérifier une tâche
category: Exporting
order: 1
keywords: export, pdf, png, svg, picture, file, preflight, check, publisher, import, exporter, image, fichier, contrôle avant impression, vérifier, importer, publisher margins, label sheet, marges publisher, planche d'étiquettes
---
# Exporter et vérifier une tâche

## Enregistrer comme fichier

- `Imprimer › Exporter › Feuille en PDF…` enregistre la tâche en PDF avec les mêmes réglages que l'impression (feuilles, position de départ, enregistrements). Envoyez-la à un imprimeur, ou gardez-la comme épreuve.
- `Étiquette actuelle en PNG…` enregistre l'étiquette affichée comme image ; `Étiquette actuelle en SVG…` comme dessin qu'ouvrent les logiciels de mise en page.

## Vérifier avant d'imprimer

`Imprimer › Contrôle avant impression…` cherche les problèmes avant une tâche coûteuse : images ou polices manquantes, codes-barres aux données incorrectes ou trop grands pour l'étiquette, champs vides pour un enregistrement, codes-barres en double, et étiquettes qui dépassent la zone imprimable de l'imprimante.

## Importer des fichiers Microsoft Publisher

`Fichier › Ouvrir un fichier Publisher (toutes les pages)…` transforme un fichier Publisher (.pub) en projet ; `Importer le texte et les images de Publisher…` apporte son contenu dans le projet ouvert.

Un fichier d'étiquettes Publisher a généralement une étiquette par page et imprime plusieurs pages par feuille. LabelAxis place ces pages sur une vraie planche d'étiquettes : celle que Publisher nomme, ou la planche intégrée aux étiquettes de cette taille, avec les marges et espacements de Publisher, pour que les étiquettes s'impriment là où Publisher les imprimait. La barre d'état indique la planche choisie ; changez-la dans le **Gestionnaire d'étiquettes** si vous en utilisez une autre. Un projet importé de Publisher avant cela (une étiquette seule sur chaque petite page) se voit proposer sa planche à l'ouverture ; **Annuler** garde une étiquette par page, et la question n'est plus posée pour ce fichier.
