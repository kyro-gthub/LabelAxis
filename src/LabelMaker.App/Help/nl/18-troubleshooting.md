---
id: troubleshooting
title: Probleemoplossing
category: Reference
order: 3
keywords: problem, help, wrong, error, not working, shifted, blurry, missing font, cut off, blank, smartscreen, warning, barcode not scanning, probleem, hulp, fout, werkt niet, verschoven, wazig, ontbrekend lettertype, afgesneden, leeg, waarschuwing, barcode scant niet
---
# Probleemoplossing

## Etiketten worden op de verkeerde plek afgedrukt

- Controleer of het gekozen **etiketvel** overeenkomt met de doos waarin de vellen zaten.
- Houd **Schaal** op 100% en zet "passend op pagina" uit in de eigen instellingen van de printer.
- Voer de [[calibration|kalibratiewizard]] uit voor die printer.

## Tekst is afgesneden of te klein

Kijk bij `Als het overloopt` in Tekstinstellingen: **Het vak vergroten** houdt alle tekst zichtbaar. Met **De tekst verkleinen** daalt de grootte tot hij past. Maak de tekst kleiner, of het vak groter, tot alles zichtbaar is.

## Een lettertype ziet er op een andere pc anders uit

De andere pc heeft dat lettertype niet. De lettertypen die bij LabelAxis horen (de vijf Arabische en die van het programma zelf) zijn er altijd, en een lettertype dat u met `Lettertype toevoegen…` hebt toegevoegd, reist mee in het project en wordt toegevoegd als het project daar wordt geopend. Installeer een lettertype dat alleen in Windows is geïnstalleerd ook op die pc, of voeg het bestand toe met `Lettertype toevoegen…` zodat het project het meeneemt. De controle vooraf waarschuwt voor ontbrekende lettertypen.

## Afbeeldingen worden wazig afgedrukt

De afbeelding heeft te weinig pixels voor haar afdrukgrootte; Afbeeldingsinstellingen toont de DPI. Gebruik een grotere afbeelding, of druk haar kleiner af.

## Een barcode scant niet

Maak hem groter, houd donkere strepen op een lichte achtergrond, laat een vrije marge eromheen, en druk op thermische printers donkerder af. Controleer of de gegevens bij het barcodetype passen ([[barcodes|Barcodes]]).

## Printer staat er niet bij

Installeer hem in Windows, of voeg een netwerkprinter toe onder `Wi-Fi-printers…` ([[wifi-printers|Wi-Fi-printers]]). Thermische en Bluetooth-printers: zie [[thermal|Thermische etiketprinters]].

## "Windows heeft uw pc beschermd" bij het installeren

Windows toont dit bij programma's van een uitgever die het nog niet kent. Kies **Meer informatie** en daarna **Toch uitvoeren**.

## Er wordt geen update aangeboden

- Controleer of er een **updateadres** is ingesteld onder `Bestand › Instellingen… › Updates`, of dat deze versie met een adres is gebouwd, en of de pc het kan bereiken.
- Probeer `Help › Op updates controleren…`: het bericht zegt wat er niet werkte.
- Een draagbare kopie werkt zichzelf niet bij; zie [[files|Projecten, opslaan en instellingen]].

## Er ging iets anders mis

LabelAxis bewaart uw werk veilig in een herstelkopie en schrijft details naar een logbestand (in `%LOCALAPPDATA%\LabelAxis\logs`). Start het programma opnieuw en accepteer het aanbod om uw werk te herstellen.
