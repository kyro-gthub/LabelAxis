---
id: barcodes
title: Barcodes en QR-codes
category: Designing
order: 3
keywords: barcode, qr, qr code, ean, upc, code 128, code 39, gs1, datamatrix, pdf417, aztec, itf, isbn, human readable, bulk barcode, gs1 builder, udi, medical device, expiry, batch, sscc, gtin, check digit, verify, verifier, grade, scan quality, camera, webcam, streepjescode, GS1-bouwer, medisch hulpmiddel, vervaldatum, controlecijfer, controleren, beoordeling, camera
---
# Barcodes en QR-codes

`Barcode` en `QR Code` op de werkbalk voegen er een toe aan het etiket. De instellingen staan onder `Eigenschappen › Barcode-instellingen`.

## Soorten

| Soort | Gebruikt voor |
|---|---|
| EAN-13, EAN-8, UPC-A, UPC-E, ISBN-13 | Winkelproducten |
| Code 128 (A, B, C), GS1-128 | Verzending, voorraad, serienummers |
| Code 39, Code 93, Codabar, Interleaved 2 of 5, ITF-14, MSI Plessey | Oudere en industriële systemen |
| QR Code, Data Matrix, PDF417, Aztec | Weblinks, lange tekst, kleine onderdelen |

## Instellingen

- **Gegevens:** wat de barcode bevat. Dit kan een veld zijn zoals `{Barcode}`, zodat elk etiket zijn eigen krijgt.
- **Leesbare tekst:** de cijfers onder de strepen, met hun lettertype en grootte.
- **Kleuren:** houd donkere strepen op een lichte achtergrond; scanners hebben dat contrast nodig.

Passen de gegevens niet bij het type (bijvoorbeeld letters in een EAN-13), dan wordt de barcode als rode omtrek getekend en zegt het paneel waarom.

> Tip: maak een barcode niet kleiner dan het type nodig heeft; scanners lezen smalle strepen slecht. Voer vóór een grote taak `Afdrukken › Controle vooraf…` uit om problemen op te sporen.

## Veel barcodes tegelijk

`Gegevens › Barcodes in bulk genereren…` slaat voor elke waarde (met voorvoegsel, achtervoegsel en opvulling) een afbeeldingsbestand van een barcode op in een map. `Gegevens › Barcode-afbeelding…` slaat één barcode op als PNG-afbeelding (150 tot 1200 dpi), SVG-tekening of PDF.

## GS1-barcodes: voedsel, geneesmiddelen, medische hulpmiddelen, verzending

`Gegevens › GS1-barcodebouwer…` (of de knop in Barcode-instellingen) maakt GS1-barcodes op de juiste manier. Kies waarvoor het etiket is, zoals voedsel met vervaldatum en batch, een medisch hulpmiddel (UDI), een geneesmiddelverpakking, een doos of een pallet (SSCC), en vul elke regel in:

- Verkeerde lengtes, controlecijfers, data die niet bestaan en gegevens die andere gegevens nodig hebben, worden tijdens het typen in gewone woorden getoond. `Controlecijfer toevoegen` rekent het voor u uit.
- Data kunt u typen als 31/12/2026; ze worden JJMMDD.
- Kies **GS1-128** (strepen, voor dozen en voedsel), **GS1 DataMatrix** (klein vierkant, voor geneesmiddelen en medische hulpmiddelen) of **GS1 QR Code**. Bij een vierkante code kunnen de gegevens ook als tekst ernaast worden geschreven, zoals medische hulpmiddelen vereisen.
- Een waarde kan een gegevensveld zijn zoals `{Batch}`: het wordt bij het afdrukken voor elk etiket ingevuld.

## Een afgedrukte barcode controleren

`Gegevens › Een afgedrukte barcode controleren…` beoordeelt een afgedrukte barcode van A (best) tot F, zoals een barcodeverificateur dat doet. Start de camera en houd het etiket ervoor, of open een foto of scan. U ziet:

- wat de barcode zegt, en of die gelijk is aan een barcode op de pagina die u ontwerpt;
- elke meting (contrast, modulatie, defecten, decodeerbaarheid, stille zones, streepbreedte) met de beoordeling;
- wat u moet veranderen, bijvoorbeeld de donkerte van de printer verlagen als de strepen te breed worden afgedrukt.

Het is een goede richtlijn, geen certificaat: licht en scherpstelling veranderen de uitkomst, dus controleer een paar keer.
