# LabelAxis — Architecture & Design

> A stable, Windows-first desktop label design and printing application focused on
> Formtec templates, barcodes (Code 128 first), bulk / variable-data printing,
> fast search, and **physically accurate** output.

This document is the answer to the twelve "first step" questions in the
specification. Implementation follows the phase plan in
[`ROADMAP.md`](ROADMAP.md) and the omissions list in
[`OMISSIONS.md`](OMISSIONS.md).

---

## 1. Technology stack

| Concern | Choice |
|---|---|
| Language / runtime | **C# 13 / .NET 10** |
| Desktop UI | **WPF** (`net10.0-windows`, `UseWPF=true`) |
| Core engine | **`LabelMaker.Core`** — plain `net10.0`, **no UI or Windows dependency** |
| Barcodes (2D + exotic 1D) | **ZXing.Net** (encoding only → bit matrix → vector redraw) |
| Barcodes (Code 128 / GS1-128) | **first-party encoder** in `LabelMaker.Core.Barcodes` (deterministic tables, vector output) |
| CSV | **CsvHelper** |
| XLSX | **ClosedXML** (read-only usage) |
| PDF export | **PdfSharp 6** (vector text / shapes / barcodes) |
| SVG export | first-party writer (small, deterministic) |
| PNG / JPEG export | WPF `RenderTargetBitmap` (Windows, exact device pixels) |
| Printing | WPF `System.Printing` + `DocumentPaginator` (XPS pipeline) |
| Project container | ZIP (`System.IO.Compression`) + `System.Text.Json` |
| Tests | **xUnit** + **FluentAssertions** + **coverlet** |
| Installer | **Inno Setup** script (`installer/LabelMaker.iss`) |

### Why this stack for a Windows label application

* **Printing accuracy is the top requirement.** WPF's entire rendering model is
  resolution-independent: coordinates are *device-independent units* (1 DIU =
  1/96 inch) and the same visual tree renders to screen, to `RenderTargetBitmap`,
  and to the printer/XPS pipeline through the same vector rasteriser. There is no
  HTML/Chromium layer to introduce scaling surprises, and no "fit to page"
  behaviour unless we ask for it. We convert **millimetres → DIU** with one
  audited function and never touch screen DPI for physical math.
* **`System.Printing`** exposes real `PrintQueue` / `PrintTicket` objects:
  hardware margins, `PageMediaSize`, per-printer capabilities, and a
  `DocumentPaginator` that lets us place marks at exact physical offsets. This is
  what makes the Printer Calibration Wizard possible.
* **.NET 10 is native, offline-friendly, and self-contained-publishable.** The
  installer ships a trimmed self-contained build; the user never installs a
  runtime and never needs admin rights.
* **The core is deliberately UI-free.** `LabelMaker.Core` targets plain `net10.0`
  and references nothing from WPF/Windows. Every rule the spec cares about —
  geometry, unit conversion, barcode bytes, sequence math, CSV/XLSX parsing,
  serialization, search, preflight, print coordinate math — lives there and is
  unit-tested headlessly. A future macOS UI (Avalonia) reuses `Core` and
  `Export` untouched; only `LabelMaker.App` is Windows-specific.
* **Mature libraries for hard standards.** ZXing.Net is the reference .NET
  implementation for QR, Data Matrix, PDF417, Aztec and the linear symbologies.
  We use it for *encoding correctness* and redraw the module matrix ourselves as
  crisp vector rectangles so output stays resolution-independent. Code 128 is the
  one symbology we encode in-house because (a) the spec makes it the highest
  barcode priority, (b) its code tables are small, closed and fully specified,
  and (c) owning it lets us guarantee automatic A/B/C set optimisation and
  GS1-128 FNC1 handling exactly the way the spec describes. It is covered by a
  round-trip test that decodes every generated symbol with ZXing.

### What we are cross-platform-ready for, and what we are not

`Core` + `Export` are portable today. `App` (WPF), PNG/JPEG raster export and the
Windows print server are Windows-only by design. We do **not** compromise the
print pipeline to abstract the last 5%.

---

## 2. Document / object model

Namespace `LabelMaker.Core.Model`. Everything is a plain serializable C# object;
nothing is flattened to an image while editing (spec §30).

