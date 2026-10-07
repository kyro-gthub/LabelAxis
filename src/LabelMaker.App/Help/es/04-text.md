---
id: text
title: Texto
category: Designing
order: 2
keywords: text, font, size, bold, italic, colour, color, highlight, align, line spacing, letter spacing, grow, shrink, wrap, edit, format, apply formatting, other pages, custom font, add font, font file, ttf, otf, texto, fuente, tamaño, negrita, cursiva, color, resaltado, alineación, interlineado, formato, añadir fuente
---
# Texto

Haga clic en `Texto` en la barra de herramientas para añadir un cuadro de texto.

## Escribir y editar

- Haga **doble clic** en el texto (o pulse `Intro` o `F2` con él seleccionado) para escribir directamente en la etiqueta. Ahí puede seleccionar palabras sueltas y ponerlas en negrita, cursiva, en color o en otro tamaño. Haga clic fuera o pulse `Esc` al terminar.
- El cuadro de `Propiedades › Configuración de texto` también edita las palabras.

## Aspecto del texto

En `Configuración de texto`: fuente, tamaño, **B** / *I* / subrayado / tachado, color, resaltado, alineación (izquierda, centro, derecha, justificado; arriba, medio, abajo), interlineado, espaciado entre letras y relleno interior.

El **resaltado** rellena todo el cuadro detrás del texto. Haga clic en `…` junto a él para elegir un color o, con **Degradado**, dos colores que se funden entre sí.

> Nota: cambiar un estilo en el panel lo aplica a todo el cuadro, también a las palabras con formato propio.

## Sus propias fuentes

Pulse `Añadir fuente…` junto a la lista de fuentes y elija un archivo de fuente (`.ttf` u `.otf`): la fuente se añade a LabelAxis, sin instalarla en Windows, y se aplica al texto seleccionado. Después aparece en todas las listas de fuentes, en pantalla, al imprimir y en los PDF. `Archivo › Configuración › Fuentes` lista las fuentes añadidas y permite quitarlas.

Un proyecto que usa esa fuente guarda una copia dentro, para que se vea igual en otro PC: al abrirlo allí se añade la fuente.

## Cuando el texto no cabe

`Cuando se desborda` decide qué pasa:

- **Agrandar el cuadro** (cuadros de texto nuevos): el cuadro crece en altura para que todo el texto quede dentro. Arrastrar un tirador lateral reajusta las líneas y la altura sigue.
- **Reducir el texto**: las letras se hacen más pequeñas hasta caber, hasta el tamaño fijado en `Mínimo (pt)`.
- **Dejar que se salga**: el cuadro mantiene su tamaño.

## Árabe y otros textos de derecha a izquierda

Escriba en árabe (o hebreo) y el párrafo va de derecha a izquierda por sí solo: las letras se unen, las palabras se leen desde la derecha y los números y las palabras en inglés mantienen su propio orden. Consulte [[arabic|Texto árabe y fuentes]].

## El mismo formato en otras páginas

`Aplicar este formato a otras páginas…` (al pie de la configuración de texto, o en el menú `Edición`) da la fuente, el tamaño, el estilo y el color del cuadro seleccionado, y si quiere su alineación y espaciado, a cuadros de texto de otras páginas: el cuadro con el **mismo nombre** en cada página, o **todos los cuadros de texto**. Consulte [[pages|Páginas]].

## Texto variable con datos

Ponga un nombre de campo entre llaves, como `{Product}`, y cada etiqueta impresa muestra esa columna de su hoja de cálculo. Consulte [[data|Datos y etiquetas variables]].
