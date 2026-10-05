Design a professional, modern and friendly user interface for LabelAxis, a Windows desktop program for designing and printing labels. I will rebuild your design in the real app (WPF on Windows 10 and 11), so it must be practical to build: clear measurements, a reusable design system, and every screen and state the program needs.

## What LabelAxis is and who uses it

People design a label on screen and print it, either on A4 label sheets (Formtec, Avery) or on thermal label printers over USB, Wi-Fi, network or Bluetooth.
- **Labels can hold:** text, barcodes, QR codes, pictures, shapes and tables.
- **Many labels in one go:** fill them from a spreadsheet or database to print thousands of different labels.
- **Users:** small shops, stationery and print shops, pharmacies, food sellers, warehouses, offices and schools in Iraq and Kurdistan. Most are not designers or computer experts. The program must feel calm, obvious and welcoming to them, while staying quick for people who use it all day.
- **Languages:** English, Arabic and Central Kurdish (Sorani). In Arabic and Kurdish the whole interface is laid out **right to left**, but the label being designed keeps its own direction. Technical names such as PDF, QR Code, Wi-Fi and barcode type names stay in English.

## Design goals

- **Professional:** precise alignment, a consistent grid, restrained colour, clear hierarchy. It should feel like a serious tool for paid work.
- **Modern:** in the spirit of Windows 11 (Fluent): soft rounded corners, quiet surfaces, subtle depth, crisp line icons, generous but not wasteful spacing.
- **Friendly:**
  - Plain words instead of jargon, and helpful empty states ("No objects yet. Add text, a barcode or a picture from the toolbar.").
  - Gentle guidance for first-time users, and clear feedback after every action.
  - Reassuring messages before anything that costs paper.
- **The label is the hero:** the canvas in the middle gets the most space and the most contrast; panels around it stay quiet.
- **Works on small screens too:** usable at 1280 × 720, comfortable at 1920 × 1080. Nothing overlaps when space is tight. Show how each area shrinks, scrolls or collapses.

## Screens to design

**1. Main window (the most important).**
- **Title bar:** custom-drawn, with the app logo, menus (File, Edit, Data, Print, View, Help) and window buttons.
- **Toolbar:**
  - Tools: Select, Text, Image, Barcode, QR Code, Table, Shapes ▾, Lines ▾.
  - Arrange and align: Align ▾, Arrange ▾, Group, Ungroup.
  - Zoom in, 100%, zoom out.
  - On the far side: Preview and a primary Print button.
- **Left panel, with tabs:**
  - *Pages:* a numbered page list 1, 2, 3… with add, duplicate, remove, reorder and rename-in-place.
  - *Objects on this page:* a list with drag-to-reorder and lock icons.
  - *Data:* the connected spreadsheet or database.
- **Centre:**
  - The label canvas, with rulers, a grid, guides, a document tab strip ("Untitled ×", "+") and a slim canvas toolbar (snap, grid size, guides, rulers, lock guides).
  - Show it with a selected object: handles, a rotation handle, smart alignment guides and a size tooltip.
- **Right panel, with tabs:** Properties, Layers, Page.
  - Properties is a set of collapsible sections: Object, Text settings, Barcode settings, Image settings, Shape settings, Position & size, Print calibration, Export settings.
  - Show three versions: nothing selected, a text box selected, and a barcode selected.
- **Status bar:** project saved / unsaved, label sheet name and size, the record navigator (only when data is loaded), zoom, and the printer with its calibration state.
- **Update bar:** appears under the toolbar when a new version is available, with "Update now", "What's new", "Later" and "Skip this version".

**2. Welcome window:** new project, open, recent projects with thumbnails, and popular label sizes.

**3. New project / Label size picker:**
- Search by product code or size, brand filter, favourites (★), and a sheet preview drawing.
- A "custom size" form, and thermal roll sizes.

**4. Print window:**
- Printer picker (USB, Wi-Fi and network printers), what to print (all pages, this page, chosen pages with their own copy counts, records).
- Paper and margins, and a large live preview of the sheet with page arrows.
- A clear summary such as "3 sheets · 72 labels" and the Print button.

**5. Print preview:** a full-size sheet preview with zoom and page navigation.

**6. Settings:**
- Language (with native names: English, العربية, کوردی), theme (Light / Dark), canvas defaults, confirmations, start-up, auto-save, recent files.
- **Fonts:** fonts added from files, with Add font… and Remove. **Updates:** an automatic check, the update address and when it last checked.