```
ProjectModel
├─ ProjectMetadata            (id, title, author, created/modified, appVersion, schemaVersion)
├─ TemplateRef                (Formtec template id + embedded copy for portability)
├─ MasterLabel : LabelDocument   ← the single design used for every record (spec §37)
├─ Pages : List<LabelPage>       ← standalone / one-off designs (each is a LabelDocument too)
├─ DataConnections : List<DataConnectionModel>
├─ Sequences : List<SequenceDefinition>
├─ Variables : List<CustomVariable>
├─ PrintSettings              (last printer, copies, start position, skip mask)
└─ RecordOverrides : Dict<int, List<ObjectOverride>>   ← rare per-record tweaks (spec §37)

LabelDocument
├─ Id, Name, Description, Tags[]
├─ Size (Measure width/height)  ← physical, taken from template label size
├─ Background : BackgroundSpec   (image, fit mode, opacity, brightness, contrast, rotation, flip, locked)
└─ Objects : List<LabelObject>   (z-ordered; index 0 = back)

LabelObject (abstract)
├─ Id (stable GUID)              ├─ Name
├─ Bounds : RectMeasure          (X, Y, Width, Height in document mm)
├─ Rotation (degrees)            ├─ Opacity
├─ Locked, Visible               ├─ GroupId (nullable)
└─ subclasses:
   TextObject        (runs: rich text model, font, size, styles, alignment, autofit, wrap, direction)
   ImageObject       (asset ref, fit mode, adjustments, flip)
   ShapeObject       (kind: Rect|RoundRect|Ellipse|Line|Arrow|Polygon; fill, stroke, dash, cornerRadius)
   BarcodeObject     (BarcodeSpec: symbology, value-or-expression, module width, height, quiet zone,
                      human-readable text config, check digit, GS1 flag, EC level, encoding, rotation)
   TableObject       (rows/cols, cell text, borders)  — see OMISSIONS for scope
   FieldObject       (a text object whose content is a single {expression}; kept distinct for search)
```

### Rich text

`TextObject.Content` is a lightweight model — `List<Paragraph>` → `List<TextRun>`
each with its own font overrides. This is enough for mixed bold/size/colour
inside one box without embedding a full flow-document engine. RTF/DrawingML text
import is **out of scope** (see OMISSIONS).

### Variable fields inside text

Text runs may contain `{Expression}` tokens. At render time the
`VariableResolver` replaces them against the current `DataRecord` +
`CustomVariable`s + built-ins (`{CurrentDate}`, `{PageNumber}`, `{Serial}`…).
The stored run text is never mutated — binding is non-destructive.

### Undo/redo

`LabelMaker.Core.Editing` — a `CommandStack` of `IUndoableCommand`
(`Do()` / `Undo()`). Every mutation from the UI goes through a command
(`AddObjectCommand`, `TransformObjectsCommand`, `SetPropertyCommand<T>`,
`ReorderCommand`, `EditTextCommand`, `SetBackgroundCommand`,
`BindObjectCommand`, …). Text editing coalesces keystrokes within a 600 ms / same
object window. The stack is capped (default 200) and cleared on project load.

---

## 3. Physical measurement system

Namespace `LabelMaker.Core.Units`. **Four distinct spaces, never mixed
implicitly** (spec §41):

| Space | Type | Definition |
|---|---|---|
| Physical | `Measure` (immutable) | a length + `Unit` (`Mm, Cm, Inch, Point, Pixel`). Canonical store is **millimetres** as `double`. |
| Logical / document | `double` mm | the document works in mm; objects' `Bounds` are mm. |
| Screen | `double` DIU | `Mm * 96.0 / 25.4`, then multiplied by the canvas zoom. Used **only** for on-screen drawing. |
| Device / printer | `double` px | `Mm * dpi / 25.4` where `dpi` comes from the `PrintTicket` / export request, never from the screen. |

* `UnitConverter` has exactly one physical constant: `MmPerInch = 25.4`.
  `Point = 1/72 inch`. `Pixel` is UI-only and requires an explicit DPI to become
  physical; storing a `Measure` in `Pixel` without DPI throws.
* `Measure` parses and formats `"40 mm"`, `"1.5 in"`, `"12pt"` for the property
  grid. Formatting is culture-invariant for files, culture-aware for the UI.
* Physical dimensions of the document/export are **derived from the template and
  the requested DPI only**. Screen DPI affects nothing that is saved or printed.
  Test suite `PhysicalMeasurementTests` asserts a 40 mm × 20 mm label is
  40 mm × 20 mm in the exported PDF (± 0.05 mm) and in the print paginator's
  page geometry.

