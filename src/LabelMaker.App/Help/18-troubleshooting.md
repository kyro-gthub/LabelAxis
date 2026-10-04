---
id: troubleshooting
title: Troubleshooting
category: Reference
order: 3
keywords: problem, help, wrong, error, not working, shifted, blurry, missing font, cut off, blank, smartscreen, warning, barcode not scanning
---
# Troubleshooting

## Labels print in the wrong place

- Check the chosen **label sheet** matches the box the sheets came in.
- Keep **Scale** at 100% and turn off "fit to page" in the printer's own settings.
- Run the [[calibration|calibration wizard]] for that printer.

## Text is cut off or too small

Look at `When it overflows` in Text Settings: **Grow the box** keeps all text visible. With **Shrink the text** the size drops until it fits. Make the text smaller, or the box bigger, until everything shows.

## A font looks different on another PC

The other PC does not have that font. The fonts that come with LabelAxis (the five Arabic fonts and the app's own) are always there; for others, install the font on that PC too. The preflight check warns about missing fonts.

## Pictures print blurry

The picture has too few pixels for its printed size; Image Settings shows its DPI. Use a larger picture, or print it smaller.

## A barcode will not scan

Make it bigger, keep dark bars on a light background, leave a clear margin around it, and print with more darkness on thermal printers. Check the data suits the barcode type ([[barcodes|Barcodes]]).

## Printer not listed

Install it in Windows, or for a network printer add it under `Wi-Fi printers…` ([[wifi-printers|Wi-Fi printers]]). Thermal and Bluetooth printers: see [[thermal|Thermal label printers]].

## "Windows protected your PC" when installing

Windows shows this for programs from a publisher it does not know yet. Choose **More info**, then **Run anyway**.

## Something else went wrong

LabelAxis keeps your work safe in a recovery snapshot and writes details to a log file (in `%LOCALAPPDATA%\LabelAxis\logs`). Restart the program and accept the offer to restore your work.
