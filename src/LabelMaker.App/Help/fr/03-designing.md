---
id: designing
title: Déplacer, dimensionner et aligner les objets
category: Designing
order: 1
keywords: select, move, resize, rotate, skew, free transform, align, distribute, group, lock, layers, front, back, guides, grid, snap, rulers, zoom, undo, sélectionner, déplacer, redimensionner, pivoter, incliner, aligner, répartir, grouper, verrouiller, calques, repères, grille, magnétisme, règles, annuler
---
# Déplacer, dimensionner et aligner les objets

## Sélectionner

- Cliquez sur un objet pour le sélectionner. `Maj`+clic ajoute des objets à la sélection ou les en retire.
- Faites glisser sur une zone vide pour sélectionner tout ce que le cadre touche.
- `Ctrl+A` sélectionne tout ce qui est sur l'étiquette.
- Les objets d'un groupe sont pris ensemble ; `Ctrl`+clic atteint un objet à l'intérieur d'un groupe.

## Déplacer et redimensionner

- Faites glisser un objet pour le déplacer. Les flèches du clavier le décalent d'un pas de grille ; avec `Maj`, de dix pas.
- Faites glisser une poignée pour redimensionner. `Maj` garde les proportions ; `Ctrl` redimensionne depuis le centre.
- Les positions et tailles exactes sont dans `Propriétés › Position et taille`.

## Faire pivoter et incliner

- Faites glisser le bouton rond au-dessus d'un objet sélectionné pour le faire pivoter ; `Maj` l'arrête tous les 15°. Faire glisser une poignée avec `Alt` fait aussi pivoter.
- `Édition › Faire pivoter de 90° à droite / à gauche` tourne d'un quart de tour ; `Édition › Retourner horizontalement / verticalement` fait un miroir.
- `Transformation libre` (`Ctrl+Maj+T`) change les poignées latérales en poignées d'inclinaison.

## Aligner

- `Aligner` dans la barre d'outils aligne les objets sélectionnés par leur bord gauche, centre, droit, haut, milieu ou bas, les répartit régulièrement ou leur donne la même largeur ou hauteur.
- **Magnétisme :** pendant le déplacement, les objets s'accrochent à la grille, aux repères et aux autres objets. Activez ou désactivez chacun dans la barre au-dessus de l'étiquette (`Aligner sur : Grille, Repères, Objets`) ou dans le menu `Échelle`.
- **Repères :** faites glisser depuis une règle sur l'étiquette pour placer un repère. Ramenez-le sur la règle pour le supprimer. `Identique sur chaque page` partage les repères entre les pages ; `Verrouiller` les empêche de bouger.
- **Grille :** affiche une grille ; `−` et `+` changent son pas.

## Avant et arrière

Les objets plus bas dans la liste `Calques` (à droite) sont dessinés derrière ceux du dessus. Faites glisser les lignes pour changer l'ordre, ou utilisez `Ctrl+B` (premier plan), `Ctrl+Maj+B` (arrière-plan) et `Ctrl+↑` / `Ctrl+↓` dans la liste.

## Grouper et verrouiller

- `Ctrl+G` groupe la sélection pour qu'elle se déplace et se redimensionne d'un bloc ; `Ctrl+Maj+G` dissocie.
- `Ctrl+L` verrouille un objet pour qu'il ne soit pas déplacé par erreur.

## Couleurs

Cliquez sur `…` à côté de n'importe quelle couleur pour ouvrir la fenêtre des couleurs : choisissez dans le grand carré, réglez la teinte et l'opacité avec les deux curseurs, saisissez un code hexadécimal ou prélevez une couleur n'importe où à l'écran avec la pipette. **Palette**, **Récents** et (pour les remplissages) **Dégradés** proposent des couleurs prêtes. **Aucune couleur** laisse cette partie vide ; choisir une couleur ensuite la rend de nouveau entièrement visible.

Les remplissages de formes et les surlignages de texte peuvent être un **dégradé** : choisissez **Dégradé** en haut, cliquez sur la couleur de départ ou de fin pour la changer, réglez la direction avec les flèches ou en degrés, et `⇄` inverse les deux couleurs.

## Zoom

La molette de la souris zoome là où se trouve le pointeur ; `Ctrl`+molette fait défiler vers le haut et le bas, `Maj`+molette sur les côtés. `Ctrl+1` ajuste l'étiquette à la fenêtre et `Ctrl+0` affiche la taille réelle. Maintenez `Espace` (ou le bouton du milieu) et faites glisser pour vous déplacer.

## Annuler

Tout peut être annulé avec `Ctrl+Z` et rétabli avec `Ctrl+Y`.
