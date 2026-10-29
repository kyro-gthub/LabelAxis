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
- Undo history: a History tab beside Properties (also Edit > Undo history)
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
- About 3,540 more built-in label sheets (3,716 in all): Avery (A4/A5 and US Letter), Avery
  Zweckform, HERMA, APLI, Agipa, Decadry, Sigel, OnlineLabels, TANEX, 3M (with Post-it), Brother
  and DYMO, from the measurements each maker supplies for Microsoft Word's label list (corner
  radius and round labels included). One catalogue file per maker in `templates/sheets`, made by
  `build\import-word-catalogues.ps1`; layouts Word places label by label, business cards, cards,
  posters and binder tabs are left out. Avery L7160 now has Avery's own figures instead of an
  estimate. Each maker gets an entry in the Label manager.
- Setup is LabelAxis's own window (`src/LabelAxis.Setup`): Welcome, Options (just for you or
  everyone, the folder, the .labelproj file type, a desktop shortcut), Installing (with .NET 10
  fetched from Microsoft when the small installer needs it, and each file as it is copied) and
  Finish, in all ten languages, right to left where needed, light or dark like Windows. An
  installed copy is found and updated in place. It runs on the .NET Framework that comes with
  Windows and carries the Inno Setup installer, which still does the installing; a silent install,
  as the updater starts it, goes straight to that installer. Inno Setup's own windows (the
  uninstaller, the updater's progress) have the Windows 11 style and new pictures.

### Changed

- Apply formatting to other pages (Edit menu, page menu) is one tool in place of
  Apply page format and Apply text formatting. It can also give one object's look to every
  object of its kind; the button under Text Settings opens it set to do that for a text box.
- The Help menu opens the help guide, which holds Your first label, Keyboard shortcuts and
  Troubleshooting; their separate menu items are gone.
- The right panel has Properties and History only. The Layers tab repeated the left panel's
  Objects on page list, and the Page tab the Label manager and Apply formatting to other pages.
- Label size is part of the Label manager (File menu and the page menu): type a size under
  Label size, or pick a sheet and use it. The separate Label size window is gone.
- Data > Add data field lists the columns to choose from, as does Add field to label in Records.
- A column of barcodes (EAN, UPC, ITF-14 or ISBN numbers with right check digits, or a column named
  Barcode) goes on the label as a real barcode: a window asks which type, with the likely one
  chosen and a count of values that type cannot hold, or offers text instead.

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
- Add field to label (Records) always added the first column, whichever one was wanted.
- Data fields added one after another all landed in the same spot in the middle of the label, and a
  long value grew past its edge; each now goes in a free row, in a size that suits the label, and a
  long value shrinks to fit its box.
- A column whose name has brackets, such as "Price (USD)", or letters other than Latin ones, showed
  nothing on the label; it is filled in now, and making pages from records finds it too.
- The Records window's rows were dark text on a dark background in the dark themes.
- When LabelAxis opened, the label stayed fitted to the window's first, smaller size and sat small
  in the middle of the canvas; it now fills the canvas until you zoom or scroll yourself.
- The title and barcode a new project starts with were the same size on every label, so on a
  small one (25 × 10 mm) the title ran onto a second line over the barcode and the barcode's
  digits squeezed its bars to a sliver. Both are now sized for the label: the title on one line,
  shrinking to fit, and the barcode below it (all bars on a very low label).
- Print settings changed in the Print and thermal print windows (printer, paper, margins,
  copies, scale, thermal settings) were not marked as a change, so they were not saved and
  were lost when the project was opened again.
- Publisher imports use Publisher's own margins and gaps for where its labels sit on the sheet,
  and pick the built-in sheet laid out the same way when Publisher does not name one.
- Excel files with a picture whose name is longer than 31 characters failed to import
  ("picture names cannot be more than 31 characters"); their records now import.
- A duplicated or added page shows in the page list straight away.
- Editing text on the label ended (kept as typed) as soon as the mouse moved out of the text box:
  passing over the canvas gave it the keyboard, taking it from the editor. The canvas no longer
  takes the keyboard while text is being typed there or in a side panel box.
- A quiet zone of 0 put no bars on the edges of the barcode's box: Code 128 kept at least 10
  modules of white space, EAN and UPC 9, other linear types 10, QR 4. The quiet zone is now used
  as set; ready-made designs start with the amount each type's standard asks for.
- Print preview drew the printer margin line through the last row of an A4 sheet when the printer
  had no A4 paper (a fax driver falls back to Letter, 17.6 mm shorter): the printer's border is now
  taken from the paper it uses and applied to the sheet, and the status bar names that paper.
  A measurement tag wider than the room beside the sheet was cut off; the room grows to fit it.

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
