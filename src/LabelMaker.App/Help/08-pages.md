---
id: pages
title: Pages: several labels in one project
category: Pages
order: 1
keywords: page, pages, master label, add pages, blank pages, copies, how many, duplicate, rename, reorder, sort, reverse, remove, select several, ctrl click, shift click, multiple pages, print selected pages, numbered copies, count up, barcode sequence, serial, pages from records, make pages, remove from all pages, delete everywhere, scroll, apply positions, apply format, page format, line up, same place, same size, apply formatting, other pages, add missing
---
# Pages: several labels in one project

A project can hold several different label designs, called **pages**, listed under `Pages` on the left and numbered 1, 2, 3… The first page is also the **master label**: the one repeated across the sheet when you print "Fill the sheet".

## Work with pages

- `+` opens **Add pages**: choose **Blank pages** or **Copies of a page** (pick the page in the list; the page on screen is chosen to begin with), and **how many**. The new pages go right after the page on screen. Copies can also count their barcodes up; see "Numbered copies" below.
- The duplicate button copies the selected page or pages; each copy goes right after its page.
- Double-click a page (or press `F2`) to rename it in place.
- `Ctrl+↑` / `Ctrl+↓`, or dragging, change the order. Right-click a page for **Sort by name, A to Z**, **Sort by name, Z to A** and **Reverse order**, or use the sort button above the list.
- A long list of pages scrolls with the mouse wheel or the up and down arrows at the ends of its scroll bar. A new or duplicated page is brought into view by itself.
- The remove button deletes the selected page or pages, after asking. The first page can be removed too while other pages exist; the next page then becomes page 1. At least one page always stays.
- To find a page by its name, press `Ctrl+F` and type part of it: page names are searched along with everything else (**Page names** under Look in). Pick a match to open that page; **Replace** renames it.

> Tip: Make one label, then **duplicate** it for each product and change only the words. Duplicated pages keep their objects' names, which the tool below uses to find "the same" object on each page.

## Select several pages

In the page list, `Ctrl`+click picks pages one by one and `Shift`+click picks every page between the one you clicked last and this one. With several selected:

- **Duplicate** copies each of them and **Remove** removes them all, each in one step that `Ctrl+Z` takes back.
- Right-click › **Print selected pages…** opens Print set to just those pages, several to a sheet.

## Numbered copies

Right-click a page › **Numbered copies…** (or `+` › Copies of a page › **Count the barcodes up on each copy**) makes copies of that page whose barcodes count up by one, page after page: `BOX-0007` becomes `BOX-0008`, `BOX-0009`, and so on. Leading zeros are kept, and the check digit of EAN, UPC, ITF-14 and ISBN barcodes is worked out again. Each copy is named after its new number. Barcodes filled in from data (`{Barcode}`) or without a number stay as they are.

## A page for each record

To turn imported records (Excel, CSV or a database) into pages, one page per record with its values written in, see [[data|Data and variable labels]] › "Make a page for each record".

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
