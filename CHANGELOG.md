# Changelog

Notable changes to LabelAxis. Versions follow [Semantic Versioning](https://semver.org/).
The full notes of each release, in all ten interface languages, are in
[`build/release-notes`](build/release-notes) and on the Releases page.

## [1.1.2] - 2026-10-25

### Added

- Several pages can be selected in the page list with Ctrl+click and Shift+click; Duplicate,
  Remove and the page menu's new Print selected pages… act on all of them, each in one undo step.
- Add pages (the + button) asks for blank pages or copies of a chosen page, and how many;
  copies can count their barcodes up.
- Undo history: a History tab beside Properties, Layers and Page (also Edit > Undo history)
  lists every step; clicking one undoes or redoes straight to it.
- Curve in the Lines menu: a curved line whose dots can be dragged to change the bend.
- The help guide covers adding and selecting pages, numbered copies, pages from records, the
  undo history, curves, barcode scanners, print settings kept with the project, Publisher label
  sheets, closing other tabs and updating a PC without internet, in every language.
- Pages from records: one page for each record of the imported data (all of them, or those
  chosen in Records…), made from the label on screen with the record's columns written into
  its text, barcodes and table cells; other placeholders (dates, counters) stay live. From the
  Data tab (Make pages…), the Records window (Make pages) and the page list's menu.
- Numbered copies (page list menu): copies of a page whose barcodes count up page by page,
  keeping leading zeros and working out EAN, UPC, UPC-E, ITF-14 and ISBN check digits again.
  GS1 data counts up its serial number (21), else its SSCC or a numbered batch; dates and GTINs
  stay as they are.
- The barcode check takes USB and Bluetooth barcode scanners: what a scanner types is compared
  with every barcode in the project (every page, and every record when a barcode comes from
  data), with a running count of matches, so a printed batch can be checked. Scanners give no grade.

### Changed

- Apply formatting to other pages (Edit menu, page menu, Page tab) is one tool in place of
  Apply page format and Apply text formatting. It can also give one object's look to every
  object of its kind; the button under Text Settings opens it set to do that for a text box.
- The Help menu opens the help guide, which holds Your first label, Keyboard shortcuts and
  Troubleshooting; their separate menu items are gone.

### Fixed

- Messages saying what an action did (pages made, pages removed, the label sheet chosen for a
  Publisher file, problems) were not shown anywhere; they show in the status bar again.
- Saving dropped the pictures of deleted image objects from the open project, so undoing the
  delete afterwards brought back an empty picture box.
- After undo or redo, an object no longer on the label stayed selected and could still be changed.
- A range of records chosen for printing also limited which pages printed; ranges now apply only
  to the kind of printing they were chosen for.
- Number boxes did not read digits typed on a Persian, Kurdish, Arabic or Hindi keyboard.
- Some status messages were only partly translated.
- Print settings changed in the Print and thermal print windows (printer, paper, margins,
  copies, scale, thermal settings) were not marked as a change, so they were not saved and
  were lost when the project was opened again.
- Publisher imports use Publisher's own margins and gaps for where its labels sit on the sheet.
- Excel files with a picture whose name is longer than 31 characters failed to import
  ("picture names cannot be more than 31 characters"); their records now import.
- A duplicated or added page shows in the page list straight away.

## [1.1.1] - 2026-10-20

### Fixed

- Microsoft Publisher label files (one label per page, printed "multiple pages per sheet")
  print several labels to a sheet again. Opening one now puts its pages on the label sheet
  with labels that size: the label product Publisher's page setup names, otherwise the sheet
  last used, otherwise the closest in size. Before, each label became a one-up "Custom" page
  the size of the label, so every label printed on a sheet of its own.
- Projects already imported that way are offered their label sheet when they open; the
  switch is one step that Undo takes back. Declining is remembered per file
  (`AppSettings.PublisherPagesKept`), so the question is not asked again.
- Label manager's "Use this sheet" resizes the project's standalone pages with the master
  label. They kept the old size, so in page printing each page spilled into the next label.
- A start position beyond the sheet's last label printed a blank first sheet; it now starts at
  the first label, and Print settings caps the number at the sheet's label count.
- Filling the rest of a partly used sheet without data laid out a whole sheet's worth of labels
  from the start position, so the rest printed on an extra second sheet.
- Records chosen for printing (the list and the first-to-last range) follow their records
  when rows are added to or deleted from the data table (and on Undo); adding a row above
  them used to make other records print.

## [1.1.0] - 2026-10-17

### Added

- Thermal printing can use the printer's own fonts and barcodes ("Send labels as › Printer
  fonts and barcodes"). Each design's fixed parts are stored on the printer once (ZPL `~DG` /
  TSPL `DOWNLOAD`); each label then sends only its changing text and barcodes as printer
  commands (`^A0`, `^BC`, `^BQ`… / `BLOCK`, `BARCODE`, `QRCODE`…). Code 128 is given the exact
  code values LabelAxis encodes. What the printer cannot draw goes as a picture of its area.
  Sending each label as one picture stays the default.
- Settings › Auto-save & recovery can switch off the `.bak` backup kept when saving.

### Fixed

- The Help guide and other separate windows no longer disappear behind a maximised main
  window: moving the mouse over the label no longer brings the main window forward.
- Strikethrough runs through the middle of the text (an old price on a sale tag), in the
  same place on screen, on paper, in PDF and in SVG.
- Underline and strikethrough in exported PDFs were three times too thick.
- The "Sale tag – old / new price" design uses real strikethrough for the old price, so the
  line grows with a longer price.
- Labels that touch show a gap of 0, not "-0", in Print preview and New Project.

### Security

- `System.Security.Cryptography.Pkcs` (brought in by PDFsharp and Microsoft.Data.SqlClient)
  pinned to 10.0.13 for CVE-2026-84008 and CVE-2026-70162.

## [1.0.0] - 2026-10-10

First public release.

### Added

- Label designer: text with mixed formatting, images, shapes and custom curved shapes,
  tables, date and record fields, rulers, guides, snapping and full undo.
- Barcodes: Code 128 / GS1-128 with its own encoder, every common 1D and 2D symbology, a GS1
  barcode builder, a barcode image generator, and grading of printed barcodes from A to F.
- 178 Formtec and Avery label sheets, custom sizes, 66 ready-made designs and your own
  designs, with a calibration wizard for each printer.
- Printing to Windows printers, thermal printers (ZPL / TSPL through Windows, the network, a
  COM port or Bluetooth) and Wi-Fi printers (IPP), with a print history to reprint from.
- Records from CSV, Excel, SQLite, SQL Server, MySQL, PostgreSQL, Access and ODBC, with
  formulas, number sequences, filters and sorting.
- Print requests from other programs through a watch folder or a local web link.
- Export to vector PDF, SVG, PNG and JPEG, with a preflight check first.
- Ten interface languages (right to left for Arabic, Kurdish and Persian), each with a
  searchable help guide.
- Eight themes and your own theme; automatic updates from GitHub Releases.
- A small installer, an offline installer and a portable zip.

[1.1.0]: ../../releases/tag/v1.1.0
[1.0.0]: ../../releases/tag/v1.0.0