---

## 4. Formtec template schema

Namespace `LabelMaker.Core.Formtec`. Templates are **data**, loaded from JSON
files under `templates/formtec/` and from the user template folder — never
hard-coded in logic (spec §3, §33). Full schema in
[`TEMPLATE-SCHEMA.md`](TEMPLATE-SCHEMA.md). Summary:

```jsonc
{
  "schema": "labelmaker.template/1",
  "id": "formtec-3100",            // stable key
  "brand": "Formtec",             // Avery / others later — same schema
  "productCode": "3100",
  "name": "Formtec 3100 — 2-up shipping",
  "categories": ["Shipping", "Address"],
  "page": { "size": "A4", "widthMm": 210.0, "heightMm": 297.0, "orientation": "Portrait" },
  "printableMarginMm": { "top": 4.0, "right": 4.0, "bottom": 4.0, "left": 4.0 },
  "grid": {
    "columns": 2, "rows": 7,
    "labelWidthMm": 99.1, "labelHeightMm": 38.1,
    "marginTopMm": 15.15, "marginLeftMm": 4.65,
    "hGapMm": 2.5, "vGapMm": 0.0,
    "cornerRadiusMm": 2.0,
    "labelShape": "RoundRect"      // Rect | RoundRect | Ellipse
  },
  "fillOrder": "RowMajor",        // RowMajor | ColumnMajor
  "notes": "…", "source": "manufacturer datasheet"
}
```

* `LabelTemplate` (record) mirrors this. `TemplateGeometry` computes, for label
  index *n* (0-based) or (row, col): the **label rectangle in page mm**, honoring
  `fillOrder`. One function, one test matrix (`FormtecGeometryTests`) covering
  every shipped template's corner labels.
* Validation: grid must fit inside the page; label + gaps + margins reconciled;
  warns if any label crosses the printable margin.
* `TemplateStore` — loads built-ins (embedded + `templates/formtec/`), merges the
  user folder (`%APPDATA%\LabelAxis\templates`), supports search by product
  code, by dimensions (± tolerance), by category, plus duplicate / edit /
  import / export (JSON and CSV). Adding a template is dropping a `.json` file.

---

## 5. Barcode architecture

Namespace `LabelMaker.Core.Barcodes`. Two-layer design:

```
BarcodeRequest         (symbology, data, options)         ← validated input
        │
        ▼
IBarcodeEncoder  ──►  BarcodeSymbol                       ← resolution-INDEPENDENT result
   ├─ Code128Encoder (first-party: sets A/B/C, auto-optimise, GS1 FNC1, check digit)
   └─ ZXingEncoder   (QR, DataMatrix, PDF417, Aztec, Code39/93, EAN/UPC, ITF, Codabar, GS1 DataBar*)
        │
        ▼
BarcodeSymbol
   ├─ 1D:  Bars : IReadOnlyList<BarModule>   (xModules, widthModules, isBar)  + quietZoneModules
   └─ 2D:  Matrix : bool[,]                  + quietZoneModules
        │
        ▼
BarcodeLayout.Place(symbol, targetRectMm, moduleWidthMm?, rotation, hriConfig)
        │
        ▼   (drawn by ANY IDrawingSurface — screen, PDF, PNG, print — as crisp rects)
```

* **Nothing is rasterised in the engine.** A symbol is bars/modules; the surface
  fills exact rectangles at whatever DPI it runs at → always print-quality.
* `BarcodeValidator` runs before render and before print: charset legality per
  symbology, EAN/UPC/ITF length + check digit, GS1 AI structure (basic),
  QR/DataMatrix capacity vs. EC level. Returns structured errors → feeds
  preflight (spec §26).
* **Verification:** `Code128RoundTripTests` encodes a large corpus (ASCII,
  digits, GS1) with the first-party encoder, renders to a bitmap, and decodes
  with ZXing — the decoded string must equal the input. Same approach spot-checks
  QR / Code 39 / EAN-13.
* Standalone tools (`BulkBarcodeService`, `SequenceGenerator`) sit on top of the
  same engine; the WPF **Bulk Barcode Generator** and the CLI both call
  `BulkBarcodeService`.

\* GS1 DataBar only if the installed ZXing build encodes it; otherwise it is
hidden from the symbology list (see OMISSIONS), never a broken menu item.

---

## 6. Master-label & dataset architecture

