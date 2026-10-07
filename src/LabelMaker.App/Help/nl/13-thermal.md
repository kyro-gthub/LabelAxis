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
