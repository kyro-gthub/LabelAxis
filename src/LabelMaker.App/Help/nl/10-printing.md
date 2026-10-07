---
id: printing
title: Afdrukken
category: Printing
order: 1
keywords: print, printer, sheet, copies, preview, pages, current page, selected pages, copies per page, paper, margins, start position, skip, history, reprint, outlines, crop marks, automatic printing, watch folder, hot folder, web link, api, integration, other programs, json, csv, afdrukken, vel, exemplaren, voorbeeld, huidige pagina, marges, beginpositie, geschiedenis, opnieuw afdrukken, omtrekken, snijtekens, bewaakte map, weblink, andere programma's
---
# Afdrukken

`Ctrl+P` (of `Afdrukken` rechtsboven) opent het venster Afdrukken. De afbeelding rechts toont het eerste vel precies zoals het wordt afgedrukt; `Voorbeeld…` toont alle vellen.

## Wat afdrukken

- **Het vel vullen met het hoofdetiket:** hetzelfde etiket op elke plek.
- **Eén etiket per gegevensrecord:** een ander etiket voor elke rij van uw gegevens (zie [[data|Gegevens]]).
- **Pagina's:** meerdere verschillende pagina's op één vel, na elkaar:
  - **Alle pagina's**, **Huidige pagina** (de pagina die u bekeek), of **Geselecteerde pagina's** (vink ze aan in de lijst).
  - Elke pagina heeft een eigen vak voor **exemplaren**: pagina 1 kan 6 keer en pagina 2 twaalf keer op dezelfde vellen worden afgedrukt. **Exemplaren van elke pagina** stelt ze allemaal tegelijk in. De aantallen worden met het project opgeslagen.

> Tip: als u op Afdrukken drukt terwijl een pagina (niet het hoofdetiket) op het scherm staat, opent het venster Afdrukken al ingesteld op die pagina.

## Beginpositie en bereiken

`Beginpositie en paginabereik…` (ook `Afdrukken › Geavanceerde afdrukinstellingen…`) begint op een latere plek van een deels gebruikt vel, slaat plekken over en kiest welke records of pagina's worden afgedrukt. **Vellen … tot …** drukt alleen enkele vellen opnieuw af, bijvoorbeeld een vastgelopen vel.

## Papier en marges

Normaal komen etiketten waar het velsjabloon aangeeft. Kies een ander **papierformaat**, **liggend**, of **mijn eigen marges en tussenruimtes gebruiken** als uw vel afwijkt van het sjabloon.

## Exemplaren, schaal en omtrekken

- **Exemplaren** drukt de hele taak meer dan één keer af.
- **Schaal** blijft op 100% zodat etiketten op de juiste grootte uitkomen; wijzig hem alleen om een printer te corrigeren, liever met [[calibration|kalibratie]].
- In `Geavanceerd…` tekenen **etiketomtrekken** en **snijtekens** de randen van elk etiket, handig op gewoon papier.

## Printers

De lijst bevat de printers die in Windows zijn geïnstalleerd en de [[wifi-printers|Wi-Fi-printers]] die u hebt toegevoegd. `Windows-venster…` opent de eigen instellingen van de printer (dubbelzijdig, lade). Thermische etiketprinters hebben een eigen venster; zie [[thermal|Thermische etiketprinters]].

## Geschiedenis

`Geschiedenis…` (of `Afdrukken › Afdrukgeschiedenis…`) toont eerdere taken; druk er een opnieuw af, of maak een taak af die halverwege stopte.

## Afdrukverzoeken van andere programma's

Een kassa, webwinkel of spreadsheet kan etiketten afdrukken terwijl LabelAxis open is: `Afdrukken › Afdrukverzoeken van andere programma's…`.

- **Bewaakte map:** bestanden die erin worden gezet, worden afgedrukt: een `.json`-verzoek, of een `.csv`-lijst waarvan de eerste rij de velden noemt (afgedrukt met het daar gekozen ontwerp). Afgedrukte bestanden gaan naar `Done`; andere gaan naar `Failed` met een notitie waarom.
- **Weblink:** programma's op deze pc sturen hetzelfde verzoek naar `http://localhost:8765/print`. Andere computers kunnen ook worden toegestaan; zij moeten de toegangssleutel meesturen. Webpagina's in een browser worden geweigerd.
- Een verzoek noemt een van uw ontwerpen (zie [[getting-started|Uw eigen ontwerpen]]), de printer, de exemplaren en één record per etiket. Met `"output": "pdf"` maakt het een PDF in plaats van af te drukken.

Elk verzoek verschijnt in de lijst in dat venster, en in de afdrukgeschiedenis.
