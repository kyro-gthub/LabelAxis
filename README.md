# Label Maker

A **Windows-first desktop application for designing and printing labels** — built
around Formtec label sheets, reliable barcodes (Code 128 first), bulk /
variable-data printing, fast search, and **physically accurate output**.

> Design → Barcode → Data → Formtec sheet → Print, done reliably.

| | |
|---|---|
| Stack | C# 13 / .NET 10, WPF (`net10.0-windows`) |
| Core engine | `LabelMaker.Core` — plain `net10.0`, **no UI dependency**, fully unit-tested |
| Barcodes | first-party **Code 128 / GS1-128** encoder + ZXing.Net for QR, Data Matrix, PDF417, Aztec, EAN/UPC, Code 39/93, ITF, Codabar |
| Data | CSV / TSV / TXT (CsvHelper) · XLSX (ClosedXML) · clipboard · manual tables |
| Export | vector **PDF** (PdfSharp) · **SVG** · **PNG / JPEG** (300 / 600 / custom DPI) |
| Project file | `.labelproj` — ZIP container, atomic save, crash recovery, versioned |
| Tests | 130 xUnit tests, incl. **ZXing round-trip decoding** of generated barcodes |

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full design,
[`docs/DEVELOPER.md`](docs/DEVELOPER.md) for the engine's internal map,
[`docs/UI-DESIGN.md`](docs/UI-DESIGN.md) for where visual style ends and app
behavior begins in the WPF desktop app, and [`docs/OMISSIONS.md`](docs/OMISSIONS.md)
for features deliberately left out to keep the core reliable.

---

## What it does

### Visual label designer
WYSIWYG canvas that renders through the **same renderer used for print and PDF**,
so what you see is what prints. Text boxes (with `{Field}` tokens), rich-ish text,
images (with fit modes, brightness/contrast, flip, crop), shapes (rect, rounded
rect, ellipse, line, arrow, polygon/star), barcodes, QR codes, simple tables,
date/time fields, page/record fields. Move / resize (8 handles) / rotate / lock /
hide / group / reorder. **Rulers with draggable guides** (drag from a ruler to
drop one, drag it back to delete), **snap-to-guide / snap-to-object / snap-to-grid**
with live snap lines, wheel-zoom, drag-to-pan, and a **property grid where you
type exact millimetre X/Y/W/H/rotation**. Full undo/redo (command stack, coalesced
drags).

### Formtec templates
36 built-in Formtec sheets — the **3000-series** (address, shipping, mini, round,
CD, name badge, binder spine, square, full-page, US-Letter) **and the full
GS-1xxx general-label family** (GS-1001 … GS-1100: 1-up through 100-up on A4) —
plus an Avery sheet to prove the schema is brand-agnostic. Search by product code
/ dimensions / category, sheet preview, duplicate / edit, and **import hundreds
more by dropping JSON or CSV files** — geometry is never hard-coded (see
[`docs/TEMPLATE-SCHEMA.md`](docs/TEMPLATE-SCHEMA.md)).

### Barcodes
`Code 128` (auto A/B/C set optimisation), `Code 128 A/B/C`, `GS1-128`, `Code 39`,
`Code 93`, `EAN-13/8`, `UPC-A/E`, `ITF-14`, `Interleaved 2 of 5`, `Codabar`,
`ISBN-13`, `MSI`, `QR Code`, `Data Matrix`, `PDF417`, `Aztec`. Everything is
rendered as **resolution-independent rectangles** (vector) — crisp at any DPI.
Data is validated before printing. A symbology only appears in the UI if the
running build can actually encode it (no stub menu items).

### Variable data & bulk
Master Label + Dataset + current record — **never one canvas per record**, so
10,000+ record jobs stay responsive. Import CSV/XLSX with header-row selection,
column rename, filter, sort and duplicate detection. Bind objects with
`{Product}`, `{Barcode}`, `{Price}` … plus a **sandboxed** expression engine
(`"LOT-" + {Batch}`, `UPPER({Product})`, `LEFT({Barcode},5)`,
`FORMAT({Price},"0.00")`). Sequence generator (`ABC000001…ABC010000`,
`BOX-A-#####`, hex/base-36). Bulk Barcode Generator writes an entire series to a
folder as SVG or PDF.

