---
id: data
title: Datos y etiquetas variables
category: Data
order: 1
keywords: data, csv, excel, spreadsheet, database, sql, mysql, postgresql, access, odbc, sqlite, field, merge, variable, records, sequence, serial, counter, date, time, expression, function, duplicate, datos, hoja de cálculo, base de datos, campo, combinar, registros, secuencia, número de serie, contador, función, duplicado
---
# Datos y etiquetas variables

Imprima una etiqueta distinta por cada fila de una hoja de cálculo o base de datos: nombres, precios, códigos de barras, números de serie.

## Conectar sus datos

- `Datos › Importar CSV / Excel…` lee un archivo .csv o .xlsx.
- `Datos › Conectar a base de datos…` lee de SQL Server, MySQL / MariaDB, PostgreSQL, SQLite, Microsoft Access o cualquier origen ODBC. `Editar conexión…` la cambia; `F5` actualiza los registros.
- `Datos › Registros…` (`Ctrl+T`) muestra las filas; edítelas ahí, elija cuáles imprimir o imprima solo las seleccionadas.

## Poner campos en la etiqueta

Escriba el nombre de la columna entre llaves en cualquier texto o código de barras: `{Product}`, `{Price}`, `{Barcode}`. `Datos › Añadir campo de datos…` inserta uno por usted. La etiqueta en pantalla muestra el registro actual; las flechas de la barra de estado los recorren.

## Campos ya preparados

| Campo | Muestra |
|---|---|
| `{TODAY}` / `{NOW}` | La fecha de hoy / fecha y hora |
| `{RECORDNUMBER}` / `{RECORDCOUNT}` | El número de este registro / cuántos hay |
| `{PAGENUMBER}` / `{LABELPOSITION}` | Número de hoja / posición de la etiqueta en la hoja |

## Cálculos

Dentro de las llaves también puede usar funciones, por ejemplo `{UPPER(Product)}`, `{ROUND(Price * 1.15, 2)}`, `{IF(Stock > 0, "En stock", "Agotado")}`, `{DATEFORMAT(ADDDAYS(TODAY(), 30), "dd/MM/yyyy")}`. Otras: LOWER, TRIM, LEFT, RIGHT, MID, CONCAT, REPLACE, PADLEFT, FORMAT, MIN, MAX, ABS, YEAR, MONTH, DAY, ADDMONTHS, IFEMPTY, CONTAINS, STARTSWITH.

## Números de serie

`Datos › Gestionar secuencias…` configura contadores: inicio, fin, paso y un prefijo o sufijo. Use uno por su nombre entre llaves; sube de uno en uno con cada etiqueta impresa.

## Antes de imprimir

`Datos › Comprobar duplicados…` encuentra registros que imprimirían el mismo valor dos veces, como dos etiquetas con el mismo código de barras. Para imprimir una etiqueta por registro, elija **Una etiqueta por registro de datos** en la ventana Imprimir; consulte [[printing|Impresión]].
