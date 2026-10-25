---
id: pages
title: Pagina's: meerdere etiketten in één project
category: Pages
order: 1
keywords: page, pages, master label, duplicate, rename, reorder, remove, remove from all pages, delete everywhere, scroll, apply positions, apply format, page format, line up, same place, same size, apply formatting, other pages, add missing, pagina, hoofdetiket, dupliceren, hernoemen, volgorde, verwijderen, van alle pagina's verwijderen, paginaopmaak, add pages, blank pages, copies, select several, ctrl click, shift click, print selected pages, numbered copies, count up, pages from records, make pages, sort, reverse, pagina's toevoegen, lege pagina's, kopieën, meerdere selecteren, geselecteerde pagina's afdrukken, genummerde kopieën, pagina's maken, sorteren, omkeren, apply formatting, every text box, opmaak toepassen, elk tekstvak
---
# Pagina's: meerdere etiketten in één project

Een project kan meerdere verschillende etiketontwerpen bevatten, **pagina's** genoemd, die links onder `Pagina's` staan en genummerd zijn 1, 2, 3… De eerste pagina is ook het **hoofdetiket**: het etiket dat over het vel wordt herhaald als u "Vel vullen" afdrukt.

## Werken met pagina's

- `+` opent **Pagina's toevoegen**: kies **Lege pagina's** of **Kopieën van een pagina** (kies de pagina in de lijst; eerst is de pagina op het scherm gekozen), en **hoeveel**. De nieuwe pagina's komen direct na de pagina op het scherm. Kopieën kunnen ook hun barcodes laten oplopen; zie "Genummerde kopieën" hieronder.
- De dupliceerknop kopieert de geselecteerde pagina of pagina's; elke kopie komt direct na haar pagina.
- Dubbelklik op een pagina (of druk op `F2`) om haar ter plekke te hernoemen.
- `Ctrl+↑` / `Ctrl+↓`, of slepen, wijzigen de volgorde. Klik met rechts op een pagina voor **Sorteren op naam, A tot Z**, **Sorteren op naam, Z tot A** en **Volgorde omkeren**, of gebruik de sorteerknop boven de lijst.
- Een lange lijst met pagina's schuift met het muiswiel of met de pijlen omhoog en omlaag aan de uiteinden van de schuifbalk. Een nieuwe of gedupliceerde pagina komt vanzelf in beeld.
- De verwijderknop verwijdert de geselecteerde pagina of pagina's, na een vraag. Ook de eerste pagina kan worden verwijderd zolang er andere pagina's zijn; de volgende pagina wordt dan pagina 1. Er blijft altijd minstens één pagina over.
- Om een pagina op naam te vinden, drukt u op `Ctrl+F` en typt u een deel van de naam: paginanamen worden samen met al het andere doorzocht (**Paginanamen** onder Zoeken in). Kies een resultaat om die pagina te openen; **Vervangen** hernoemt haar.

> Tip: maak één etiket, **dupliceer** het voor elk product en wijzig alleen de tekst. Gedupliceerde pagina's houden de namen van hun objecten, die het hulpmiddel hieronder gebruikt om "hetzelfde" object op elke pagina te vinden.

## Meerdere pagina's selecteren

In de paginalijst kiest `Ctrl`+klik pagina's één voor één en kiest `Shift`+klik alle pagina's tussen de pagina waarop u het laatst klikte en deze. Met meerdere geselecteerd:

- **Dupliceren** kopieert elk ervan en **Verwijderen** verwijdert ze allemaal, elk in één stap die `Ctrl+Z` terugdraait.
- Rechtsklik › **Geselecteerde pagina's afdrukken…** opent Afdrukken, ingesteld op alleen die pagina's, meerdere per vel.

## Genummerde kopieën

Rechtsklik op een pagina › **Genummerde kopieën…** (of `+` › Kopieën van een pagina › **De barcodenummers op elke kopie laten oplopen**) maakt kopieën van die pagina waarvan de barcodes per pagina met één oplopen: `BOX-0007` wordt `BOX-0008`, `BOX-0009`, enzovoort. Voorloopnullen blijven staan, en het controlecijfer van EAN-, UPC-, ITF-14- en ISBN-barcodes wordt opnieuw berekend. Elke kopie krijgt haar nieuwe nummer als naam. Barcodes die uit gegevens komen (`{Barcode}`) of geen nummer hebben, blijven zoals ze zijn.

## Een pagina per record

Om geïmporteerde records (Excel, CSV of een database) in pagina's om te zetten, één pagina per record met de waarden erin, zie [[data|Gegevens en variabele etiketten]] › "Een pagina per record maken".

## Opmaak op andere pagina's toepassen

`Bewerken › Opmaak op andere pagina's toepassen…` (ook met rechtsklikken op een pagina, en op het tabblad `Pagina`) laat de andere pagina's eruitzien als de pagina op het scherm. Elk tekstvak, elke barcode, afbeelding, vorm en tabel daar krijgt de plaats, grootte en het uiterlijk van zijn tegenhanger op deze pagina, en **houdt zijn eigen tekst, barcodewaarde en afbeelding**.

