---
id: data
title: Données et étiquettes variables
category: Data
order: 1
keywords: data, csv, excel, spreadsheet, database, sql, mysql, postgresql, access, odbc, sqlite, field, merge, variable, records, sequence, serial, counter, date, time, expression, function, duplicate, données, tableur, base de données, champ, publipostage, enregistrements, séquence, numéro de série, compteur, fonction, doublon
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

## Avant d'imprimer

`Données › Vérifier les doublons…` trouve les enregistrements qui imprimeraient deux fois la même valeur, comme deux étiquettes avec le même code-barres. Pour imprimer une étiquette par enregistrement, choisissez **Une étiquette par enregistrement de données** dans la fenêtre Imprimer ; voir [[printing|Impression]].