Namespace `LabelMaker.Core.Data` + `LabelMaker.Core.Bulk`. Implements spec §9,
§27, §36, §37: **Master Label + Dataset + Current Record** — never one canvas per
record.

```
DataSet
├─ Columns : List<DataColumn>        (name, inferred type, sourceIndex)
├─ Rows    : IReadOnlyList<DataRecord>   (backed by array; virtualised in UI)
└─ Source  : DataConnectionModel     (CSV | XLSX | Manual | Clipboard; reloadable)

DataRecord : indexer [columnName] → string, plus typed accessors

RenderContext
├─ Document        = ProjectModel.MasterLabel
├─ Record          = DataSet.Rows[currentIndex]     (or null for static pages)
├─ RecordIndex, RecordCount
├─ SheetPosition   (row, col, absolute index on the Formtec sheet)
└─ Resolver        = VariableResolver(Record, Variables, builtins)

BulkRenderPlan
├─ Template, DataSet, MasterLabel
├─ StartPosition (skip used labels), SkipMask, RecordFilter/Selection
└─ Enumerate() → IEnumerable<PlacedLabel>   (lazy; PlacedLabel = record + page rect + resolver)
```

* The designer edits `MasterLabel` once. The **Record Navigator**
  (`First/Prev/Next/Last/®12 of 5000`) only moves `currentIndex` and re-resolves
  — O(1), no document cloning.
* `RecordOverrides` is an opt-in sparse dictionary for the rare "label 4,213
  needs a different photo" case; the renderer applies overrides on top of the
  master.
* Preview generation is lazy + cached (`BarcodePreviewCache` keyed by
  symbology+data+options+size; LRU). Sheet preview virtualises: only visible
  label cells are rendered.
* Bulk export streams `PlacedLabel`s to the PDF/print pipeline — memory stays
  flat at 10,000+ records.

---

## 7. Printing pipeline

Namespace `LabelMaker.Core.Printing` (math, portable) + `LabelMaker.App.Printing`
(WPF `PrintQueue`). Implements spec §15, §16, §41.

```
SheetLayoutEngine
   input:  LabelTemplate, page orientation, StartPosition, SkipMask, selection
   output: List<LabelSlot>  { index, row, col, rectInPageMm }

PrintJobPlan
   ├─ PrintQueue + PrintTicket (media size, orientation, copies, source tray)
   ├─ PrinterCalibration (per-printer: offsetXmm, offsetYmm, scaleX, scaleY)
   ├─ hardware margin (from PrintTicket.PageImageableArea) → origin correction
   └─ pages: each page = one sheet; each sheet = N LabelSlots × (record or master)

LabelSheetPaginator : DocumentPaginator
   for each sheet:
     transform = Translate(-hardwareOriginMm)          // printer's unprintable border
               ∘ Translate(calibration.offsetMm)       // wizard result, per printer
               ∘ Scale(calibration.scale)              // fine scale correction
     for each slot:
        push Translate(slot.rectInPageMm) → clip to label → LabelRenderer.Render(ctx)
   PageSize is the true media size in DIU; **PrintTicket.PageScalingFactor is
   forced to 1 / "None"** — never "fit to page" (spec §16).
```

* **Printer Calibration Wizard** prints a crosshair + ruler target, the user
  measures the offset, and we store
  `{printerName → PrinterCalibration}` in
  `%APPDATA%\LabelAxis\calibration.json` (spec §16: X `+0.8mm`, Y `-1.2mm` per
  printer).
* **Actual-size preview**: the preview control renders the paginator output at
  `zoom = screenDpi/96` so 100% on screen equals physical size; a "hold a ruler
  to the screen" calibration adjusts `screenDpi`.
* Print preview, print range, "start at label 7", "skip used labels", "selected
  records only", copies — all are `PrintJobPlan` inputs, no special cases in the
  renderer.

---

## 8. Save / load architecture

Namespace `LabelMaker.Core.Serialization`. Native format **`.labelproj`** —
a ZIP container (spec §18):

```
project.labelproj  (ZIP)
├─ manifest.json        { schema:"labelmaker.project/1", appVersion, created, modified }
├─ project.json         ProjectModel  (System.Text.Json, source-generated context, indented)
├─ template.json        embedded copy of the Formtec template (portability)
├─ data/
│   ├─ <connectionId>.csv        cached snapshot of each imported dataset
│   └─ <connectionId>.meta.json  column map, header row, filters, sheet name
├─ assets/
│   └─ <sha256>.<ext>            de-duplicated images (referenced by hash)
└─ thumbnails/label.png          for the Recent Projects / template library
```