### Search
One box searches page names, IDs, tags, on-label text, barcode values, serial
numbers, template names and **every data-record value** — jumps straight to the
page / object / record. Fast on large projects.

### Printing
Print preview, printer + paper selection, copies, **print from label position N**,
skip used labels, selected records/positions, and a **Printer Calibration Wizard**
that stores an X/Y offset (and fine scale) **per printer**. Output defaults to
**100 % physical size** — "fit to page" is never applied automatically. A label
defined as 40 mm × 20 mm exports and prints as 40 mm × 20 mm (verified by tests).

### Preflight
Optional pre-print check: barcode/text/object outside the label or printable
area, missing image, missing font, invalid barcode, duplicate barcode, low-res
image, empty required field. Errors vs warnings, each linking to the offending
object/record.

---

## Quick start (developers)

```bash
# Prerequisites: .NET 10 SDK on Windows 10/11
dotnet build LabelMaker.slnx -c Release
dotnet test  tests/LabelMaker.Tests
dotnet run   --project src/LabelMaker.App        # launch the designer
```

Full build / packaging instructions: [`docs/BUILD.md`](docs/BUILD.md).

### Command-line tool

The `labelmaker` CLI exposes the engine headlessly (vector SVG/PDF only — PNG is
desktop-app only):

```bash
labelmaker barcode --symbology code128 --value ABC001459 --out code.svg
labelmaker sequence --prefix PRD- --suffix -IRQ --start 1 --end 10000 --padding 6
labelmaker bulk-barcode --symbology code128 --prefix ABC --start 1 --end 10000 \
           --padding 6 --format svg --out ./out
labelmaker export --project labels.labelproj --data rows.csv --out sheet.pdf
labelmaker templates list
labelmaker preflight --project labels.labelproj --data rows.csv
labelmaker inspect --project labels.labelproj
```

---

## Repository layout

```
Label Maker/
├─ LabelMaker.slnx                 solution (also builds with `dotnet build`)
├─ Directory.Build.props           common TFM / nullable / analyzers / version
├─ Directory.Packages.props        central, pinned package versions
├─ docs/                           architecture, template schema, build, roadmap, omissions
├─ templates/formtec/*.json        shipped label-sheet templates (data, not code)
├─ src/
│   ├─ LabelMaker.Core/            the whole engine (units, model, Formtec, barcodes,
│   │                              data, variables, sequences, bulk, search, preflight,
│   │                              printing math, rendering, serialization, editing)
│   ├─ LabelMaker.Export/          PdfSharp + SVG drawing surfaces and exporters
│   ├─ LabelMaker.App/             WPF desktop application (Windows)
│   └─ LabelMaker.Cli/             headless tools (`labelmaker`)
├─ tests/LabelMaker.Tests/         xUnit — engine + export, headless
├─ installer/LabelMaker.iss        Inno Setup script (per-user, .labelproj assoc)
└─ build/                          build / test / publish PowerShell scripts
```

---

## Priorities (when trade-offs are needed)

1. Stability & data integrity
2. Printing & physical-size accuracy
3. Formtec template accuracy
4. Reliable Code 128 generation
5. Barcode & QR generation
6. Variable-data & bulk generation
7. Project save/open reliability
8. Fast barcode / page / data search
9. Easy visual design
10. PDF & image export
11. Secondary conveniences

A smaller reliable application beats a larger one full of half-working features.

## Licence

MIT — see [`LICENSE`](LICENSE). Bundled libraries retain their own licences
(ZXing.Net – Apache-2.0, CsvHelper – MS-PL/Apache-2.0, ClosedXML – MIT, PdfSharp –
MIT, xUnit – Apache-2.0, FluentAssertions 7.x – Apache-2.0).
