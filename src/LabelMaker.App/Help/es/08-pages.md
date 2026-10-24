---
id: pages
title: Páginas: varias etiquetas en un proyecto
category: Pages
order: 1
keywords: page, pages, master label, duplicate, rename, reorder, remove, remove from all pages, delete everywhere, scroll, apply positions, apply format, page format, line up, same place, same size, apply formatting, other pages, add missing, página, etiqueta maestra, duplicar, renombrar, reordenar, quitar, quitar de todas las páginas, formato de página, add pages, blank pages, copies, select several, ctrl click, shift click, print selected pages, numbered copies, count up, pages from records, make pages, sort, reverse, añadir páginas, páginas en blanco, copias, seleccionar varias, imprimir páginas seleccionadas, copias numeradas, crear páginas, ordenar, invertir
---
# Páginas: varias etiquetas en un proyecto

Un proyecto puede contener varios diseños de etiqueta distintos, llamados **páginas**, listados en `Páginas` a la izquierda y numerados 1, 2, 3… La primera página es también la **etiqueta maestra**: la que se repite por la hoja cuando imprime «Llenar la hoja».

## Trabajar con páginas

- `+` abre **Añadir páginas**: elija **Páginas en blanco** o **Copias de una página** (escoja la página en la lista; al principio está elegida la página en pantalla) y **cuántas**. Las páginas nuevas van justo después de la página en pantalla. Las copias también pueden numerar sus códigos de barras; vea «Copias numeradas» más abajo.
- El botón de duplicar copia la página o las páginas seleccionadas; cada copia va justo después de su página.
- Haga doble clic en una página (o pulse `F2`) para renombrarla en su sitio.
- `Ctrl+↑` / `Ctrl+↓`, o arrastrar, cambian el orden. Haga clic derecho en una página para **Ordenar por nombre, de la A a la Z**, **Ordenar por nombre, de la Z a la A** e **Invertir orden**, o use el botón de ordenar que hay sobre la lista.
- Una lista larga de páginas se desplaza con la rueda del ratón o con las flechas arriba y abajo de los extremos de su barra de desplazamiento. Una página nueva o duplicada se muestra sola.
- El botón de quitar elimina la página o las páginas seleccionadas, después de preguntar. También se puede quitar la primera página mientras haya otras; la siguiente pasa a ser la página 1. Siempre queda al menos una página.
- Para encontrar una página por su nombre, pulse `Ctrl+F` y escriba parte de él: los nombres de página se buscan junto con todo lo demás (**Nombres de página** en Buscar en). Elija un resultado para abrir esa página; **Reemplazar** la renombra.

> Consejo: haga una etiqueta y luego **duplíquela** para cada producto cambiando solo los textos. Las páginas duplicadas conservan los nombres de sus objetos, que la herramienta de abajo usa para encontrar «el mismo» objeto en cada página.

## Seleccionar varias páginas

En la lista de páginas, `Ctrl`+clic elige páginas una a una y `Shift`+clic elige todas las páginas entre la última en la que hizo clic y esta. Con varias seleccionadas:

- **Duplicar** copia cada una y **Quitar** las quita todas, cada cosa en un solo paso que `Ctrl+Z` deshace.
- Clic derecho › **Imprimir páginas seleccionadas…** abre Imprimir ajustado solo a esas páginas, varias por hoja.

## Copias numeradas

Clic derecho en una página › **Copias numeradas…** (o `+` › Copias de una página › **Numerar los códigos de barras en cada copia**) crea copias de esa página cuyos códigos de barras aumentan de uno en uno, página tras página: `BOX-0007` pasa a ser `BOX-0008`, `BOX-0009`, y así sucesivamente. Se conservan los ceros a la izquierda y se vuelve a calcular el dígito de control de los códigos EAN, UPC, ITF-14 e ISBN. Cada copia recibe el nombre de su nuevo número. Los códigos de barras rellenados con datos (`{Barcode}`) o sin número se quedan como están.

## Una página por registro

Para convertir registros importados (Excel, CSV o una base de datos) en páginas, una página por registro con sus valores escritos, consulte [[data|Datos y etiquetas variables]] › «Crear una página por registro».

## Dar a otras páginas el formato de esta página

`Edición › Aplicar formato de página a otras páginas…` (también con clic derecho en una página, y en la pestaña `Página`) hace que las otras páginas se parezcan a la página en pantalla. Cada cuadro de texto, código de barras, imagen, forma y tabla de ellas toma la posición, el tamaño y el aspecto de su equivalente en esta página, y **conserva sus propios textos, valor de código de barras e imagen**.

- **Copiar de:** todos los objetos de esta página, o solo los seleccionados.
- **Qué copiar:**
  - **Posición**, **tamaño**, y **giro e inclinación**.
  - **Aspecto y configuración:** fuente y colores, tipo de código de barras, altura de barras y dígitos bajo las barras, ajuste y borde de una imagen, contorno y relleno de una forma, líneas y texto de una tabla. Para el texto puede elegir qué partes: fuente, tamaño, negrita/cursiva/subrayado, colores, alineación, interlineado y espaciado entre letras.
  - **Añadir los objetos que le faltan a una página:** un objeto de esta página sin equivalente en otra se copia allí, con sus textos e imagen.
- **Qué objetos:** texto, códigos de barras y QR, imágenes, formas y tablas, cada uno activado o no.
- **Emparejar por:** su **nombre o, si no, su orden** (recomendado); solo su **nombre**; o solo su **orden** (el primer cuadro de texto con el primero, el segundo código de barras con el segundo). Un código QR solo se empareja con otro QR, y un código lineal con otro lineal.
- **Qué páginas:** márquelas; cada una dice cuántos objetos cambiarían y cuántos se añadirían.

Un tipo de código de barras que no puede contener el valor de una página (EAN-13 para las letras «ABC») no se impone en esa página; el código toma el resto del aspecto.

## Quitar un objeto de todas las páginas

Para quitar algo de todas las etiquetas a la vez, como un código de barras antiguo, un logotipo o un cuadro de texto llamado «Oferta», selecciónelo en una página y elija `Edición › Quitar de todas las páginas…` (también con clic derecho sobre él en la etiqueta o en `Capas`).

- **Quitarlo también de esta página** quita también el propio objeto seleccionado; desmárquelo para conservarlo aquí.
- **Emparejar por** encuentra el mismo objeto en las otras páginas: su **nombre o, si no, su orden** (recomendado), solo su **nombre**, o solo su **orden**.
- **Qué páginas:** márquelas; cada una dice cuántos objetos se quitarían.

Los objetos bloqueados nunca se quitan.

## El mismo formato de texto en otras páginas

`Edición › Aplicar formato de texto a otras páginas…` copia la fuente, el tamaño, el estilo y el color del cuadro de texto seleccionado a otras páginas. Consulte [[text|Texto]].

Cada una de estas herramientas es un solo paso de deshacer: `Ctrl+Z` deja todas las páginas como estaban.

## Imprimir páginas

Para imprimir varias páginas por hoja, solo la página en pantalla o unas pocas elegidas con su propio número de copias, consulte [[printing|Impresión]].