* **Atomic save** (spec §18, §28): write to `project.labelproj.tmp` in the same
  directory, `FileStream.Flush(true)`, then `File.Replace` (falls back to
  delete+move across volumes) so a crash mid-save never corrupts the last good
  file. A `.bak` of the previous version is kept.
* **Versioned**: `schemaVersion` on `ProjectModel`; `ProjectUpgrader` runs
  ordered migrations old→current on load. Unknown newer version → refuse with a
  clear message, never partial-parse.
* **No executable content.** Deserialization uses a fixed
  `JsonSerializerContext` (source generator) with a closed type list — no
  polymorphic type-name binding, no `$type`, no scripts. Opening a project cannot
  run code (spec §28).
* **Auto-save & recovery** (spec §32): every N seconds (default 120) the
  in-memory model is written to
  `%LOCALAPPDATA%\LabelAxis\recovery\<projectId>\autosave.labelproj` — a
  *separate* file, the user's saved file is never touched. A sentinel records
  "clean shutdown". On launch, an un-cleared sentinel → "An unsaved project was
  recovered. Restore it?". On clean exit the recovery folder is purged.
* **Backup**: on each manual save, the prior file is copied to
  `<name>.labelproj.bak` (configurable rolling count).
* `RecentProjects` list in settings; missing files pruned lazily.

---

## 9. Rendering pipeline

Namespace `LabelMaker.Core.Rendering`. One renderer, many surfaces (spec §17,
§29):

```
interface IDrawingSurface
   PushTransform(Matrix) / Pop() ;  PushClip(rect|roundrect) / Pop()
   FillRectangle / StrokeRectangle / FillRoundRect / FillEllipse / StrokeLine / FillPolygon
   DrawText(formattedRuns, rect, alignment, options)      // vector glyphs
   DrawImage(decodedImage, destRectMm, adjustments)
   DrawBarcodeBars(bars, rectMm) / DrawBarcodeMatrix(matrix, rectMm)   // = filled rects

LabelRenderer.Render(LabelDocument, RenderContext, IDrawingSurface)
   1. background (image fit mode + adjustments + opacity), clipped to label shape
   2. objects in z-order:
        resolve {expressions} via ctx.Resolver
        push object transform (translate to Bounds, rotate about centre, opacity)
        dispatch by type
   3. optional crop marks / label outline (preview & PDF only)

Surfaces:
   WpfDrawingSurface     → DrawingContext        (screen canvas, print, RenderTargetBitmap)
   PdfSharpDrawingSurface → XGraphics            (vector PDF)
   SvgDrawingSurface     → StringBuilder          (SVG document)
```

The designer canvas draws the *same* `LabelRenderer` output under an editing
overlay (selection handles, guides, snap lines) so "what you see" is exactly
"what prints".

---

## 10. Proposed folder structure

```
LabelAxis/
├─ LabelMaker.sln
├─ Directory.Build.props            common TFM, nullable, analyzers, version
├─ Directory.Packages.props         central package versions (locked)
├─ README.md  LICENSE  .editorconfig  .gitignore
├─ docs/
│   ├─ ARCHITECTURE.md   TEMPLATE-SCHEMA.md   BUILD.md
│   ├─ DEVELOPER.md      ROADMAP.md           OMISSIONS.md
├─ templates/formtec/*.json         shipped Formtec templates (data)
├─ src/
│   ├─ LabelMaker.Core/             net10.0 — no UI. The whole engine:
│   │   ├─ Units/  Model/  Model/Objects/  Editing/
│   │   ├─ Formtec/  Barcodes/  Barcodes/Code128/
│   │   ├─ Data/  Data/Import/  Variables/  Sequences/  Bulk/
│   │   ├─ Search/  Preflight/  Printing/  Rendering/
│   │   ├─ Serialization/  Diagnostics/  Settings/
│   ├─ LabelMaker.Export/           net10.0 — PdfSharp + SVG surfaces, exporters
│   ├─ LabelMaker.App/              net10.0-windows — WPF
│   │   ├─ App.xaml  MainWindow.xaml
│   │   ├─ Views/ (Ribbon, DesignerView, DataView, BarcodeToolView, PrintPreviewView,
│   │   │          CalibrationWizard, TemplatePicker, PreflightView)
│   │   ├─ ViewModels/   Controls/DesignerCanvas   Adorners/
│   │   ├─ Rendering/WpfDrawingSurface  Rendering/WpfImageExporter
│   │   ├─ Printing/WpfPrintServer      Services/  Interop/HighDpi
│   └─ LabelMaker.Cli/              net10.0 — headless: barcode/sequence/bulk export
├─ tests/
│   └─ LabelMaker.Tests/            xUnit — Core + Export (headless)
├─ installer/
│   ├─ LabelMaker.iss               Inno Setup: shortcuts, icon, .labelproj assoc, per-user
│   └─ assets/ (icon.ico, banner)
└─ build/
    ├─ build.ps1   publish.ps1      test.ps1
```

