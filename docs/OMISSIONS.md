# Deliberately omitted or simplified

Per the spec's "avoid unnecessary complexity" rule, the following were left out or
scoped down so the core (**Design → Barcode → Data → Formtec sheet → Print**)
stays exceptionally reliable. Each could be revisited later without disturbing the
architecture.

## Excluded outright

| Feature | Why | Reconsider when |
|---|---|---|
| **Microsoft Publisher `.pub` import** | Excluded by the spec and by us — unreliable conversion, layout drift, heavy dependency. | Never (spec §11). |
| **Database data sources** (SQLite, Access, SQL Server, MySQL, PostgreSQL, ODBC, REST) | v1 permits skipping (spec §23). `IDataSource`-shaped seams exist; adding one is a new reader class. | A concrete customer workflow needs live DB merge. |
| **RFID / Zebra ZPL / TSC TSPL / ERP / cloud templates** | Future possibilities only (spec §39). | A device/integration is actually in scope. |
| **Arbitrary SVG import** | Full SVG is a rendering engine of its own. | A constrained subset (paths + basic shapes) is worth the risk. |
| **Full PDF import / editing** | Out of scope (spec §11). | — |

## Simplified

| Area | What ships in v1 | Not in v1 |
|---|---|---|
| **PDF import** | Not wired into the UI. The renderer/exporters are vector-only. | Placing a PDF page as a raster background — planned, needs a PDF rasteriser dependency (e.g. PDFium) that we chose not to add yet. |
| **Rich text** | Per-run font / size / bold / italic / underline / strike / colour / letter-spacing; paragraph alignment, line spacing, shrink-to-fit, word wrap. | RTF/DrawingML import, tab stops, bulleted/numbered lists, inline images, per-character kerning tables. |
| **Tables** | Fixed rows × columns, per-cell tokenised text, uniform border, optional header fill, column/row weight sizing. | Merged/spanning cells, per-cell borders, per-cell rotation, nested tables. |
| **Polygon tool** | Regular N-gon (3–64) and N-point star via one ratio. | Freeform / bézier path editing. |
| **Image editing** | Import, resize, crop (normalised rect), rotate (via object rotation), flip H/V, opacity, brightness, contrast, grayscale, replace. | Filters, curves, background removal, multi-layer image compositing. |
| **GS1-128 AI parsing** | `(nn)` groups are recognised; FNC1 is inserted at the start and after variable-length AIs (common fixed-length AI table). HRI shows the digits without the parentheses. | Full GS1 General Specifications AI validation, checksum-per-AI, decimal-point AIs, composite components. |
| **GS1 DataBar / UPC-E / ISBN edge cases** | Offered only if the bundled ZXing build encodes them **and** a startup self-test passes; otherwise hidden, never a broken menu item. | Guaranteed support for every DataBar variant. |
| **Expression engine** | Whitelisted pure functions over string / number / date / bool; `+ - * / %`, comparisons, `&` concat, `IF`. Hard limits on input length, AST size, recursion depth and produced string length. | Variable assignment, loops, user-defined functions, any I/O or reflection. **By design** (spec §10, §28). |
| **Printer calibration** | Per-printer X/Y offset in millimetres + fine X/Y scale, stored in `%APPDATA%\LabelAxis\calibration.json`. Wizard prints a crosshair target. | Automatic closed-loop calibration (would need a scanner), per-tray or per-media calibration. |
| **Undo granularity** | Command stack with drag/keystroke coalescing (600 ms window), capacity 200, cleared on load. | Unbounded history, selective undo, branching history. |
| **Multi-page projects** | Master label + dataset is the primary model. Standalone named "pages" exist and are searchable; per-record overrides are a sparse opt-in dictionary. | A full page-per-label document mode for 10k records (explicitly rejected for performance — spec §27, §37). |
| **Localization** | Invariant culture for files; current culture for on-screen number formatting. UI strings are English only. | Translated UI. |

## Notes on library choices

* **Code 128 is first-party** (not ZXing) because the spec makes it the top
  barcode priority and owning the encoder lets us guarantee automatic A/B/C
  optimisation and GS1 FNC1 behaviour. It is covered by a ZXing **round-trip
  decode** test over a broad corpus.
* **ZXing.Net** does the standards-compliant encoding for every other symbology;
  we only re-express its bit matrix as vector rectangles.
* **PNG/JPEG raster export** uses WPF `RenderTargetBitmap` and therefore lives in
  the Windows app, not the portable `LabelMaker.Export`. PDF and SVG export are
  portable.
