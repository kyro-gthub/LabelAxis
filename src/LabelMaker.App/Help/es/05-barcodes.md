---
id: barcodes
title: Códigos de barras y QR
category: Designing
order: 3
keywords: barcode, qr, qr code, ean, upc, code 128, code 39, gs1, datamatrix, pdf417, aztec, itf, isbn, human readable, bulk barcode, gs1 builder, udi, medical device, expiry, batch, sscc, gtin, check digit, verify, verifier, grade, scan quality, camera, webcam, código de barras, generador GS1, producto sanitario, caducidad, lote, dígito de control, comprobar, calificación, cámara
---
# Códigos de barras y QR

`Código de barras` y `QR Code` en la barra de herramientas añaden uno a la etiqueta. Sus ajustes están en `Propiedades › Configuración del código de barras`.

## Tipos

| Tipo | Uso |
|---|---|
| EAN-13, EAN-8, UPC-A, UPC-E, ISBN-13 | Productos de venta al público |
| Code 128 (A, B, C), GS1-128 | Envíos, stock, números de serie |
| Code 39, Code 93, Codabar, Interleaved 2 of 5, ITF-14, MSI Plessey | Sistemas antiguos e industriales |
| QR Code, Data Matrix, PDF417, Aztec | Enlaces web, texto largo, piezas pequeñas |

## Ajustes

- **Datos:** lo que contiene el código de barras. Puede ser un campo como `{Barcode}` para que cada etiqueta tenga el suyo.
- **Texto legible:** los números impresos bajo las barras, con su fuente y tamaño.
- **Colores:** mantenga barras oscuras sobre fondo claro; los lectores necesitan el contraste.

Si los datos no encajan con el tipo (por ejemplo, letras en un EAN-13), el código se dibuja como un contorno rojo y el panel explica por qué.

> Consejo: no reduzca un código de barras por debajo del tamaño que necesita su tipo; los lectores leen mal las barras estrechas. Ejecute `Imprimir › Comprobación previa…` antes de un trabajo grande para detectar problemas.

## Muchos códigos de barras a la vez

`Datos › Generador masivo de códigos de barras…` guarda en una carpeta un archivo de imagen de código de barras por cada valor (con prefijo, sufijo y relleno). `Datos › Imagen de código de barras…` guarda un código de barras como imagen PNG (150 a 1200 ppp), dibujo SVG o PDF.

## Códigos GS1: alimentos, medicamentos, productos sanitarios, envíos

`Datos › Generador de códigos GS1…` (o el botón en la configuración del código de barras) crea códigos GS1 correctamente. Elija para qué es la etiqueta, como un alimento con fecha de caducidad y lote, un producto sanitario (UDI), un envase de medicamento, una caja o un palé (SSCC), y complete cada línea:

- Las longitudes incorrectas, los dígitos de control, las fechas que no existen y los datos que necesitan otros datos se muestran en palabras claras mientras escribe. `Añadir dígito de control` lo calcula por usted.
- Las fechas se pueden escribir como 31/12/2026; se convierten en AAMMDD.
- Elija **GS1-128** (barras, para cajas y alimentos), **GS1 DataMatrix** (cuadrado pequeño, para medicamentos y productos sanitarios) o **GS1 QR Code**. En un código cuadrado los datos también pueden escribirse como texto al lado, como exigen los productos sanitarios.
- Un valor puede ser un campo de datos como `{Batch}`: se rellena para cada etiqueta al imprimir.

## Comprobar un código de barras impreso

`Datos › Comprobar un código de barras impreso…` califica un código impreso de A (el mejor) a F, como lo hace un verificador. Inicie la cámara y ponga la etiqueta delante, o abra una foto o un escaneo. Verá:

- qué dice el código de barras y si es igual a un código de la página que diseña;
- cada medida (contraste, modulación, defectos, decodificabilidad, zonas de silencio, ancho de barra) con su calificación;
- qué cambiar, por ejemplo bajar la oscuridad de la impresora cuando las barras salen demasiado anchas.

Es una guía cercana, no un certificado: la luz y el enfoque cambian el resultado, así que compruebe varias veces.
