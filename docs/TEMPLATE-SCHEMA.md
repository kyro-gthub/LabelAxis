# Label-sheet template schema (`labelmaker.template/1`)

Templates are **data**. The application ships them as JSON files under
`templates/formtec/` (compiled in as embedded resources) and also loads any
`*.json` from the user template folder:

```
%APPDATA%\LabelMaker\templates\
```

Adding a new Formtec (or Avery, or any brand) sheet is **dropping a `.json`
file** — no code change, no rebuild. Hundreds can be added this way. The Formtec
Template Manager (Tools → Formtec template manager) also imports/exports single
templates as JSON and batches as CSV.

---

## JSON shape

```jsonc
{
  "schema": "labelmaker.template/1",   // required, currently always this value

  "id": "formtec-3100",                // required, unique key (file name is not the id)
  "brand": "Formtec",                  // free text; groups the catalogue
  "productCode": "3100",               // manufacturer code shown/searched in the UI
  "name": "Formtec 3100 — Shipping label, 14-up",   // required, human title
  "categories": ["Shipping", "Address"],            // 0..n free-text tags

  "page": {
    "size": "A4",                      // A4 | A5 | Letter | Legal | Custom (informational)
    "widthMm": 210.0,                  // physical page width  (used for layout)
    "heightMm": 297.0                  // physical page height (used for layout)
  },
  "orientation": "Portrait",           // Portrait | Landscape (swaps page W/H for layout)

  "printableMarginMm": {               // assumed printer non-imageable border; used by
    "top": 4.0, "right": 4.0,          // preflight to warn about labels in the dead zone.
    "bottom": 4.0, "left": 4.0         // NOT a hard clip.
  },

  "grid": {
    "columns": 2,                      // labels across  (>= 1)
    "rows": 7,                         // labels down    (>= 1)

    "labelWidthMm": 99.1,              // one label's physical width
    "labelHeightMm": 38.1,            // one label's physical height

    "marginTopMm": 15.15,             // page top edge -> first row's top edge
    "marginLeftMm": 4.65,             // page left edge -> first column's left edge

    "hGapMm": 2.5,                     // gap between adjacent columns
    "vGapMm": 0.0,                     // gap between adjacent rows

    "cornerRadiusMm": 2.0,            // for RoundRectangle labels
    "labelShape": "Rectangle"         // Rectangle | RoundRectangle | Ellipse
  },

  "fillOrder": "RowMajor",            // RowMajor (L->R then T->B) | ColumnMajor (T->B then L->R)

  "notes": "General purpose A4 shipping / address sheet.",
  "source": "Formtec catalogue (approximate geometry)"
}
```

All lengths are **millimetres**. Numbers may be integers or decimals.

### Geometry contract

For label index *n* (0-based), or (row, col) with 0-based row/col, the label
rectangle in **page millimetres** (origin at the page's top-left) is:

```
x = grid.marginLeftMm + col * (grid.labelWidthMm + grid.hGapMm)
y = grid.marginTopMm  + row * (grid.labelHeightMm + grid.vGapMm)
w = grid.labelWidthMm
h = grid.labelHeightMm
```

`fillOrder` only changes how a linear index maps to (row, col):

| fillOrder    | index → (row, col)                         |
|--------------|--------------------------------------------|
| `RowMajor`   | `row = index / columns`, `col = index % columns` |
| `ColumnMajor`| `row = index % rows`,    `col = index / rows`    |

This is implemented once in `TemplateGeometry` and covered by
`FormtecGeometryTests` for every shipped template's corner labels.

### Validation

`TemplateValidator` runs on load. **Errors** (template rejected):

* missing `id` or `name`
* `columns`/`rows` < 1, non-positive label size, negative gaps/margins
* the label block (`marginLeft + columns·labelW + (columns−1)·hGap`, likewise
  vertically) extends **past the physical page edge**

**Warnings** (loaded, but flagged):

* labels reach into the assumed `printableMarginMm`
* first label starts inside that margin
* `RoundRectangle` with zero corner radius, or corner radius > half the label

Invalid user files are listed in `TemplateStore.LoadErrors` and skipped — one bad
file never blocks the rest.

---

## CSV batch format

`TemplateStore.ExportCsv()` / `ImportCsv()` and `labelmaker templates export-csv`
use one row per template with this header:

```
id,brand,productCode,name,categories,pageSize,pageWidthMm,pageHeightMm,orientation,
columns,rows,labelWidthMm,labelHeightMm,marginTopMm,marginLeftMm,hGapMm,vGapMm,
cornerRadiusMm,labelShape,fillOrder,printTopMm,printRightMm,printBottomMm,printLeftMm
```

`categories` is `|`-separated. Values follow the same units and meaning as the
JSON fields above.

---

## Adding your own

1. Copy an existing file in `templates/formtec/` (for a shipped default) or drop a
   new file in `%APPDATA%\LabelMaker\templates\` (for a personal one).
2. Give it a unique `id`.
3. Fill in the grid from the manufacturer's datasheet.
4. Restart the app (or reopen the Template Manager) — it appears in **New
   Project** and in search.

The shipped geometry values are close approximations of real Formtec products;
verify against your own sheet and the Calibration Wizard before a large run.