---

## 11. Technical risks & mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Printer hardware margin / origin varies by driver | Labels shift on the sheet | Read `PageImageableArea` from the live `PrintTicket`; per-printer Calibration Wizard; store offsets separately per printer. |
| "Fit to page" silently rescaling | Wrong physical size | Force `PageScalingFactor = 1`, `PageScaling = None`; `PhysicalMeasurementTests` guards it; preview shows a mm ruler. |
| ZXing encoding quirks (ITF length, EAN check digits, GS1) | Invalid/again-unscannable codes | `BarcodeValidator` pre-checks; round-trip decode tests in CI; symbology hidden if the build can't encode it. |
| Code 128 A/B/C optimisation bugs | Unscannable / oversized codes | First-party encoder with the published cost-optimal shift algorithm + exhaustive round-trip corpus test. |
| XLSX edge cases (dates, shared strings, multiple sheets) | Wrong merge data | ClosedXML (mature); data-preview step with sheet + header-row selection before binding; values read as formatted text. |
| Large jobs (10k+) freezing UI | Unusable | Master-label model, virtualised lists, lazy preview, `IEnumerable` streaming to export/print, background workers with cancellation. |
| Font not installed on this machine | Layout reflow, wrong metrics | `FontResolver` reports missing families; preflight warning; project stores family name + a substitution note. |
| High-DPI / per-monitor DPI | Blurry canvas, wrong "actual size" | `PerMonitorV2` manifest; canvas works in mm→DIU→zoom, never raw pixels; screen-DPI calibration for actual-size preview only. |
| SVG import fidelity | Broken visuals | SVG supported for **images/shapes we control on export**; SVG *import* limited to `<image>`-style placement, complex SVG rasterised or refused (OMISSIONS). |
| PdfSharp font embedding / CJK | Missing glyphs in PDF | Embed subsetted fonts; fall back to rasterising a text object only when embedding fails, with a preflight note. |
| Atomic save across network drives | `File.Replace` unsupported | Detect and fall back to `tmp → delete → move`; still never delete the old file before the new one is fully flushed. |
| ClosedXML / OpenXml dependency size | Bloat | Isolated to `Core.Data.Import`; interface `ITabularReader` so it can be swapped; no OpenXml types leak past the importer. |

---

## 12. Features simplified or excluded for reliability

Full list with rationale in [`OMISSIONS.md`](OMISSIONS.md). Headlines:

* **No Microsoft Publisher `.pub` import** — excluded by the spec and by us.
* **PDF import = raster background/reference only**, not editable vector import.
* **SVG import = limited**; arbitrary SVG is rasterised on load or refused, never
  silently mangled.
* **Tables** — basic grid (fixed rows/cols, per-cell text, uniform borders).
  No cell mer, no spanning, no per-cell rotation in v1.
* **Rich text** — runs with font/size/style/colour. No embedded objects, no
  tab stops, no bulleted lists, no RTF import.
* **GS1 DataBar / ISBN / UPC-E** — shown only when the bundled ZXing build
  encodes them and a round-trip test passes; otherwise hidden, not stubbed.
* **Image editing** — resize/crop/rotate/flip/opacity/brightness/contrast only.
  No filters, no layers-within-image, no background removal.
* **Database data sources (SQLite/Access/SQL/ODBC/REST)** — architected for
  (`IDataSource`) but **not shipped in v1** (spec §23 permits this).
* **Polygon tool** — regular N-gon + star only; freeform bézier path editing is
  out.
* **Expression engine** — whitelisted functions over strings/numbers/dates only;
  no variables assignment, no loops, no I/O, hard node/time limits. By design.
