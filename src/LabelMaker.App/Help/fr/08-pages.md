---
id: pages
title: Pages : plusieurs étiquettes dans un projet
category: Pages
order: 1
keywords: page, pages, master label, duplicate, rename, reorder, remove, remove from all pages, delete everywhere, scroll, apply positions, apply format, page format, line up, same place, same size, apply formatting, other pages, add missing, étiquette modèle, dupliquer, renommer, réorganiser, supprimer, supprimer de toutes les pages, format de page, mise en forme
---
# Pages : plusieurs étiquettes dans un projet

Un projet peut contenir plusieurs modèles d'étiquettes différents, appelés **pages**, listés sous `Pages` à gauche et numérotés 1, 2, 3… La première page est aussi l'**étiquette modèle** : celle qui est répétée sur la feuille quand vous imprimez « Remplir la feuille ».

## Travailler avec les pages

- `+` ajoute une page vide ; le bouton de duplication copie la page affichée.
- Double-cliquez sur une page (ou appuyez sur `F2`) pour la renommer sur place.
- `Ctrl+↑` / `Ctrl+↓`, ou le glisser, changent l'ordre.
- Une longue liste de pages défile avec la molette ou avec les flèches haut et bas aux extrémités de sa barre de défilement.
- Le bouton de suppression supprime la page affichée. La première page peut aussi être supprimée tant qu'il existe d'autres pages ; la suivante devient alors la page 1.
- Pour trouver une page par son nom, appuyez sur `Ctrl+F` et tapez-en une partie : les noms de pages sont cherchés avec tout le reste (**Noms des pages** sous Chercher dans). Choisissez un résultat pour ouvrir la page ; **Remplacer** la renomme.

> Astuce : créez une étiquette, puis **dupliquez-la** pour chaque produit et changez seulement les textes. Les pages dupliquées gardent les noms de leurs objets, que l'outil ci-dessous utilise pour trouver « le même » objet sur chaque page.

## Donner aux autres pages le format de cette page

`Édition › Appliquer le format de page aux autres pages…` (aussi par clic droit sur une page, et dans l'onglet `Page`) donne aux autres pages l'apparence de la page affichée. Chaque zone de texte, code-barres, image, forme et tableau y prend la place, la taille et l'apparence de son équivalent sur cette page, et **garde ses propres textes, valeur de code-barres et image**.

- **Copier depuis :** tous les objets de cette page, ou seulement les objets sélectionnés.
- **Quoi copier :**
  - **Position**, **taille**, et **rotation et inclinaison**.
  - **Apparence et paramètres :** police et couleurs, type de code-barres, hauteur des barres et chiffres sous les barres, ajustement et bordure d'une image, contour et remplissage d'une forme, lignes et texte d'un tableau. Pour le texte, vous pouvez choisir quelles parties : police, taille du texte, gras/italique/souligné, couleurs, alignement, interligne et espacement des lettres.
  - **Ajouter les objets qu'une page n'a pas :** un objet de cette page sans équivalent sur une autre page y est copié, avec ses textes et son image.
- **Quels objets :** texte, codes-barres et codes QR, images, formes et tableaux, chacun activé ou non.
- **Associer par :** son **nom, sinon son ordre** (recommandé) ; son **nom** seulement ; ou son **ordre** seulement (la première zone de texte avec la première zone de texte, le deuxième code-barres avec le deuxième). Un code QR n'est jamais associé qu'à un code QR, et un code-barres linéaire qu'à un code-barres linéaire.
- **Quelles pages :** cochez-les ; chacune indique combien d'objets changeraient et combien seraient ajoutés.

Un type de code-barres qui ne peut pas contenir la valeur d'une page (EAN-13 pour les lettres « ABC ») n'est pas imposé à cette page ; le code-barres prend le reste de l'apparence.

## Supprimer un objet de toutes les pages

Pour retirer quelque chose de toutes les étiquettes d'un coup, comme un ancien code-barres, un logo ou une zone de texte nommée « Offre », sélectionnez-le sur une page et choisissez `Édition › Supprimer de toutes les pages…` (aussi par clic droit dessus sur l'étiquette ou dans `Calques`).

- **Le supprimer aussi de cette page** supprime aussi l'objet sélectionné lui-même ; décochez pour le garder ici.
- **Associer par** trouve le même objet sur les autres pages : son **nom, sinon son ordre** (recommandé), son **nom** seulement, ou son **ordre** seulement.
- **Quelles pages :** cochez-les ; chacune indique combien d'objets partiraient.

Les objets verrouillés ne sont jamais supprimés.

## Même mise en forme du texte sur d'autres pages

`Édition › Appliquer la mise en forme du texte aux autres pages…` copie la police, la taille, le style et la couleur de la zone de texte sélectionnée sur d'autres pages. Voir [[text|Texte]].

Chacun de ces outils est une seule étape d'annulation : `Ctrl+Z` rétablit toutes les pages.

## Imprimer des pages

Pour imprimer plusieurs pages par feuille, seulement la page affichée, ou quelques pages choisies avec leur propre nombre d'exemplaires, voir [[printing|Impression]].
