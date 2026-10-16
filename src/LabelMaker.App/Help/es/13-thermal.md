---
id: thermal
title: Impresoras térmicas de etiquetas
category: Printing
order: 4
keywords: thermal, zebra, tsc, xprinter, zpl, tspl, roll, bluetooth, serial, com port, network, wifi, darkness, speed, gap, black mark, tear off, peel, cut, térmica, rollo, puerto serie, red, oscuridad, velocidad, separación, marca negra, arrancar, despegar, cortar
---
# Impresoras térmicas de etiquetas

Las impresoras térmicas (Zebra, TSC, Xprinter y otras) imprimen etiquetas de un rollo. Empiece con `Archivo › Nueva etiqueta térmica…` para fijar el tamaño de la etiqueta, cuántas etiquetas hay a lo ancho del rollo y la separación entre ellas. `Imprimir` abre después la ventana de impresión térmica (también `Imprimir › Impresión térmica (ZPL / TSPL)…`).

## Impresora

- **Lenguaje de impresora:** ZPL para Zebra e impresoras «compatibles con ZPL»; TSPL para TSC, Xprinter y muchas impresoras de escritorio.
- **Enviar a:**
  - **Impresora de Windows:** una impresora instalada en Windows.
  - **Red (dirección IP):** una impresora en la red o por Wi-Fi. `Buscar impresoras…` busca en su red; `Probar` comprueba que la dirección responde.
  - **Bluetooth o puerto serie (COM):** vea más abajo.
  - **Archivo:** guarda los comandos de impresora en un archivo .zpl / .prn.
- **Resolución:** 203, 300 o 600 ppp, como en la etiqueta de la impresora.

## Impresoras Bluetooth

1. Encienda la impresora y emparéjela en **Configuración de Windows › Bluetooth y dispositivos**.
2. Windows asigna un puerto COM a una impresora emparejada. En la ventana de impresión térmica elija **Bluetooth o puerto serie (COM)** y escoja el puerto marcado **Bluetooth** (`↻ Buscar de nuevo` actualiza la lista tras emparejar).
3. Pulse `Probar`; cuando el puerto se abre, la impresora está lista.

> Nota: algunas impresoras de etiquetas baratas que se manejan con una aplicación de móvil usan su propio sistema Bluetooth en lugar de ZPL o TSPL; esas no se pueden usar desde un programa de PC.

## Soporte y calidad

- **Detección de etiquetas:** separación entre etiquetas, marca negra o rollo continuo.
- **Después de imprimir:** arrancar, despegar o cortar.
- **Desplazar impresión:** mueve un poco la impresión a la derecha/abajo para corregir una impresora; **Imprimir al revés** para rollos que salen en el otro sentido.
- **Oscuridad** y **velocidad:** más oscuro y más lento para códigos de barras nítidos.
- **Los grises se convierten en:** nítido para texto y códigos de barras, o sombreado para fotos.

La vista previa de la derecha muestra exactamente los puntos que recibirá la impresora.

## Imagen o fuentes de la impresora

**Enviar etiquetas como** (bajo Lenguaje de impresora) elige cómo llega cada etiqueta a la impresora:

- **Una imagen (exactamente como en pantalla):** cada etiqueta se envía como una sola imagen de puntos, exactamente como muestra la vista previa. Cualquier fuente, texto árabe, fotos y degradados se imprimen tal como se diseñaron. Lo mejor para el día a día.
- **Fuentes y códigos de barras de la impresora (más rápido):** para tiradas largas. Las partes que nunca cambian (marcos, logotipos, texto fijo) se guardan una sola vez en la impresora al empezar el trabajo; después cada etiqueta envía solo su texto y códigos de barras variables, que dibuja la propia impresora.
  - Los códigos de barras que conoce la impresora (Code 128, GS1-128, EAN-13, EAN-8, UPC-A, Code 39, Code 93, Interleaved 2 of 5, Codabar, QR y Data Matrix) los dibuja la impresora, con las mismas barras que LabelAxis.
  - El texto variable usa la fuente de la impresora, así que se ve un poco distinto que en pantalla. Las líneas se cortan donde se cortan en pantalla.
  - Lo que la impresora no puede dibujar (texto árabe y otros de derecha a izquierda, letras fuera de las de Europa occidental, texto claro sobre fondo oscuro, texto inclinado, tablas variables) se envía como una pequeña imagen solo de esa zona.
  - La ventana indica cuántos elementos dibuja la propia impresora. La vista previa muestra la etiqueta tal como se diseñó.

> Consejo: imprima primero unas pocas etiquetas. Si el texto en la fuente de la impresora se sale de su cuadro, ensanche el cuadro o reduzca el texto.