- **Kopiëren van:** elk object op deze pagina, of alleen de geselecteerde objecten.
- **Wat kopiëren:**
  - **Positie**, **grootte**, en **draaiing en helling**.
  - **Uiterlijk en instellingen:** lettertype en kleuren, barcodetype, streephoogte en de cijfers onder de strepen, hoe een afbeelding past en haar rand, de omtrek en opvulling van een vorm, de lijnen en tekst van een tabel. Voor tekst kiest u welke delen: lettertype, tekstgrootte, vet/cursief/onderstrepen, kleuren, uitlijning, regel- en letterafstand.
  - **De objecten toevoegen die een pagina mist:** een object op deze pagina zonder tegenhanger op een andere pagina wordt daarheen gekopieerd, met zijn tekst en afbeelding.
- **Welke objecten:** tekst, barcodes en QR-codes, afbeeldingen, vormen en tabellen, elk aan of uit.
- **Koppelen op:** de **naam, anders de volgorde** (aanbevolen); alleen de **naam**; of alleen de **volgorde** (het eerste tekstvak met het eerste tekstvak, de tweede barcode met de tweede barcode). Een QR-code wordt alleen met een QR-code gekoppeld, en een streepjescode alleen met een streepjescode. Of **elk van dezelfde soort**: het uiterlijk van één tekstvak (of barcode, vorm…) gaat naar elk van die soort op de aangevinkte pagina's. `Deze opmaak op andere pagina's toepassen…` onder aan Tekstinstellingen opent het venster zo ingesteld voor het geselecteerde tekstvak: alleen het uiterlijk, voor elk tekstvak.
- **Welke pagina's:** vink ze aan; bij elke staat hoeveel objecten zouden veranderen en hoeveel er zouden worden toegevoegd.

Een barcodetype dat de waarde van een pagina niet kan bevatten (EAN-13 voor de letters "ABC"), wordt die pagina niet opgedrongen; de barcode krijgt de rest van het uiterlijk.

## Een object van alle pagina's verwijderen

Om iets in één keer van alle etiketten te halen, zoals een oude barcode, een logo of een tekstvak met de naam "Aanbieding", selecteert u het op één pagina en kiest u `Bewerken › Van alle pagina's verwijderen…` (ook met rechtsklikken erop op het etiket of in `Lagen`).

- **Ook van deze pagina verwijderen** verwijdert ook het geselecteerde object zelf; vink het uit om het hier te houden.
- **Koppelen op** vindt hetzelfde object op de andere pagina's: de **naam, anders de volgorde** (aanbevolen), alleen de **naam**, of alleen de **volgorde**.
- **Welke pagina's:** vink ze aan; bij elke staat hoeveel objecten zouden verdwijnen.

Vergrendelde objecten worden nooit verwijderd.

Elk van deze hulpmiddelen is één stap om ongedaan te maken: `Ctrl+Z` zet elke pagina terug.

## Pagina's afdrukken

Om meerdere pagina's per vel af te drukken, alleen de pagina op het scherm, of enkele gekozen pagina's met hun eigen aantal exemplaren, zie [[printing|Afdrukken]].
