---
id: data
title: Data and variable labels
category: Data
order: 1
keywords: data, csv, excel, spreadsheet, database, sql, mysql, postgresql, access, odbc, sqlite, field, merge, variable, records, sequence, serial, counter, date, time, expression, function, duplicate
---
# Data and variable labels

Print a different label for every row of a spreadsheet or database: names, prices, barcodes, serial numbers.

## Connect your data

- `Data > Import CSV / Excel…` reads a .csv, .xlsx file.
- `Data > Connect to database…` reads from SQL Server, MySQL / MariaDB, PostgreSQL, SQLite, Microsoft Access or any ODBC source. `Edit connection…` changes it; `F5` refreshes the records.
- `Data > Records…` (`Ctrl+T`) shows the rows; edit them there, pick which to print, or print just the selected ones.

## Put fields on the label

Write the column name in curly brackets in any text or barcode: `{Product}`, `{Price}`, `{Barcode}`. `Data > Add data field…` inserts one for you. The label on screen shows the current record; the arrows in the status bar step through them.

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

## Before printing

`Data > Duplicate check…` finds records that would print the same value twice, such as two labels with the same barcode. To print one label per record, choose **One label per data record** in the Print window; see [[printing|Printing]].