**7. Apply page format to other pages:**
- What to copy: position, size, rotation, look, with sub-options for text, and "add missing objects".
- Which objects, how to match them, and a list of pages with tick boxes and counts.

**8. Data connection:** pick a source (CSV / Excel file, SQLite, SQL Server, MySQL, PostgreSQL, Access, ODBC), connection fields, a test button and a records table.

**9. Smaller windows:**
- Printer calibration wizard (step by step).
- Find and replace.
- Help guide (searchable articles with a sidebar).
- Colour picker.
- Message box (information, warning, question, error).
- Progress dialog for long print jobs.
- About.

## States and variations

- **Light and dark** themes for the main window, Print and Settings.
- **Right-to-left:** the main window and Print window in Arabic, and one screen in Kurdish.
  - The mirroring must be correct: panels swap sides, arrows point the other way, and number fields stay readable.
  - Arabic and Kurdish text runs about 20–30% longer or shorter than English, so layouts must not depend on exact text widths.
- **Interaction states** for buttons, toggles, inputs, list rows, tabs and menus: rest, hover, pressed, focused (a visible keyboard focus ring), disabled, selected.
- **Empty, loading and error states:** no objects on the page; no printers found; a database that cannot be reached; a barcode whose value is wrong for its type (shown on the canvas and in the panel).
- **Accessibility:**
  - Text contrast at least 4.5 : 1 (WCAG AA).
  - Click targets at least 32 px high in toolbars and 28 px in dense lists.
  - Everything reachable with the keyboard.
  - Never colour alone to carry meaning.

## Design system to deliver

- **Colour tokens for light and dark:** background, surface, raised surface, borders (normal / strong), text (primary / secondary / muted), accent with hover and soft variants, success, warning, danger, selection, the canvas backdrop and the label paper.
  - Today's accent is blue #2C6FE8 (light) and #5C9BFF (dark); keep or improve it.
  - Name the tokens so they map one to one onto my theme (e.g. `Bg`, `Surface`, `Surface2`, `Border`, `BorderStrong`, `Text`, `Text2`, `TextMuted`, `Accent`, `AccentHover`, `AccentSoft`, `Ok`, `Danger`, `SelectionBlue`).
- **Typography:**
  - Use Segoe UI (or Segoe UI Variable on Windows 11) for English. For Arabic and Kurdish interface text, choose and show a font that has all Kurdish letters (ڕ ڵ ێ ۆ ە), e.g. Segoe UI, Tahoma, or IBM Plex Sans Arabic.
  - Give a type scale with sizes, weights and line heights, around a 13 px base.
- **Spacing and shape:** a spacing scale (e.g. 4-px based), corner radii, border widths, shadows and elevation levels, and panel widths (with minimums).
- **Icons:** one consistent line-icon style (about 1.5 px stroke, 16 and 20 px sizes) for every toolbar tool and panel section, delivered as simple vector paths (SVG) I can convert.
- **Components:**
  - Primary, secondary, ghost and icon buttons.
  - A tool tile (icon above a short label).
  - Split button, text box, number box with unit (mm), drop-down, editable font picker, check box, radio button, toggle switch, slider with a value box.
  - Colour swatch button, tabs, collapsible section header, list row, tree row, table/grid, tooltip, context menu, menu bar, status bar item, toast/notification, dialog frame.
- **Motion:** short, subtle transitions (100–200 ms) only where they help, plus a "reduce motion" option.

## What to send back

1. High-fidelity mockups of every screen above at 1920 × 1080 (the main window also at 1280 × 720), light and dark, plus the right-to-left screens.
2. The design system as a single reference page: tokens with hex values, the type scale, the spacing scale, every component in all its states, and the icon set.
3. Measurements I can build from: panel widths, paddings, heights of the title bar, toolbar, status bar and list rows, and component sizes, given in px at 100% Windows scaling.
4. A short note for each screen on the main improvement over a typical, cluttered label program, and anything that needs special behaviour (for example, what collapses first when the window gets narrow).

Use realistic content everywhere: a price tag "Notebook A5 – 100 sheets – 3,500 IQD" with an EAN-13 barcode, a Formtec 3105 sheet (63.53 × 38.1 mm, 21 labels), a printer named "Xprinter XP-365B", and Arabic and Kurdish sample text, never lorem ipsum.
