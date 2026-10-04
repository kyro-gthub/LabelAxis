---
id: thermal
title: Thermal label printers
category: Printing
order: 4
keywords: thermal, zebra, tsc, xprinter, zpl, tspl, roll, bluetooth, serial, com port, network, wifi, darkness, speed, gap, black mark, tear off, peel, cut
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
