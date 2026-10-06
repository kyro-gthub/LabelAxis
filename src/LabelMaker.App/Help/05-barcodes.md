---
id: barcodes
title: Barcodes and QR codes
category: Designing
order: 3
keywords: barcode, qr, qr code, ean, upc, code 128, code 39, gs1, datamatrix, pdf417, aztec, itf, isbn, human readable, bulk barcode
---
# Barcodes and QR codes

`Barcode` and `QR Code` on the toolbar add one to the label. Its settings are under `Properties > Barcode Settings`.

## Types

| Type | Used for |
|---|---|
| EAN-13, EAN-8, UPC-A, UPC-E, ISBN-13 | Retail products |
| Code 128 (A, B, C), GS1-128 | Shipping, stock, serial numbers |
| Code 39, Code 93, Codabar, Interleaved 2 of 5, ITF-14, MSI Plessey | Older and industry systems |
| QR Code, Data Matrix, PDF417, Aztec | Web links, long text, small parts |

## Settings

- **Data:** what the barcode holds. It can be a field such as `{Barcode}` so each label gets its own.
- **Human-readable text:** the numbers printed under the bars, with their font and size.
- **Colours:** keep dark bars on a light background; scanners need the contrast.

If the data does not suit the type (for example letters in an EAN-13), the barcode is drawn as a red outline and the panel says why.

> Tip: Do not shrink a barcode below the size the type needs; scanners read narrow bars badly. Run `Print > Preflight check…` before a big job to catch problems.

## Many barcodes at once

`Data > Bulk Barcode Generator…` saves a picture file of a barcode for each value (with prefix, suffix and padding) into a folder. `Data > Barcode image…` saves one barcode as a PNG picture (150 to 1200 dpi), an SVG drawing or a PDF.
