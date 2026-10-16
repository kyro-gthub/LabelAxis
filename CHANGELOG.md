# Changelog

Notable changes to LabelAxis. Versions follow [Semantic Versioning](https://semver.org/).
The full notes of each release, in all ten interface languages, are in
[`build/release-notes`](build/release-notes) and on the Releases page.

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
