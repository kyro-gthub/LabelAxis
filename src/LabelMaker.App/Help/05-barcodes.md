---
id: barcodes
title: Barcodes and QR codes
category: Designing
order: 3
keywords: barcode, qr, qr code, ean, upc, code 128, code 39, gs1, datamatrix, pdf417, aztec, itf, isbn, human readable, bulk barcode, gs1 builder, udi, medical device, expiry, batch, sscc, gtin, check digit, verify, verifier, grade, scan quality, camera, webcam, scanner, barcode scanner, usb scanner
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
- **Quiet zone:** the white space kept either side of the bars (around a QR code), in bar widths. 0 puts the bars right on the edges of the barcode's box; then leave white space around the box on the label yourself, as scanners need it. Ready-made designs use the amount the barcode's standard asks for.

If the data does not suit the type (for example letters in an EAN-13), the barcode is drawn as a red outline and the panel says why.

> Tip: Do not shrink a barcode below the size the type needs; scanners read narrow bars badly. Run `Print > Preflight check…` before a big job to catch problems.

## Many barcodes at once

`Data > Bulk Barcode Generator…` saves a picture file of a barcode for each value (with prefix, suffix and padding) into a folder. `Data > Barcode image…` saves one barcode as a PNG picture (150 to 1200 dpi), an SVG drawing or a PDF.

## GS1 barcodes: food, medicines, medical devices, shipping

`Data > GS1 barcode builder…` (or the button in Barcode Settings) makes GS1 barcodes the right way. Choose what the label is for, such as food with expiry date and batch, a medical device (UDI), a medicine pack, a carton or a pallet (SSCC), then fill in each line:

- Wrong lengths, check digits, dates that do not exist and data that needs other data with it are shown in plain words as you type. `Add check digit` works it out for you.
- Dates can be typed as 31/12/2026; they become YYMMDD.
- Choose **GS1-128** (bars, for cartons and food), **GS1 DataMatrix** (small square, for medicines and medical devices) or **GS1 QR Code**. For a square code the data can also be written as text beside it, as medical devices need.
- A value can be a data field such as `{Batch}`: it is filled in for each label when printing.

## Check a printed barcode

`Data > Check a printed barcode…` grades a printed barcode from A (best) to F, the way a barcode verifier does. Start the camera and hold the label in front of it, or open a photo or a scan. You see:

- what the barcode says, and whether it is the same as a barcode in the project (on any page, and for every record when it is filled in from data);
- each measurement (contrast, modulation, defects, decodability, quiet zones, bar width) with its grade;
- what to change, for example lower the printer's darkness when the bars print too wide.

It is a close guide, not a certificate: light and focus change the result, so check a few times.

### With a barcode scanner

A USB or Bluetooth barcode scanner works too: open the check window and scan a printed label (the **Barcode scanner** box at the top is ready as soon as the window opens). LabelAxis says whether what it read is the same as a barcode in the project (on any page, and for every record when a barcode is filled in from data), and keeps count ("Scanned 12: 11 match"), which is handy for checking a whole batch. A scanner tells what a barcode says but not how well it is printed, so there is no A to F grade; for a grade, use the camera or a photo.
