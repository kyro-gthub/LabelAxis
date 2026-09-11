# UI design vs. application logic — where to edit what

This is a map for anyone (human or AI) who wants to change the app and needs to
know which file governs *what it looks like* versus *what it does*. For the
engine's internal structure (Core namespaces, rendering pipeline, serialization)
see [`DEVELOPER.md`](DEVELOPER.md) — this file is specifically about the WPF
desktop app's visual layer (`src/LabelMaker.App`) and how it's kept separate
from behavior.

## The short version

| You want to change... | Edit this |
|---|---|
| Colors, gradients, fonts, spacing, shadows, control look (buttons, tabs, menus, scrollbars, panels) | `src/LabelMaker.App/Theme.xaml` |
| A toolbar/ribbon icon, or add a new one | `src/LabelMaker.App/Icons.xaml` |
| A window's layout — what's on screen, in what panel, in what order | `src/LabelMaker.App/Views/*.xaml` |
| The app icon, logo, or an embedded font file | `src/LabelMaker.App/Assets/` |
| What a button/field/checkbox in the UI actually *does* when clicked | `src/LabelMaker.App/ViewModels/*.cs` (commands live here, referenced from XAML via `{Binding SomeCommand}`) |
| Which fields a selected object exposes in the Properties panel | `src/LabelMaker.App/ViewModels/SelectionProperties.cs` (the values/logic) *and* `Views/MainWindow.xaml` (the fields shown) — see below |
| File pickers, message boxes, sub-windows (which window opens for "Bulk Barcode Generator", etc.) | `src/LabelMaker.App/Services/DialogService.cs` + `IShellServices.cs` |
| Barcode encoding, template geometry, printing math, data import, undo/redo, project save format — **anything that isn't visual** | `src/LabelMaker.Core/**` (no WPF/UI reference at all — see `DEVELOPER.md`) |

## Why this split exists

`LabelMaker.Core` has **zero** dependency on `System.Windows` or `System.Drawing`
— it's pure logic, unit-tested headlessly. Everything visual lives in
`src/LabelMaker.App`, which itself follows MVVM, so *design* and *programming*
are separated at two levels:

1. **Core vs. App** — business logic (barcode math, template geometry, print
   coordinate mapping, file format) vs. the Windows-only WPF shell around it.
2. **Inside App: styling vs. behavior** — a Theme/Icons layer that only
   controls appearance, separate from ViewModels/Services that only control
   behavior. A XAML file should reference a *named style or resource*
   (`Style="{StaticResource RibbonButton}"`), never hard-code a color or a
   font — that keeps a full re-theme to touching `Theme.xaml` alone.

## `src/LabelMaker.App` folder map

```
App.xaml            Application-wide resources: merges Theme.xaml + Icons.xaml,
                     registers value converters, a few small style aliases.
Theme.xaml           ALL design tokens and control styles: colors, gradients,
                     fonts, the ribbon/menu/tab/card/button/scrollbar look.
                     Change the app's visual style here, nowhere else.
Icons.xaml           Vector line-icons (DrawingImage resources) used by the
                     ribbon toolbar and a few buttons elsewhere.
Assets/              Fonts/ (embedded Plus Jakarta Sans + DM Mono .ttf), the
                     app icon.ico and logo.png.

Views/               *.xaml (layout) + *.xaml.cs (thin code-behind — dialog
                     wiring only, no business logic). MainWindow.xaml is the
                     whole shell: title band, ribbon, left panel, canvas,
                     properties panel, status bar. Other files are the
                     secondary windows (Bulk Barcode Generator, Print
                     Settings, Settings, Template Manager, etc.)

ViewModels/          MainViewModel (all RelayCommands + top-level bindable
                     state), SelectionProperties (the Properties panel's
                     bindable façade over the selected object), small item
                     records for list views (ObjectListItem, PageListItem).
                     This is "what the app does" — no WPF drawing here.

Services/            AppEnvironment (settings/paths/templates), DialogService
                     + IShellServices (the only place that opens a Window or
                     MessageBox — ViewModels never do this directly),
                     ObjectClipboard.

Controls/            DesignerCanvas — the custom WPF control that draws the
                     ruler + artboard + selection handles + guides and
                     handles mouse/keyboard/drag-and-drop. Mostly behavior;
                     its actual pixel drawing calls out to Rendering/.

Rendering/           The WPF implementation of Core's IDrawingSurface
                     abstraction (WpfDrawingSurface), plus SheetVisualRenderer,
                     WpfImageExporter (PNG/JPEG), WpfTextMeasurer,
                     WpfImageSource, BarcodeRasterizer. This is the bridge
                     between Core's renderer and actual WPF pixels — changing
                     it changes how things are *drawn*, not what they look
                     like as a style (that's Theme.xaml) or what's on screen
                     (that's Views/).

Printing/            WpfPrintServer, paginators — talks to System.Printing.

Mvvm/                ObservableObject/RelayCommand base classes and the value
                     converters XAML binds to (BoolToVisibilityConverter,
                     IntEqualsConverter, etc.). Framework plumbing, not
                     feature logic.
```

## A worked example: the Properties panel

Say you want to add a new editable field for image objects. Three files, three
separate concerns:

1. **Model** (if the property doesn't exist yet) — `src/LabelMaker.Core/Model/Objects/ImageObject.cs`.
2. **Behavior** — add a bindable property to `SelectionProperties.cs` that reads/writes it through the existing undoable `Set(...)` helper.
3. **Layout** — add the field's `TextBlock`/`TextBox` row to the `Image` `GroupBox` in `Views/MainWindow.xaml`, using the existing `FieldLabel`/`NumberBox` *styles* (not new inline colors/fonts).

Nothing about the field's *look* needs touching Theme.xaml unless you're introducing a genuinely new kind of control.

## Common "where is..." lookups

- Ribbon button groups (File/Edit/Clipboard/Insert/Data/Print) → `Views/MainWindow.xaml`, the `<ToolBar>` section; each button's icon is `{StaticResource Icon.Xxx}` from `Icons.xaml`.
- The pastel title band / menu bar → `Views/MainWindow.xaml` top `<Border>`, styled via `Br.TitleBarGradient` in `Theme.xaml`.
- Left panel (Templates/Pages tabs, Data Source card) → `Views/MainWindow.xaml`, `Grid.Column="0"`.
- Properties panel tabs (General/Style/Position/Data) → `Views/MainWindow.xaml`, `Grid.Column="4"`, driven by `SelectionProperties.SelectedPropertyTab` + the `IntEquals`/`IntEqualsVis` converters.
- Status bar → `Views/MainWindow.xaml` bottom `<StatusBar>`.
- App-wide settings (persisted JSON) → `src/LabelMaker.Core/Settings/AppSettings.cs` (the data) + `Views/SettingsWindow.xaml` (the UI for it).
