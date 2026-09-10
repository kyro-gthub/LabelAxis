# Build order & status

The application was built in the phases below (spec §42). Status reflects this
repository.

| Phase | Scope | Status |
|---|---|---|
| **1 — Core designer** | app shell, document/object model, canvas, text, images, shapes, properties, object/layer list, save/open, undo/redo | **Done.** WPF shell + `DesignerCanvas`, full `LabelObject` model, `CommandStack`, `.labelproj` round-trip. |
| **2 — Formtec & printing** | template schema, template manager, sheet layout, print preview, printing, printer calibration | **Done.** `TemplateStore` (14 built-ins + user JSON/CSV), `TemplateGeometry`, `SheetLayoutEngine`, `LabelSheetPaginator`, `PrintCoordinateMapper`, Calibration Wizard. |
| **3 — Barcode system** | Code 128, QR, main standards, barcode object, barcode image export, validation | **Done.** First-party Code 128/GS1-128 + ZXing wrapper, `BarcodeEngine`/`BarcodeValidator`/`BarcodeLayout`, standalone SVG/PDF export, round-trip decode tests. |
| **4 — Variable data** | CSV, XLSX, data preview, data binding, sequence generator, bulk labels, master-label architecture | **Done.** `DataImportService`, `SequenceGenerator`, `ExpressionEngine`/`VariableResolver`, `BulkRenderPlan`, Bulk Barcode Generator. Data-preview UI is minimal (import + first-N rows via the report dialog); column mapping is programmatic via `DataConnectionModel`. |
| **5 — Search & validation** | barcode/page/data search, duplicate detection, preflight, large-dataset optimisation | **Done.** `ProjectSearchIndex` (+ `FindRecords` fast path), `DuplicateDetector`, `PreflightChecker`. Streaming enumeration keeps 10k-record jobs flat in memory. |
| **6 — Export & refinement** | PDF export, high-res image export, reusable templates, recovery, usability | **Mostly done.** Vector PDF (whole sheet / per label / per record), SVG, PNG/JPEG at 300/600/custom DPI, `AutoSaveService` crash recovery. "Save design as reusable template" is stored as a `.labelproj` in the user folder; a dedicated template gallery UI is a follow-up. |
| **7 — Release** | automated tests, performance, packaging, Windows installer, docs | **Done for engine/CLI; app packaging scripted.** 130 xUnit tests, Inno Setup script, build scripts, this documentation. Broader UI automation tests are a follow-up. |

## Known follow-ups (not blocking core reliability)

* Richer **data-import wizard** (interactive worksheet / header-row / column-map
  UI; today those are `DataConnectionModel` fields set in code or via defaults).
* **Template gallery** with thumbnails for saved label designs.
* **PDF-as-background** import (needs a rasteriser dependency — see
  [`OMISSIONS.md`](OMISSIONS.md)).
* **Actual-size preview** currently uses a fixed screen-DPI assumption from
  settings; a "hold a ruler to the screen" calibration step would improve it.
* Marquee (rubber-band) multi-select and a rotate handle on the canvas (rotation
  is editable in the property grid today).
* GS1 Application Identifier full validation.
