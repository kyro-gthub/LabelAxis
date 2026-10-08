# Developer guide

## Project map

| Project | TFM | Responsibility |
|---|---|---|
| `LabelMaker.Core` | `net10.0` | Everything non-visual. No `System.Windows`, no `System.Drawing`. |
| `LabelMaker.Export` | `net10.0` | `IDrawingSurface` implementations for PdfSharp (`XGraphics`) and SVG; PDF/SVG exporters. |
| `LabelMaker.App` | `net10.0-windows` | WPF UI, the WPF `IDrawingSurface`, PNG/JPEG raster export, `System.Printing` server. |
| `LabelMaker.Cli` | `net10.0` | `labelaxis` headless tool. |
| `LabelMaker.Tests` | `net10.0` | xUnit over Core, Export and Data. |
| `LabelMaker.App.Tests` | `net10.0-windows` | xUnit over the app's own code: view models, the Properties panel, the page list, languages. Headless: never creates `App`, shows no window, never touches the clipboard. |

**Rule:** logic goes in `Core`. If you're about to reference a UI type in `Core`,
add an interface in `Core` and implement it in `App`/`Export` instead (see
`IDrawingSurface`, `ITextMeasurer`, `IImageSource`, `IFieldValueSource`,
`PreflightEnvironment`).

## Namespaces in `LabelMaker.Core`

```
Units/          Measure, UnitConverter, PointMm/SizeMm/RectMm      physical/logical/screen/device split
Model/          ProjectModel, LabelDocument, LabelObject + Objects/  the document graph (plain data)
Model/Text/     RichTextContent, Paragraph, TextRun
Editing/        CommandStack, IUndoableCommand + concrete commands   undo/redo
Formtec/        LabelTemplate, TemplateGeometry, TemplateStore       template data + geometry
Barcodes/       BarcodeEngine, BarcodeValidator, BarcodeLayout       + Code128/ first-party encoder
Data/           DataSet, DataRecord, DuplicateDetector               + Import/ (CsvHelper, ClosedXML)
Variables/      ExpressionEngine, VariableResolver, TokenScanner     sandboxed {token} resolution
Sequences/      SequenceDefinition, SequenceGenerator
Bulk/           BulkRenderPlan, BulkBarcodeService                    master-label + dataset streaming
Search/         ProjectSearchIndex
Preflight/      PreflightChecker, PreflightIssue
Printing/       SheetLayoutEngine, PrintCoordinateMapper, PrinterCalibration/CalibrationStore
Rendering/      IDrawingSurface, LabelRenderer, TextLayouter, ImageFit, ApproxTextMeasurer
Serialization/  ProjectFile, ProjectArchive, converters, AutoSaveService, AssetStore, ProjectUpgrader
Settings/       AppPaths, AppSettings
```

## The rendering pipeline

`LabelRenderer.Render(LabelDocument, ResolveContext, IDrawingSurface, options)`
walks objects back-to-front, resolves `{tokens}` via `VariableResolver`, and emits
millimetre-space vector primitives. Three surfaces implement `IDrawingSurface`:

* `LabelMaker.App.Rendering.WpfDrawingSurface` → `DrawingContext` (screen canvas,
  print paginator, `RenderTargetBitmap`).
* `LabelMaker.Export.Pdf.PdfDrawingSurface` → PdfSharp `XGraphics`.
* `LabelMaker.Export.Svg.SvgDrawingSurface` → an SVG string.

Every `PushTransform` / `PushOpacity` / `PushClip*` is balanced by exactly one
`Pop`. `WpfDrawingSurface.OpenPushDepth` should be 0 after a render.

Text layout (`TextLayouter`) needs an `ITextMeasurer`:
`WpfTextMeasurer` (real `FormattedText` metrics) in the app,
`ApproxTextMeasurer` (glyph-advance estimate) headless.

## Units — the four spaces

Never mix these implicitly (`docs/ARCHITECTURE.md` §3):

| Space | Type | Formula |
|---|---|---|
| Physical | `Measure` (canonical mm) | authored value × unit factor |
| Document | `double` mm | object `Bounds` are mm |
| Screen | DIU | `mm × 96 / 25.4`, then × canvas zoom |
| Device | px | `mm × dpi / 25.4`, dpi from the `PrintTicket` / export request — **never the screen** |

