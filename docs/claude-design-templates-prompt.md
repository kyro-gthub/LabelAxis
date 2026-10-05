Design 50 ready-to-use label templates for LabelAxis, a Windows label design and printing program. People open a template, change the words, and print — on A4 label sheets (Formtec and Avery) or on thermal label rolls. The templates should look professional, be easy to edit, and print cleanly.

## Who uses it

Small shops, stationery and print shops, pharmacies, food sellers, warehouses, offices and schools in Iraq and Kurdistan. The software works in English, Arabic and Central Kurdish (Sorani). About a third of the templates should be bilingual (Arabic + English, or Kurdish + English), and a few fully Arabic or fully Kurdish, laid out right to left.

## The 50 templates

Spread them over these groups (about the counts shown):

1. Retail price tags and shelf labels (8): price large, old/new price, "offer", per-kg price, small jewellery tag.
2. Product labels with a barcode (8): name, size/weight, barcode (EAN-13 or Code 128), price, made in.
3. Food labels (5): ingredients, best before / produced on, net weight, halal mark area, simple nutrition table.
4. Shipping and address labels (6): from/to, phone, order number, Code 128 tracking barcode, a 100 × 150 mm thermal shipping label.
5. Inventory and asset tags (5): item code, location (aisle/shelf/bin), QR code, "property of" asset tag.
6. Stationery, school and office (6): exercise-book name labels, file/folder spine labels, subject labels, return-address labels.
7. Pharmacy and clinic (4): medicine dose instructions (morning/noon/night), patient name, date.
8. Name badges and events (4): visitor badge, staff badge with photo area, event ticket with QR code.
9. QR / contact and social (4): "scan to pay / visit / follow", Wi-Fi QR, business contact label.

## Label sizes to use

Use only these real sizes (width × height, millimetres), so each template fits a sheet or roll the software already knows:

- Formtec sheets: 3100 — 38.1 × 21.17 · 3102 — 47 × 26.86 · 3104 — 62.67 × 30.06 · 3105 — 63.53 × 38.1 · 3106 — 64 × 34 · 3107 — 99.05 × 33.85 · 3108 — 99.05 × 38.07 · 3111 — 20 × 20 · 3114 — 99.1 × 67.55 · 3116 — 99.1 × 92.93 · 3118 — 99 × 139.8 · 3189 — 25.4 × 10 · 3218 — 63.5 × 45 · 3639 — 37.96 × 37.96 (round) · 3640 — 62.95 × 62.95 (round) · 3672 — 76 × 56
- Avery: L7160 — 63.5 × 38.1
- Thermal rolls: 40 × 30 · 50 × 30 · 58 × 40 · 100 × 50 · 100 × 150

## What a template can contain

Design only with what LabelAxis can draw, so every design can be rebuilt exactly:

- **Text box:** font, size in points, bold, italic, underline, text colour, highlight colour, left / centre / right / justified, top / middle / bottom, line spacing, letter spacing, right-to-left for Arabic and Kurdish. Variable fields are written in curly brackets, e.g. `{Product}`, `{Price}`, `{Date}`.
- **Barcode:** Code 128, EAN-13, EAN-8, UPC-A, Code 39, ITF-14, GS1-128, with or without the digits under the bars; black on white.
- **QR code** (also Data Matrix, PDF417).
- **Image:** a placeholder box for a logo or photo (fit, optional round or rounded-corner crop, optional border).
- **Shape:** rectangle, rounded rectangle, ellipse, line, arrow, polygon or star; outline colour and thickness, solid or dashed, fill colour or a two-colour gradient.
- **Table:** rows × columns with border lines, optional shaded header row.
- **Fonts:** Segoe UI, Arial, Calibri, Tahoma, Verdana, Times New Roman, Courier New, Consolas, and for Arabic: Tajawal, IBM Plex Sans Arabic, Almarai, Amiri, Scheherazade New. For **Kurdish text use Segoe UI, Tahoma or Arial** (Tajawal lacks Kurdish letters such as ڕ ڵ ێ ۆ ە).

## Printing rules

- Keep everything at least 1.5 mm inside the label edge; no bleed.
- Smallest text 6 pt; barcodes at least 8 mm tall, with clear space on both sides.
- Every thermal-roll template must work in pure black and white (no grey fills, no colour that carries meaning).
- Sheet templates may use colour, but must still read well printed in black and white.
- Round labels: keep content inside the circle.
- Real-looking sample text (not lorem ipsum): realistic product names, prices in IQD, Iraqi phone numbers.

## What to deliver

1. A gallery of all 50 designs drawn at true proportions, grouped as above, each with its name, size and intended use.
2. For each template, a precise spec I can turn into a LabelAxis file, as JSON in this shape (all positions and sizes in millimetres from the label's top-left corner, colours as #RRGGBB):

```json
{
  "id": "retail-price-tag-01",
  "name": "Price tag – bold price",
  "group": "Retail price tags and shelf labels",
  "size": { "sheet": "Formtec 3105", "widthMm": 63.53, "heightMm": 38.1 },
  "language": "en | ar | ckb | ar+en | ckb+en",
  "background": "#FFFFFF",
  "objects": [
    { "type": "text", "name": "Product", "x": 3, "y": 3, "w": 57.5, "h": 7,
      "text": "{Product}", "sample": "Notebook A5, 100 sheets",
      "font": "Segoe UI", "sizePt": 11, "bold": true, "color": "#1A1A1A",
      "align": "left", "valign": "top", "rightToLeft": false },
    { "type": "barcode", "name": "Barcode", "x": 3, "y": 22, "w": 30, "h": 12,
      "symbology": "EAN-13", "data": "{Barcode}", "sample": "6260000000017", "showDigits": true },
    { "type": "qr", "name": "QR", "x": 45, "y": 20, "w": 15, "h": 15, "data": "https://example.com" },
    { "type": "image", "name": "Logo", "x": 50, "y": 3, "w": 10, "h": 10, "placeholder": "logo", "crop": "none" },
    { "type": "shape", "name": "Price box", "shape": "roundRectangle", "x": 36, "y": 20, "w": 24.5, "h": 15,
      "fill": "#FFE8A3", "stroke": "#1A1A1A", "strokeMm": 0.3, "cornerMm": 2, "dash": "solid" },
    { "type": "table", "name": "Nutrition", "x": 3, "y": 12, "w": 40, "h": 20, "rows": 4, "columns": 2,
      "cells": ["Energy", "250 kcal", "Fat", "9 g", "Sugar", "12 g", "Salt", "0.4 g"],
      "font": "Arial", "sizePt": 7, "borderMm": 0.2, "headerRow": false }
  ]
}
```

List objects back to front (the first is drawn underneath). Give every object a short, meaningful name, because the software matches objects across pages by name. Use the same names for the same role across templates (Product, Price, Barcode, Logo, Date…).

Aim for variety in layout and mood: some minimal and modern, some bold and colourful, some classic. They should still feel like one family that belongs to the same software.
