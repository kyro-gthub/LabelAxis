---
id: text
title: Texte
category: Designing
order: 2
keywords: text, font, size, bold, italic, colour, color, highlight, align, line spacing, letter spacing, grow, shrink, wrap, edit, format, apply formatting, other pages, custom font, add font, font file, ttf, otf, texte, police, taille, gras, italique, couleur, surlignage, alignement, interligne, mise en forme, ajouter une police
---
# Texte

Cliquez sur `Texte` dans la barre d'outils pour ajouter une zone de texte.

## Saisir et modifier

- **Double-cliquez** sur le texte (ou appuyez sur `Entrée` ou `F2` quand il est sélectionné) pour écrire directement sur l'étiquette. Vous pouvez y sélectionner des mots isolés et les mettre en gras, en italique, en couleur ou dans une autre taille. Cliquez à l'extérieur ou appuyez sur `Échap` quand c'est fini.
- La zone sous `Propriétés › Paramètres du texte` modifie aussi les mots.

## Apparence du texte

Dans `Paramètres du texte` : police, taille, **B** / *I* / souligné / barré, couleur, surlignage, alignement (gauche, centre, droite, justifié ; haut, milieu, bas), interligne, espacement des lettres et marge intérieure.

Le **surlignage** remplit toute la zone derrière le texte. Cliquez sur `…` à côté pour choisir une couleur ou, avec **Dégradé**, deux couleurs qui se fondent l'une dans l'autre.

> Remarque : changer un style dans le panneau l'applique à toute la zone, y compris aux mots mis en forme à part.

## Vos propres polices

Appuyez sur `Ajouter une police…` à côté de la liste des polices et choisissez un fichier de police (`.ttf` ou `.otf`) : la police est ajoutée à LabelAxis, sans être installée dans Windows, et appliquée au texte sélectionné. Elle figure ensuite dans toutes les listes de polices, à l'écran, à l'impression et dans les PDF. `Fichier › Paramètres › Polices` liste les polices ajoutées et permet de les retirer.

Un projet qui utilise une telle police en garde une copie, pour qu'il ait le même aspect sur un autre PC : l'ouvrir là-bas ajoute la police.

## Quand le texte ne tient pas

`En cas de débordement` décide de ce qui se passe :

- **Agrandir le cadre** (nouvelles zones de texte) : le cadre devient plus haut pour que tout le texte reste dedans. Faire glisser une poignée latérale replie le texte et la hauteur suit.
- **Réduire le texte** : les lettres rapetissent jusqu'à tenir, jusqu'à la taille indiquée dans `Plus petite (pt)`.
- **Laisser déborder** : le cadre garde sa taille.

## Arabe et autres textes de droite à gauche

Tapez de l'arabe (ou de l'hébreu) et le paragraphe s'écrit de droite à gauche tout seul : les lettres se lient, les mots se lisent depuis la droite, et les nombres et mots anglais gardent leur propre ordre. Voir [[arabic|Texte arabe et polices]].

## Même mise en forme sur d'autres pages

`Appliquer cette mise en forme aux autres pages…` (en bas des paramètres du texte, ou dans le menu `Édition`) donne la police, la taille, le style et la couleur de la zone sélectionnée, et si vous voulez son alignement et ses espacements, aux zones de texte d'autres pages : soit la zone du **même nom** sur chaque page, soit **toutes les zones de texte**. Voir [[pages|Pages]].

## Texte variable avec des données

Mettez un nom de champ entre accolades, comme `{Product}`, et chaque étiquette imprimée affiche cette colonne de votre tableur. Voir [[data|Données et étiquettes variables]].
