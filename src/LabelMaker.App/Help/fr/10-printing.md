---
id: printing
title: Impression
category: Printing
order: 1
keywords: print, printer, sheet, copies, preview, pages, current page, selected pages, copies per page, paper, margins, start position, skip, history, reprint, outlines, crop marks, automatic printing, watch folder, hot folder, web link, api, integration, other programs, json, csv, imprimer, imprimante, feuille, exemplaires, aperçu, page actuelle, marges, position de départ, historique, réimprimer, contours, traits de coupe, dossier surveillé, lien web, autres programmes
---
# Impression

`Ctrl+P` (ou `Imprimer` en haut à droite) ouvre la fenêtre Imprimer. L'image à droite montre la première feuille exactement comme elle s'imprimera ; `Aperçu…` montre toutes les feuilles.

## Quoi imprimer

- **Remplir la feuille avec l'étiquette modèle :** la même étiquette dans chaque emplacement.
- **Une étiquette par enregistrement de données :** une étiquette différente pour chaque ligne de vos données (voir [[data|Données]]).
- **Pages :** plusieurs pages différentes sur une feuille, l'une après l'autre :
  - **Toutes les pages**, **Page actuelle** (celle que vous regardiez), ou **Pages sélectionnées** (cochez-les dans la liste).
  - Chaque page a sa propre case d'**exemplaires** : la page 1 peut s'imprimer 6 fois et la page 2 douze fois sur les mêmes feuilles. **Exemplaires de chaque page** les règle toutes d'un coup. Les nombres sont enregistrés avec le projet.

> Astuce : appuyer sur Imprimer quand une page (autre que l'étiquette modèle) est affichée ouvre la fenêtre Imprimer déjà réglée sur cette page.

## Position de départ et plages

`Position de départ et plage de pages…` (aussi `Imprimer › Paramètres d'impression avancés…`) commence à un emplacement plus loin sur une feuille déjà entamée, saute des emplacements et choisit quels enregistrements ou pages imprimer. **Feuilles … à …** réimprime seulement certaines feuilles, par exemple une qui s'est coincée.

## Papier et marges

Normalement, les étiquettes vont là où le modèle de feuille l'indique. Choisissez un autre **format de papier**, **paysage**, ou **utiliser mes propres marges et écarts** quand votre feuille diffère du modèle.

## Exemplaires, échelle et contours

- **Exemplaires** imprime toute la tâche plusieurs fois.
- **Échelle** reste à 100 % pour que les étiquettes sortent à la bonne taille ; ne la changez que pour corriger une imprimante, de préférence avec le [[calibration|calibrage]].
- Dans `Avancé…`, les **contours des étiquettes** et les **traits de coupe** tracent les bords de chaque étiquette, pratique sur du papier ordinaire.

## Imprimantes

La liste contient les imprimantes installées dans Windows et les [[wifi-printers|imprimantes Wi-Fi]] que vous avez ajoutées. `Boîte de dialogue Windows…` ouvre les réglages propres de l'imprimante (recto verso, bac). Les imprimantes d'étiquettes thermiques ont leur propre fenêtre ; voir [[thermal|Imprimantes d'étiquettes thermiques]].

## Historique

`Historique…` (ou `Imprimer › Historique d'impression…`) liste les tâches précédentes ; réimprimez-en une, ou terminez une tâche arrêtée en cours de route.

## Demandes d'impression d'autres programmes

Une caisse, une boutique en ligne ou un tableur peut imprimer des étiquettes pendant que LabelAxis est ouvert : `Imprimer › Demandes d'impression d'autres programmes…`.

- **Dossier surveillé :** les fichiers qui y sont déposés sont imprimés : une demande `.json`, ou une liste `.csv` dont la première ligne nomme les champs (imprimée avec le modèle choisi là). Les fichiers imprimés passent dans `Done` ; les autres passent dans `Failed` avec une note expliquant pourquoi.
- **Lien web :** les programmes de ce PC envoient la même demande à `http://localhost:8765/print`. D'autres ordinateurs peuvent aussi être autorisés ; ils doivent envoyer la clé d'accès. Les pages web d'un navigateur sont refusées.
- Une demande nomme l'un de vos modèles (voir [[getting-started|Vos propres modèles]]), l'imprimante, les exemplaires et un enregistrement par étiquette. Avec `"output": "pdf"`, elle crée un PDF au lieu d'imprimer.

Chaque demande apparaît dans la liste de cette fenêtre, et dans l'historique d'impression.
