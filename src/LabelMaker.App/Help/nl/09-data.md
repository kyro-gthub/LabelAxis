---
id: data
title: Gegevens en variabele etiketten
category: Data
order: 1
keywords: data, csv, excel, spreadsheet, database, sql, mysql, postgresql, access, odbc, sqlite, field, merge, variable, records, sequence, serial, counter, date, time, expression, function, duplicate, gegevens, rekenblad, veld, samenvoegen, reeks, serienummer, teller, functie, dubbel, make pages, page for each record, pages from records, pagina's maken, pagina per record, barcode column, barcodekolom
---
# Gegevens en variabele etiketten

Druk voor elke rij van een spreadsheet of database een ander etiket af: namen, prijzen, barcodes, serienummers.

## Uw gegevens koppelen

- `Gegevens › CSV / Excel importeren…` leest een .csv- of .xlsx-bestand.
- `Gegevens › Verbinden met database…` leest uit SQL Server, MySQL / MariaDB, PostgreSQL, SQLite, Microsoft Access of elke ODBC-bron. `Verbinding bewerken…` wijzigt haar; `F5` vernieuwt de records.
- `Gegevens › Records…` (`Ctrl+T`) toont de rijen; bewerk ze daar, kies welke u afdrukt, of druk alleen de geselecteerde af.

## Velden op het etiket zetten

Schrijf de kolomnaam tussen accolades in een tekst of barcode: `{Product}`, `{Price}`, `{Barcode}`. `Gegevens › Gegevensveld toevoegen` en een kolom voegt er een voor u in. Het etiket op het scherm toont de huidige record; de pijlen in de statusbalk lopen ze door. Bij een kolom met barcodes (EAN- of UPC-nummers, of een kolom met de naam Barcode) wordt eerst gevraagd of die op het etiket komt als een barcode die scant, en van welk type, met het waarschijnlijke type al gekozen; of als tekst.

## Kant-en-klare velden

| Veld | Toont |
|---|---|
| `{TODAY}` / `{NOW}` | De datum van vandaag / datum en tijd |
| `{RECORDNUMBER}` / `{RECORDCOUNT}` | Het nummer van deze record / hoeveel er zijn |
| `{PAGENUMBER}` / `{LABELPOSITION}` | Velnummer / positie van het etiket op het vel |

## Berekeningen

Tussen de accolades kunt u ook functies gebruiken, bijvoorbeeld `{UPPER(Product)}`, `{ROUND(Price * 1.15, 2)}`, `{IF(Stock > 0, "Op voorraad", "Uitverkocht")}`, `{DATEFORMAT(ADDDAYS(TODAY(), 30), "dd/MM/yyyy")}`. Andere: LOWER, TRIM, LEFT, RIGHT, MID, CONCAT, REPLACE, PADLEFT, FORMAT, MIN, MAX, ABS, YEAR, MONTH, DAY, ADDMONTHS, IFEMPTY, CONTAINS, STARTSWITH.

## Serienummers

`Gegevens › Reeksen beheren…` stelt tellers in: begin, einde, stap en een voor- of achtervoegsel. Gebruik er een met zijn naam tussen accolades; hij gaat met één omhoog voor elk afgedrukt etiket.

## Een pagina per record maken

In plaats van één etiket per record af te drukken, kunt u de records in pagina's omzetten, om er sommige met de hand te wijzigen of ze samen met andere pagina's af te drukken:

1. Zet de kolommen op het etiket (`Records…` › **Veld aan etiket toevoegen** en de kolom, of typ `{Product}` in een tekstvak).
2. Kies `Gegevens` › **Pagina's maken…** voor elk record, of open `Records…`, selecteer de records die u wilt (`Ctrl`+klik, `Shift`+klik) en klik op **Pagina's maken**. Als er geen geselecteerd zijn, worden alle records gebruikt die het filter toont.

Elke nieuwe pagina is een kopie van het etiket op het scherm met de waarden van dat record in de tekst, barcodes en tabelcellen, en krijgt de eerste kolom van het record als naam. Datums, tellers en andere plaatshouders blijven zoals ze zijn, dus ze werken nog steeds als de pagina's worden afgedrukt. Daarna is het afdrukken ingesteld op de pagina's, meerdere per vel. Het is één stap: `Ctrl+Z` haalt alle nieuwe pagina's weg. Er worden maximaal 2.000 pagina's tegelijk gemaakt.

## Voor het afdrukken

`Gegevens › Dubbele controleren…` vindt records die dezelfde waarde twee keer zouden afdrukken, zoals twee etiketten met dezelfde barcode. Om één etiket per record af te drukken, kiest u **Eén etiket per gegevensrecord** in het venster Afdrukken; zie [[printing|Afdrukken]].
