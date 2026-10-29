---
id: pages
title: Pages : plusieurs étiquettes dans un projet
category: Pages
order: 1
keywords: page, pages, master label, duplicate, rename, reorder, remove, remove from all pages, delete everywhere, scroll, apply positions, apply format, page format, line up, same place, same size, apply formatting, other pages, add missing, étiquette modèle, dupliquer, renommer, réorganiser, supprimer, supprimer de toutes les pages, format de page, mise en forme, add pages, blank pages, copies, select several, ctrl click, shift click, print selected pages, numbered copies, count up, pages from records, make pages, sort, reverse, ajouter des pages, pages vides, copies, sélectionner plusieurs, imprimer les pages sélectionnées, copies numérotées, créer des pages, trier, inverser, apply formatting, every text box, appliquer la mise en forme, toutes les zones de texte
---
# Pages : plusieurs étiquettes dans un projet

Un projet peut contenir plusieurs modèles d'étiquettes différents, appelés **pages**, listés sous `Pages` à gauche et numérotés 1, 2, 3… La première page est aussi l'**étiquette modèle** : celle qui est répétée sur la feuille quand vous imprimez « Remplir la feuille ».

## Travailler avec les pages

- `+` ouvre **Ajouter des pages** : choisissez **Pages vides** ou **Copies d'une page** (choisissez la page dans la liste ; la page affichée est choisie au départ), et **combien**. Les nouvelles pages se placent juste après la page affichée. Les copies peuvent aussi incrémenter leurs codes-barres ; voir « Copies numérotées » plus bas.
- Le bouton de duplication copie la ou les pages sélectionnées ; chaque copie se place juste après sa page.
- Double-cliquez sur une page (ou appuyez sur `F2`) pour la renommer sur place.
- `Ctrl+↑` / `Ctrl+↓`, ou le glisser, changent l'ordre. Faites un clic droit sur une page pour **Trier par nom, de A à Z**, **Trier par nom, de Z à A** et **Inverser l'ordre**, ou utilisez le bouton de tri au-dessus de la liste.
- Une longue liste de pages défile avec la molette ou avec les flèches haut et bas aux extrémités de sa barre de défilement. Une page nouvelle ou dupliquée s'affiche d'elle-même.
- Le bouton de suppression supprime la ou les pages sélectionnées, après confirmation. La première page peut aussi être supprimée tant qu'il existe d'autres pages ; la suivante devient alors la page 1. Il reste toujours au moins une page.
- Pour trouver une page par son nom, appuyez sur `Ctrl+F` et tapez-en une partie : les noms de pages sont cherchés avec tout le reste (**Noms des pages** sous Chercher dans). Choisissez un résultat pour ouvrir la page ; **Remplacer** la renomme.

> Astuce : créez une étiquette, puis **dupliquez-la** pour chaque produit et changez seulement les textes. Les pages dupliquées gardent les noms de leurs objets, que l'outil ci-dessous utilise pour trouver « le même » objet sur chaque page.

## Sélectionner plusieurs pages

Dans la liste des pages, `Ctrl`+clic choisit les pages une par une et `Shift`+clic choisit toutes les pages entre celle où vous avez cliqué en dernier et celle-ci. Avec plusieurs pages sélectionnées :

- **Dupliquer** copie chacune d'elles et **Supprimer** les supprime toutes, chaque fois en une seule étape que `Ctrl+Z` annule.
- Clic droit › **Imprimer les pages sélectionnées…** ouvre Imprimer réglé sur ces seules pages, plusieurs par feuille.

## Copies numérotées

Clic droit sur une page › **Copies numérotées…** (ou `+` › Copies d'une page › **Incrémenter les codes-barres sur chaque copie**) crée des copies de cette page dont les codes-barres augmentent de un, page après page : `BOX-0007` devient `BOX-0008`, `BOX-0009`, et ainsi de suite. Les zéros de tête sont gardés, et la clé de contrôle des codes EAN, UPC, ITF-14 et ISBN est recalculée. Chaque copie porte le nom de son nouveau numéro. Les codes-barres remplis par des données (`{Barcode}`) ou sans numéro restent tels quels.

## Une page par enregistrement

Pour transformer des enregistrements importés (Excel, CSV ou une base de données) en pages, une page par enregistrement avec ses valeurs écrites, voir [[data|Données et étiquettes variables]] › « Créer une page par enregistrement ».

## Appliquer la mise en forme aux autres pages

`Édition › Appliquer la mise en forme aux autres pages…` (aussi par clic droit sur une page) donne aux autres pages l'apparence de la page affichée. Chaque zone de texte, code-barres, image, forme et tableau y prend la place, la taille et l'apparence de son équivalent sur cette page, et **garde ses propres textes, valeur de code-barres et image**.

- **Copier depuis :** tous les objets de cette page, ou seulement les objets sélectionnés.
- **Quoi copier :**
  - **Position**, **taille**, et **rotation et inclinaison**.
  - **Apparence et paramètres :** police et couleurs, type de code-barres, hauteur des barres et chiffres sous les barres, ajustement et bordure d'une image, contour et remplissage d'une forme, lignes et texte d'un tableau. Pour le texte, vous pouvez choisir quelles parties : police, taille du texte, gras/italique/souligné, couleurs, alignement, interligne et espacement des lettres.
  - **Ajouter les objets qu'une page n'a pas :** un objet de cette page sans équivalent sur une autre page y est copié, avec ses textes et son image.
- **Quels objets :** texte, codes-barres et codes QR, images, formes et tableaux, chacun activé ou non.
- **Associer par :** son **nom, sinon son ordre** (recommandé) ; son **nom** seulement ; ou son **ordre** seulement (la première zone de texte avec la première zone de texte, le deuxième code-barres avec le deuxième). Un code QR n'est jamais associé qu'à un code QR, et un code-barres linéaire qu'à un code-barres linéaire. Ou **tous ceux du même type** : l'aspect d'une zone de texte (ou d'un code-barres, d'une forme…) va à tous ceux de son type sur les pages cochées. `Appliquer cette mise en forme aux autres pages…`, en bas des Paramètres du texte, ouvre la fenêtre réglée ainsi pour la zone de texte sélectionnée : son aspect seulement, pour toutes les zones de texte.
- **Quelles pages :** cochez-les ; chacune indique combien d'objets changeraient et combien seraient ajoutés.

Un type de code-barres qui ne peut pas contenir la valeur d'une page (EAN-13 pour les lettres « ABC ») n'est pas imposé à cette page ; le code-barres prend le reste de l'apparence.

## Supprimer un objet de toutes les pages

Pour retirer quelque chose de toutes les étiquettes d'un coup, comme un ancien code-barres, un logo ou une zone de texte nommée « Offre », sélectionnez-le sur une page et choisissez `Édition › Supprimer de toutes les pages…` (aussi par clic droit dessus sur l'étiquette ou dans **Objets de la page**).

- **Le supprimer aussi de cette page** supprime aussi l'objet sélectionné lui-même ; décochez pour le garder ici.
- **Associer par** trouve le même objet sur les autres pages : son **nom, sinon son ordre** (recommandé), son **nom** seulement, ou son **ordre** seulement.
- **Quelles pages :** cochez-les ; chacune indique combien d'objets partiraient.

Les objets verrouillés ne sont jamais supprimés.

Chacun de ces outils est une seule étape d'annulation : `Ctrl+Z` rétablit toutes les pages.

## Imprimer des pages

Pour imprimer plusieurs pages par feuille, seulement la page affichée, ou quelques pages choisies avec leur propre nombre d'exemplaires, voir [[printing|Impression]].