`UnitConverter` holds the only physical constant (`MmPerInch = 25.4`). Converting
to/from `Unit.Pixel` without a DPI throws on purpose.

## Serialization

`.labelproj` is a ZIP (`ProjectArchive`) containing `project.json` (the
`ProjectModel` via `ProjectJson.Default`), `template.json` (the DTO form), cached
datasets, de-duplicated `assets/<sha256>.<ext>`, and a thumbnail.

The polymorphic `List<LabelObject>` uses `LabelObjectJsonConverter` — a **closed**
`kind` switch, no `$type`, no reflection type binding. Deserialising cannot
instantiate an arbitrary type or run code.

Saving is atomic: write `*.tmp`, `Flush(true)`, then `File.Replace` (with a
`.bak`), falling back to delete+move across volumes. `AutoSaveService` writes
recovery snapshots to `%LOCALAPPDATA%\LabelAxis\recovery\` — a **separate**
location from the user's file.

Schema changes: bump `ProjectModel.CurrentSchemaVersion` and add an ordered JSON
migration in `ProjectUpgrader.Migrations`. A newer-than-current file is refused,
never partially parsed.

## Adding things

* **A barcode symbology** — map it in `ZXingEncoder.TryMapFormat`, add a
  self-test sample in `BarcodeEngine.SelfTestSamples`, add validation in
  `BarcodeValidator`. It appears in the UI automatically once the self-test passes.
* **A label object type** — subclass `LabelObject`, add an `ObjectKind`, handle it
  in `LabelObjectJsonConverter`, `LabelRenderer.RenderObject`, and
  `ProjectSearchIndex`.
* **A data source** — implement `Data.Import.ITabularReader` (raw grid) and route
  it in `DataImportService`; the rest of the pipeline is source-agnostic.
* **An expression function** — add a case in `ExpressionEngine.CallFunction` and
  its name to the `ProbeFunction` whitelist. Keep it pure.
* **A drawing target** — implement `IDrawingSurface`. The renderer is unchanged.

## Tests

`dotnet test tests/LabelMaker.Tests` (the engine) and `dotnet test tests/LabelMaker.App.Tests`
(the app). `build\test.ps1` runs both with coverage and fails when a part falls below its
minimum. Notable suites:

* `Code128RoundTripTests` — encodes a corpus, rasterises, **decodes with ZXing**,
  asserts equality. Guards the pattern table and A/B/C algorithm.
* `FormtecGeometryTests` — every shipped template's geometry + fill order.
* `PrintCoordinateTests` / `PdfExportTests` — a 40×20 mm label stays 40×20 mm
  through the print transform and in the exported PDF page.
* `ProjectRoundTripTests` — save/load fidelity, atomic `.bak`, "no CLR type names
  in JSON", newer-schema rejection.
* `BulkRenderPlanTests` — start position, skip mask, record selection, 10k-record
  laziness.
* `ExpressionEngineTests` — documented examples + sandbox limits (recursion,
  size, division by zero, unknown function).
* `ExportDrawingTests` — every kind of object drawn into PDF and SVG; the PDF is read back
  and its drawing operators and resources checked.
* App: `MainViewModelTests`, `FilesAndDataTests`, `PropertyBoxTests` (every Properties box
  keeps what is typed and undoes in one step) run the main view model against `FakeShell`,
  a stand-in for the dialogs, with settings in a temporary folder (`AppHarness`).

Style: `.editorconfig` at the repo root; tests allow `Method_Name_With_Underscores`
via `tests/.editorconfig`. The build treats every warning as an error.

## Errors

A `catch` that tells the user what went wrong filters with `Faults.IsReportable(ex)`
(`LabelMaker.Core.Diagnostics`): a failure outside the program (a locked file, a refusing
printer) becomes a message, while a mistake in LabelAxis (null reference, bad cast, index
out of range) reaches the crash log through the app's unhandled-exception handler. Code
that must carry on whatever happens, such as drawing a page or falling back to another
font, catches everything and calls `Faults.Survived(ex)`, which writes a mistake to the
crash log once for each place it happens.
