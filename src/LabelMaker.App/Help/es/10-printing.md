---
id: printing
title: Impresión
category: Printing
order: 1
keywords: print, printer, sheet, copies, preview, pages, current page, selected pages, copies per page, paper, margins, start position, skip, history, reprint, outlines, crop marks, automatic printing, watch folder, hot folder, web link, api, integration, other programs, json, csv, imprimir, impresora, hoja, copias, vista previa, página actual, márgenes, posición inicial, historial, reimprimir, contornos, marcas de corte, carpeta vigilada, enlace web, otros programas
---
# Impresión

`Ctrl+P` (o `Imprimir` arriba a la derecha) abre la ventana Imprimir. La imagen de la derecha muestra la primera hoja tal como se imprimirá; `Vista previa…` muestra todas las hojas.

## Qué imprimir

- **Llenar la hoja con la etiqueta maestra:** la misma etiqueta en cada hueco.
- **Una etiqueta por registro de datos:** una etiqueta distinta por cada fila de sus datos (consulte [[data|Datos]]).
- **Páginas:** varias páginas distintas en una hoja, una tras otra:
  - **Todas las páginas**, **Página actual** (la que estaba viendo) o **Páginas seleccionadas** (márquelas en la lista).
  - Cada página tiene su propia casilla de **copias**: la página 1 puede imprimirse 6 veces y la página 2 doce veces en las mismas hojas. **Copias de cada página** las ajusta todas a la vez. Los números se guardan con el proyecto.

> Consejo: pulsar Imprimir con una página (no la etiqueta maestra) en pantalla abre la ventana Imprimir ya ajustada a esa página.

## Posición inicial e intervalos

`Posición inicial e intervalo de páginas…` (también `Imprimir › Configuración de impresión avanzada…`) empieza en un hueco posterior de una hoja ya empezada, salta huecos y elige qué registros o páginas imprimir. **Hojas … a …** reimprime solo algunas hojas, por ejemplo una que se atascó.

## Papel y márgenes

Normalmente las etiquetas van donde indica la plantilla de hoja. Elija otro **tamaño de papel**, **horizontal**, o **usar mis propios márgenes y separaciones** cuando su hoja difiera de la plantilla.

## Copias, escala y contornos

- **Copias** imprime el trabajo entero más de una vez.
- **Escala** se queda en 100 % para que las etiquetas salgan del tamaño correcto; cámbiela solo para corregir una impresora, mejor con la [[calibration|calibración]].
- En `Avanzado…`, los **contornos de etiquetas** y las **marcas de corte** dibujan los bordes de cada etiqueta, útil en papel normal.

## Impresoras

La lista contiene las impresoras instaladas en Windows y las [[wifi-printers|impresoras Wi-Fi]] que haya añadido. `Cuadro de Windows…` abre los ajustes propios de la impresora (doble cara, bandeja). Las impresoras térmicas de etiquetas tienen su propia ventana; consulte [[thermal|Impresoras térmicas de etiquetas]].

## Historial

`Historial…` (o `Imprimir › Historial de impresión…`) muestra los trabajos anteriores; reimprima uno o termine un trabajo que se detuvo a medias.

## Solicitudes de impresión de otros programas

Una caja registradora, una tienda en línea o una hoja de cálculo pueden imprimir etiquetas mientras LabelAxis está abierto: `Imprimir › Solicitudes de impresión de otros programas…`.

- **Carpeta vigilada:** se imprimen los archivos que se dejan en ella: una solicitud `.json`, o una lista `.csv` cuya primera fila nombra los campos (impresa con el diseño elegido ahí). Los archivos impresos pasan a `Done`; los demás pasan a `Failed` con una nota que explica por qué.
- **Enlace web:** los programas de este PC envían la misma solicitud a `http://localhost:8765/print`. También se pueden permitir otros equipos; deben enviar la clave de acceso. Las páginas web de un navegador se rechazan.
- Una solicitud indica uno de sus diseños (consulte [[getting-started|Sus propios diseños]]), la impresora, las copias y un registro por etiqueta. Con `"output": "pdf"` crea un PDF en lugar de imprimir.

Cada solicitud aparece en la lista de esa ventana y en el historial de impresión.
