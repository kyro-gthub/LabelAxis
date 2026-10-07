---
id: pages
title: Páginas: varias etiquetas en un proyecto
category: Pages
order: 1
keywords: page, pages, master label, duplicate, rename, reorder, remove, remove from all pages, delete everywhere, scroll, apply positions, apply format, page format, line up, same place, same size, apply formatting, other pages, add missing, página, etiqueta maestra, duplicar, renombrar, reordenar, quitar, quitar de todas las páginas, formato de página
---
# Páginas: varias etiquetas en un proyecto

Un proyecto puede contener varios diseños de etiqueta distintos, llamados **páginas**, listados en `Páginas` a la izquierda y numerados 1, 2, 3… La primera página es también la **etiqueta maestra**: la que se repite por la hoja cuando imprime «Llenar la hoja».

## Trabajar con páginas

- `+` añade una página vacía; el botón de duplicar copia la página en pantalla.
- Haga doble clic en una página (o pulse `F2`) para renombrarla en su sitio.
- `Ctrl+↑` / `Ctrl+↓`, o arrastrar, cambian el orden.
- Una lista larga de páginas se desplaza con la rueda del ratón o con las flechas arriba y abajo de los extremos de su barra de desplazamiento.
- El botón de quitar elimina la página en pantalla. También se puede quitar la primera página mientras haya otras; la siguiente pasa a ser la página 1.
- Para encontrar una página por su nombre, pulse `Ctrl+F` y escriba parte de él: los nombres de página se buscan junto con todo lo demás (**Nombres de página** en Buscar en). Elija un resultado para abrir esa página; **Reemplazar** la renombra.

> Consejo: haga una etiqueta y luego **duplíquela** para cada producto cambiando solo los textos. Las páginas duplicadas conservan los nombres de sus objetos, que la herramienta de abajo usa para encontrar «el mismo» objeto en cada página.

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
