# Feature recommendations

Ideas for where Label Maker goes next, ordered by what they are worth against
what they cost. Nothing here is committed work — it is a menu to choose from.

Read alongside [OMISSIONS.md](OMISSIONS.md), which records what was left out of
v1 **on purpose**; a few items below are omissions worth revisiting now, and they
say so.

---

## Tier 1 — biggest gain for the effort

### 1. A design library, not just a sheet library

The Templates panel today lists **sheet geometry** (Formtec GS-1189, 3100, …).
There is no way to save *the label you designed* and reuse it next month.

Save the current label as a named design with a thumbnail, in
`%APPDATA%\LabelMaker\designs\`; browse them in a fourth tab of the left panel;
double-click to apply to the current sheet. Repeat jobs — the same product label
every re-stock — go from "rebuild it" to "two clicks".

*Touches:* a small `DesignLibrary` service, a thumbnail render (the PNG exporter
already does this), one panel tab. No format change; a design is a `LabelDocument`
fragment you already serialise.

### 2. Batch export — one file per record

Bulk barcode PNG export exists, but there is no way to export **the whole label**
once per data row. Anyone uploading to a marketplace, sending artwork to a print
shop, or attaching images to a spreadsheet needs this.

Export N files from the dataset with a filename pattern (`{SKU}-{Size}.png`),
into PDF/PNG/SVG, with the DPI control the single-label export already has.

*Touches:* a loop over records around the existing exporters plus a dialog. The
renderer already resolves `{Field}` tokens per record.

### 3. Print history and reprint

A label run that jams halfway is the most expensive failure this app has, and
right now nothing remembers what was printed. Log every run (template, printer,
record range, sheet start position, copies, timestamp) to
`%APPDATA%\LabelMaker\printlog.json`, show it in the Print Queue tab, and offer
**Reprint** → prefilled Advanced Print Settings.

The pieces are there: "Start at label position" and "Skip positions" already let
you print onto a part-used sheet. Reprint just fills them in for you.

### 4. Print only the rows you picked

The Data Table is read-only and prints all-or-nothing. Add a checkbox column, a
filter box, and "Print selected rows" / "Export selected rows". A row filter is
the difference between printing 12 labels and printing 400 to find 12.

### 5. Surface preflight where the mistake happens

`LabelMaker.Core.Preflight` already knows how to find problems; today they appear
only when you ask for a report. Show them live in the Properties panel and as a
badge on the Print button:

- image below ~200 DPI at its printed size (blurry labels are the #1 reprint cause);
- barcode module width below what the printer can resolve, or a missing quiet zone;
- text overflowing its box, or an object outside the label's safe area;
- a `{Field}` token with no matching column in the loaded data.

---

## Tier 2 — clearly worth doing, smaller

### 6. Custom sheet wizard

Templates are hand-written JSON files (`templates/formtec/formtec-gs-1189.json`
was authored by hand). A measure-and-enter wizard — columns, rows, label size,
margins, gaps, with a live preview and a "print a test grid" button — lets anyone
add the sheet sitting on their desk, from any brand.

### 7. Alignment that keeps up

Add distribute horizontally/vertically and equal-spacing; alignment relative to
the *selection* as well as the label; and live snap lines while dragging (edges
and centres of nearby objects), which is the feature people miss most when moving
from Illustrator-style tools.

### 8. Format painter

Copy type, size, weight, colour, stroke and fill from one object to others. Two
lines of model work, saves dozens of clicks on every multi-object label.

### 9. Make object locking visible

Locking exists and even survives Delete and Cut, but it is buried in a per-item
button in the Assets list. Put a lock column in the Layers tab, a padlock badge on
the canvas, and a "Lock as template" toolbar toggle — the feature was asked for as
*"keep a fixed template and change only the desired object"*, and it should look
like that.

### 10. QR content wizards

A QR code currently holds whatever text you type. Add builders for URL, Wi-Fi
credentials, vCard/MeCard and plain text, so the encoded string is correct by
construction rather than by memory.

### 11. Counters that survive a restart

Sequences exist in Core. Persist "next value" per sequence between sessions, show
the next number in the label preview, and offer a reset — so serial-numbered
labels can be printed across days without bookkeeping in a notebook.

---

## Tier 3 — platform and polish

### 12. Korean UI

Formtec is a Korean brand and these sheets are bought in Korea. The app is English
only, and `docs/OMISSIONS.md` lists localisation as deferred. A `ko-KR` resource
set (plus Korean-friendly default fonts) would make this shippable to the people
most likely to use it. Strings are not externalised yet, so this is real work —
but the sooner it is done, the cheaper it is.

### 13. Printing from the command line

The CLI already does `export`, `preflight`, `sequence`, `templates`, `barcode`.
A `print` verb (project + data file + printer + record range) makes scheduled or
triggered label runs possible without opening the UI.

### 14. Update check

`LabelMaker.iss` builds an installer, but the app never looks for a newer version.
A quiet "check on start, tell me once" is enough.

### 15. Remember the workspace

Splitter positions, which panel tabs were open, which Properties sections were
expanded, window size. Small, and noticeable every single launch.

### 16. Template favourites and thumbnails

Pin the two or three sheets you actually own to the top of the list, and draw a
small preview of the grid on each row instead of a generic icon.

### 17. Thermal / roll printers (ZPL, TSPL)

Explicitly out of scope in v1. Worth reopening only if labels-on-a-roll enter the
picture — it is a second rendering back end, not a feature.

---

## Small things noticed while working, worth fixing whenever

- **The document tab strip is cosmetic.** The tab's ✕ and the ＋ both run *New
  Project*; there is only ever one open document. Either wire up real multi-document
  tabs or drop the strip, because it promises something it does not do.
- **Dialog title bars stay light in dark mode on Windows 10.** The app asks for a
  dark caption (`DWMWA_USE_IMMERSIVE_DARK_MODE`) and Windows 11 honours it, but
  Windows 10 only does when the *system* app mode is dark. Giving the dialogs the
  same custom chrome as the main window would make them fully dark everywhere.
- **The bundled fonts are dead weight.** `Assets/Fonts/` still ships Plus Jakarta
  Sans and DM Mono, which nothing references now that the UI draws in Segoe UI.
- **`BackupCount` in settings is not read by anything** — either implement rolling
  backups or remove the field.
