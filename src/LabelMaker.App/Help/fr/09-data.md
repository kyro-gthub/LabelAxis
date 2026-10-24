---
id: data
title: Données et étiquettes variables
category: Data
order: 1
keywords: data, csv, excel, spreadsheet, database, sql, mysql, postgresql, access, odbc, sqlite, field, merge, variable, records, sequence, serial, counter, date, time, expression, function, duplicate, données, tableur, base de données, champ, publipostage, enregistrements, séquence, numéro de série, compteur, fonction, doublon, make pages, page for each record, pages from records, créer des pages, une page par enregistrement
---
# Données et étiquettes variables

Imprimez une étiquette différente pour chaque ligne d'un tableur ou d'une base de données : noms, prix, codes-barres, numéros de série.

## Connecter vos données

- `Données › Importer CSV / Excel…` lit un fichier .csv ou .xlsx.
- `Données › Se connecter à une base de données…` lit depuis SQL Server, MySQL / MariaDB, PostgreSQL, SQLite, Microsoft Access ou toute source ODBC. `Modifier la connexion…` la change ; `F5` actualise les enregistrements.
- `Données › Enregistrements…` (`Ctrl+T`) affiche les lignes ; modifiez-les là, choisissez lesquelles imprimer, ou imprimez seulement celles sélectionnées.

## Placer des champs sur l'étiquette

Écrivez le nom de la colonne entre accolades dans n'importe quel texte ou code-barres : `{Product}`, `{Price}`, `{Barcode}`. `Données › Ajouter un champ de données…` en insère un pour vous. L'étiquette à l'écran montre l'enregistrement actuel ; les flèches de la barre d'état les parcourent.

## Champs tout prêts

| Champ | Affiche |
|---|---|
| `{TODAY}` / `{NOW}` | Date du jour / date et heure |
| `{RECORDNUMBER}` / `{RECORDCOUNT}` | Numéro de cet enregistrement / nombre total |
| `{PAGENUMBER}` / `{LABELPOSITION}` | Numéro de la feuille / position de l'étiquette sur la feuille |

## Calculs

Entre les accolades, vous pouvez aussi utiliser des fonctions, par exemple `{UPPER(Product)}`, `{ROUND(Price * 1.15, 2)}`, `{IF(Stock > 0, "En stock", "Épuisé")}`, `{DATEFORMAT(ADDDAYS(TODAY(), 30), "dd/MM/yyyy")}`. Autres : LOWER, TRIM, LEFT, RIGHT, MID, CONCAT, REPLACE, PADLEFT, FORMAT, MIN, MAX, ABS, YEAR, MONTH, DAY, ADDMONTHS, IFEMPTY, CONTAINS, STARTSWITH.

## Numéros de série

`Données › Gérer les séquences…` crée des compteurs : début, fin, pas, et un préfixe ou un suffixe. Utilisez-en un par son nom entre accolades ; il augmente de un à chaque étiquette imprimée.

## Créer une page par enregistrement

Au lieu d'imprimer une étiquette par enregistrement, vous pouvez transformer les enregistrements en pages, pour en modifier certaines à la main ou les imprimer avec d'autres pages :

1. Placez les colonnes sur l'étiquette (`Enregistrements…` › **Ajouter le champ à l'étiquette**, ou tapez `{Product}` dans une zone de texte).
2. Choisissez `Données` › **Créer des pages…** pour tous les enregistrements, ou ouvrez `Enregistrements…`, sélectionnez ceux que vous voulez (`Ctrl`+clic, `Shift`+clic) et cliquez sur **Créer des pages**. Si aucun n'est sélectionné, tous les enregistrements affichés par le filtre sont utilisés.

Chaque nouvelle page est une copie de l'étiquette affichée, avec les valeurs de cet enregistrement écrites dans ses textes, codes-barres et cellules de tableau, et porte le nom de la première colonne de l'enregistrement. Les dates, compteurs et autres champs restent tels quels, et fonctionnent donc toujours à l'impression des pages. L'impression est ensuite réglée pour imprimer les pages, plusieurs par feuille. C'est une seule étape : `Ctrl+Z` retire toutes les nouvelles pages.

## Avant d'imprimer

`Données › Vérifier les doublons…` trouve les enregistrements qui imprimeraient deux fois la même valeur, comme deux étiquettes avec le même code-barres. Pour imprimer une étiquette par enregistrement, choisissez **Une étiquette par enregistrement de données** dans la fenêtre Imprimer ; voir [[printing|Impression]].
