---
id: data
title: Data and variable labels
category: Data
order: 1
keywords: data, csv, excel, spreadsheet, database, sql, mysql, postgresql, access, odbc, sqlite, field, merge, variable, records, sequence, serial, counter, date, time, expression, function, duplicate, make pages, page for each record, pages from records, barcode column, field as barcode
---
# Data and variable labels

Print a different label for every row of a spreadsheet or database: names, prices, barcodes, serial numbers.

## Connect your data

- `Data > Import CSV / Excel…` reads a .csv, .xlsx file.
- `Data > Connect to database…` reads from SQL Server, MySQL / MariaDB, PostgreSQL, SQLite, Microsoft Access or any ODBC source. `Edit connection…` changes it; `F5` refreshes the records.
- `Data > Records…` (`Ctrl+T`) shows the rows; edit them there, pick which to print, or print just the selected ones.

## Put fields on the label

Write the column name in curly brackets in any text or barcode: `{Product}`, `{Price}`, `{Barcode}`. `Data > Add data field` and a column insert one for you. The label on screen shows the current record; the arrows in the status bar step through them. A column of barcodes (EAN or UPC numbers, or a column called Barcode) asks first whether it goes on the label as a barcode that scans, and of which type, with the likely type already chosen; or as text.

## Ready-made fields

| Field | Shows |
|---|---|
| `{TODAY}` / `{NOW}` | Today's date / date and time |
| `{RECORDNUMBER}` / `{RECORDCOUNT}` | This record's number / how many there are |
| `{PAGENUMBER}` / `{LABELPOSITION}` | Sheet number / position of the label on the sheet |

## Calculations

Inside the brackets you can also use functions, for example `{UPPER(Product)}`, `{ROUND(Price * 1.15, 2)}`, `{IF(Stock > 0, "In stock", "Sold out")}`, `{DATEFORMAT(ADDDAYS(TODAY(), 30), "dd/MM/yyyy")}`. Others: LOWER, TRIM, LEFT, RIGHT, MID, CONCAT, REPLACE, PADLEFT, FORMAT, MIN, MAX, ABS, YEAR, MONTH, DAY, ADDMONTHS, IFEMPTY, CONTAINS, STARTSWITH.

## Serial numbers

`Data > Manage sequences…` sets up counters: start, end, step, and a prefix or suffix. Use one by its name in brackets; it goes up by one for each printed label.

## Make a page for each record

Instead of printing one label per record, you can make the records into pages, to change some of them by hand or to print them alongside other pages:

1. Put the columns on the label (`Records…` › **Add field to label** and the column, or type `{Product}` in a text box).
2. Choose `Data` › **Make pages…** for every record, or open `Records…`, select the records you want (`Ctrl`+click, `Shift`+click) and click **Make pages**. With none selected, it uses every record the filter shows.

Each new page is a copy of the label on screen with that record's values written into its text, barcodes and table cells, named after the record's first column. Dates, counters and other placeholders stay as they are, so they keep working when the pages print. Printing is then set to print the pages, several to a sheet. It is one step: `Ctrl+Z` takes all the new pages away. Up to 2,000 pages are made at a time.

## Before printing

`Data > Duplicate check…` finds records that would print the same value twice, such as two labels with the same barcode. To print one label per record, choose **One label per data record** in the Print window; see [[printing|Printing]].
