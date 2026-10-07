---
id: troubleshooting
title: Solución de problemas
category: Reference
order: 3
keywords: problem, help, wrong, error, not working, shifted, blurry, missing font, cut off, blank, smartscreen, warning, barcode not scanning, problema, ayuda, error, no funciona, desplazado, borroso, fuente que falta, cortado, en blanco, advertencia, código que no se lee
---
# Solución de problemas

## Las etiquetas se imprimen en el lugar equivocado

- Compruebe que la **hoja de etiquetas** elegida coincide con la caja de las hojas.
- Mantenga la **escala** al 100 % y desactive «ajustar a la página» en los ajustes propios de la impresora.
- Ejecute el [[calibration|asistente de calibración]] para esa impresora.

## El texto sale cortado o demasiado pequeño

Mire `Cuando se desborda` en la configuración de texto: **Agrandar el cuadro** mantiene todo el texto visible. Con **Reducir el texto** el tamaño baja hasta que cabe. Reduzca el texto, o agrande el cuadro, hasta que se vea todo.

## Una fuente se ve distinta en otro PC

El otro PC no tiene esa fuente. Las fuentes incluidas en LabelAxis (las cinco árabes y las del propio programa) siempre están, y una fuente añadida con `Añadir fuente…` viaja dentro del proyecto y se añade al abrirlo allí. Para una fuente instalada solo en Windows, instálela también en ese PC, o añada su archivo con `Añadir fuente…` para que el proyecto la lleve. La comprobación previa avisa de las fuentes que faltan.

## Las imágenes salen borrosas

La imagen tiene muy pocos píxeles para su tamaño impreso; la configuración de imagen muestra su DPI. Use una imagen más grande, o imprímala más pequeña.

## Un código de barras no se lee

Hágalo más grande, mantenga barras oscuras sobre fondo claro, deje un margen libre alrededor e imprima más oscuro en las impresoras térmicas. Compruebe que los datos encajan con el tipo de código ([[barcodes|Códigos de barras]]).

## La impresora no aparece

Instálela en Windows o, para una impresora de red, añádala en `Impresoras Wi-Fi…` ([[wifi-printers|Impresoras Wi-Fi]]). Impresoras térmicas y Bluetooth: consulte [[thermal|Impresoras térmicas de etiquetas]].

## «Windows protegió su PC» al instalar

Windows muestra esto con programas de un editor que todavía no conoce. Elija **Más información** y luego **Ejecutar de todas formas**.

## No se ofrece ninguna actualización

- Compruebe que hay una **dirección de actualización** en `Archivo › Configuración… › Actualizaciones`, o que esta versión se compiló con una, y que el PC puede llegar a ella.
- Pruebe `Ayuda › Buscar actualizaciones…`: el mensaje dice qué falló.
- Una copia portátil no se actualiza sola; consulte [[files|Proyectos, guardado y configuración]].

## Algo más salió mal

LabelAxis protege su trabajo en una copia de recuperación y escribe los detalles en un archivo de registro (en `%LOCALAPPDATA%\LabelAxis\logs`). Reinicie el programa y acepte la oferta de restaurar su trabajo.
