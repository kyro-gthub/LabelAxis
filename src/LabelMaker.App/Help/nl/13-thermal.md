---
id: thermal
title: Thermische etiketprinters
category: Printing
order: 4
keywords: thermal, zebra, tsc, xprinter, zpl, tspl, roll, bluetooth, serial, com port, network, wifi, darkness, speed, gap, black mark, tear off, peel, cut, thermisch, rol, seriële poort, netwerk, donkerte, snelheid, tussenruimte, zwarte markering, afscheuren, afpellen, snijden
---
# Thermische etiketprinters

Thermische printers (Zebra, TSC, Xprinter en andere) drukken etiketten af van een rol. Begin met `Bestand › Nieuw thermisch etiket…` om het etiketformaat in te stellen, hoeveel etiketten naast elkaar op de rol zitten en de tussenruimte ertussen. `Afdrukken` opent daarna het venster voor thermisch afdrukken (ook `Afdrukken › Thermisch afdrukken (ZPL / TSPL)…`).

## Printer

- **Printertaal:** ZPL voor Zebra en "ZPL-compatibele" printers; TSPL voor TSC, Xprinter en veel desktopprinters.
- **Verzenden naar:**
  - **Windows-printer:** een printer die in Windows is geïnstalleerd.
  - **Netwerk (IP-adres):** een printer op het netwerk of via Wi-Fi. `Printers zoeken…` doorzoekt uw netwerk; `Testen` controleert of het adres antwoordt.
  - **Bluetooth of seriële poort (COM):** zie hieronder.
  - **Bestand:** slaat de printeropdrachten op in een .zpl- / .prn-bestand.
- **Resolutie:** 203, 300 of 600 dpi, zoals op het etiket van de printer.

## Bluetooth-printers

1. Zet de printer aan en koppel hem in **Windows-instellingen › Bluetooth en apparaten**.
2. Windows geeft een gekoppelde printer een COM-poort. Kies in het venster voor thermisch afdrukken **Bluetooth of seriële poort (COM)** en kies de poort gemarkeerd met **Bluetooth** (`↻ Opnieuw zoeken` vernieuwt de lijst na het koppelen).
3. Druk op `Testen`; als de poort opent, is de printer klaar.

> Opmerking: sommige goedkope etiketprinters die met een telefoonapp werken, gebruiken hun eigen Bluetooth-systeem in plaats van ZPL of TSPL; die kunnen niet vanuit een pc-programma worden gebruikt.

## Media en kwaliteit

- **Etiketdetectie:** tussenruimte tussen etiketten, zwarte markering of doorlopende rol.
- **Na het afdrukken:** afscheuren, afpellen of snijden.
- **Afdruk verschuiven:** verschuift de afdruk iets naar rechts/omlaag om een printer te corrigeren; **Ondersteboven afdrukken** voor rollen die andersom uitkomen.
- **Donkerte** en **snelheid:** donkerder en langzamer voor scherpe barcodes.
- **Grijstinten worden:** scherp voor tekst en barcodes, of met tinten voor foto's.

Het voorbeeld rechts toont precies de punten die de printer ontvangt.

## Afbeelding of printerlettertypen

**Labels verzenden als** (onder Printertaal) bepaalt hoe elk label de printer bereikt:

- **Een afbeelding (precies zoals op het scherm):** elk label gaat als één afbeelding van punten, precies zoals het voorbeeld. Elk lettertype, Arabische tekst, foto's en kleurverlopen worden afgedrukt zoals ontworpen. Het beste voor dagelijks gebruik.
- **Printerlettertypen en barcodes (snelst):** voor lange series. De delen die nooit veranderen (kaders, logo's, vaste tekst) worden aan het begin van de taak één keer in de printer opgeslagen; daarna stuurt elk label alleen zijn wisselende tekst en barcodes, die de printer zelf tekent.
  - Barcodes die de printer kent (Code 128, GS1-128, EAN-13, EAN-8, UPC-A, Code 39, Code 93, Interleaved 2 of 5, Codabar, QR en Data Matrix) tekent de printer, met dezelfde streepjes als LabelAxis.
  - Wisselende tekst gebruikt het eigen lettertype van de printer en ziet er dus iets anders uit dan op het scherm. Regels breken waar ze op het scherm breken.
  - Wat de printer niet kan tekenen (Arabische en andere tekst van rechts naar links, letters buiten de West-Europese, lichte tekst op een donker vlak, schuine tekst, wisselende tabellen) gaat als een kleine afbeelding van alleen dat gebied.
  - Het venster toont hoeveel onderdelen de printer zelf tekent. Het voorbeeld toont het label zoals ontworpen.

> Tip: druk eerst een paar labels af. Loopt tekst in het printerlettertype buiten zijn vak, maak het vak dan breder of de tekst kleiner.
