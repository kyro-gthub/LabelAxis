---
id: pages
title: Pages: several labels in one project
category: Pages
order: 1
keywords: page, pages, master label, duplicate, rename, reorder, remove, remove from all pages, delete everywhere, scroll, apply positions, apply format, page format, line up, same place, same size, apply formatting, other pages, add missing
---
# Pages: several labels in one project

A project can hold several different label designs, called **pages**, listed under `Pages` on the left and numbered 1, 2, 3… The first page is also the **master label**: the one repeated across the sheet when you print "Fill the sheet".

## Work with pages

- `+` adds an empty page; the duplicate button copies the page on screen.
- Double-click a page (or press `F2`) to rename it in place.
- `Ctrl+↑` / `Ctrl+↓`, or dragging, change the order.
- A long list of pages scrolls with the mouse wheel or the up and down arrows at the ends of its scroll bar.
- The remove button deletes the page on screen. The first page can be removed too while other pages exist; the next page then becomes page 1.
- To find a page by its name, press `Ctrl+F` and type part of it: page names are searched along with everything else (**Page names** under Look in). Pick a match to open that page; **Replace** renames it.

> Tip: Make one label, then **duplicate** it for each product and change only the words. Duplicated pages keep their objects' names, which the tool below uses to find "the same" object on each page.

## Give other pages this page's format

`Edit > Apply page format to other pages…` (also when you right-click a page, and on the `Page` tab) makes the other pages look like the page on screen. Each text box, barcode, picture, shape and table there takes the place, size and look of its match on this page, and **keeps its own words, barcode value and picture**.

- **Copy from:** every object on this page, or only the selected objects.
- **What to copy:**
  - **Position**, **size**, and **rotation and slant**.
  - **Look and settings:** font and colours, barcode type, bar height and the digits under the bars, how a picture fits and its border, a shape's outline and fill, a table's lines and text. For text you can pick which parts: font, text size, bold/italic/underline, colours, alignment, line and letter spacing.
  - **Add the objects a page doesn't have:** an object on this page with no match on another page is copied there, with its words and picture.
- **Which objects:** text, barcodes and QR codes, images, shapes and tables, each on or off.
- **Match by:** its **name, or else its order** (recommended); its **name** only; or its **order** only (the first text box to the first text box, the second barcode to the second barcode). A QR code is only ever matched with a QR code, and a line barcode with a line barcode.
- **Which pages:** tick them; each says how many objects would change, and how many would be added.

A barcode type that cannot hold a page's value (EAN-13 for the letters "ABC") is not forced on that page; the barcode takes the rest of the look.

## Remove an object from all pages

To take something off every label at once, such as an old barcode, a logo or a text box called "Offer", select it on one page and choose `Edit > Remove from all pages…` (also when you right-click it on the label or in `Layers`).

- **Also remove it from this page** removes the selected object itself too; untick it to keep it here.
- **Match by** finds the same object on the other pages: its **name, or else its order** (recommended), its **name** only, or its **order** only.
- **Which pages:** tick them; each says how many objects would go.

Locked objects are never removed.

## Same text formatting on other pages

`Edit > Apply text formatting to other pages…` copies the selected text box's font, size, style and colour to other pages. See [[text|Text]].

Each of these tools is a single undo step: `Ctrl+Z` puts every page back.

## Printing pages

To print pages several to a sheet, the page on screen only, or a chosen few with their own number of copies, see [[printing|Printing]].
