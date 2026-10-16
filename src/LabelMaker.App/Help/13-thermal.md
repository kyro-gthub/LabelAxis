---
id: thermal
title: Thermal label printers
category: Printing
order: 4
keywords: thermal, zebra, tsc, xprinter, zpl, tspl, printer fonts, native, fast, long runs, picture, roll, bluetooth, serial, com port, network, wifi, darkness, speed, gap, black mark, tear off, peel, cut
---
# Thermal label printers

Thermal printers (Zebra, TSC, Xprinter and others) print labels from a roll. Start with `File > New thermal label…` to set the label size, how many labels sit across the roll and the gap between them. `Print` then opens the thermal print window (also `Print > Thermal print (ZPL / TSPL)…`).

## Printer

- **Printer language:** ZPL for Zebra and "ZPL compatible" printers; TSPL for TSC, Xprinter and many desktop printers.
- **Send to:**
  - **Windows printer:** a printer installed in Windows.
  - **Network (IP address):** a printer on the network or Wi-Fi. `Find printers…` searches your network; `Test` checks the address answers.
  - **Bluetooth or serial port (COM):** see below.
  - **File:** saves the printer commands to a .zpl / .prn file.
- **Resolution:** 203, 300 or 600 dpi, as on the printer's label.

## Bluetooth printers

1. Switch the printer on and pair it in **Windows Settings > Bluetooth & devices**.
2. Windows gives a paired printer a COM port. In the thermal print window choose **Bluetooth or serial port (COM)** and pick the port marked **Bluetooth** (`↻ Look again` refreshes the list after pairing).
3. Press `Test`; when the port opens, the printer is ready.

> Note: Some cheap phone-app label printers use their own private Bluetooth system instead of ZPL or TSPL; those cannot be used from a PC program.

## Media and quality

- **Label detection:** gap between labels, black mark, or continuous roll.
- **After printing:** tear off, peel or cut.
- **Move print:** shifts the print a little right/down to correct a printer; **Print upside down** for rolls that come out the other way.
- **Darkness** and **speed:** darker and slower for sharp barcodes.
- **Greys become:** crisp for text and barcodes, or shaded for photos.

The preview on the right shows exactly the dots the printer will receive.

## Picture or printer fonts

**Send labels as** (under Printer language) chooses how each label reaches the printer:

- **A picture (exactly like the screen):** each label goes as one picture of dots, exactly as the preview shows. Any font, Arabic text, photos and gradients print as designed. Best for everyday printing.
- **Printer fonts and barcodes (fastest):** for long runs. The parts that never change (frames, logos, fixed text) are stored on the printer once at the start of the job; each label then sends only its changing text and barcodes, which the printer draws itself.
  - Barcodes the printer knows (Code 128, GS1-128, EAN-13, EAN-8, UPC-A, Code 39, Code 93, Interleaved 2 of 5, Codabar, QR and Data Matrix) are drawn by the printer, with the same bars LabelAxis draws.
  - Changing text uses the printer's own font, so it looks a little different from the screen. Lines break where they do on screen.
  - What the printer cannot draw (Arabic and other right-to-left text, letters beyond Western European ones, light text on a dark box, text at an angle, changing tables) goes as a small picture of just that area.
  - The window says how many items the printer draws itself. The preview shows the label as designed.

> Tip: Print a few labels first. If text in the printer's font runs past its box, make the box wider or the text smaller.
