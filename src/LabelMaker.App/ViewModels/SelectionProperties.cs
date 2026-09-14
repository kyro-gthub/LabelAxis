using System.Windows.Media;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Services;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Units;

namespace LabelMaker.App.ViewModels;

/// <summary>
/// Bindable façade over the current selection's primary object. Every setter
/// routes through an undoable <see cref="SetPropertyCommand{T}"/> so typed edits
/// in the property grid participate in undo/redo (spec §21, §31).
/// </summary>
public sealed class SelectionProperties : ObservableObject
{
    private readonly DocumentSession _session;
    private readonly LabelMaker.Core.Settings.AppSettings _settings;

    public SelectionProperties(DocumentSession session, LabelMaker.Core.Settings.AppSettings settings)
    {
        _session = session;
        _settings = settings;
        _session.SelectionChanged += (_, _) => RefreshAll();
        _session.DocumentChanged += (_, _) => RefreshAll();
    }

    // ---- units ------------------------------------------------------------

    /// <summary>
    /// The unit every length in this panel is shown in. Storage is always
    /// millimetres; only the display and parsing change (spec: mm is canonical).
    /// </summary>
    public Unit DisplayUnit => _settings.DisplayUnit;

    public string UnitSymbol => DisplayUnit.Symbol();

    /// <summary>Pixels are meaningless without a DPI, so they use the screen calibration.</summary>
    private double? Dpi => DisplayUnit == Unit.Pixel ? Math.Max(1, _settings.ScreenDpiCalibration) : null;

    private double Out(double millimetres) =>
        Math.Round(UnitConverter.FromMillimetres(millimetres, DisplayUnit, Dpi), DisplayUnit == Unit.Inch ? 3 : 2);

    private double In(double display) => UnitConverter.ToMillimetres(display, DisplayUnit, Dpi);

    /// <summary>Raised by the shell when the unit setting changes.</summary>
    public void NotifyUnitChanged()
    {
        OnPropertyChanged(nameof(DisplayUnit));
        OnPropertyChanged(nameof(UnitSymbol));
        RefreshAll();
    }

    private LabelObject? Obj => _session.Selection.Count > 0 ? _session.Selection[0] : null;

    public bool HasSelection => Obj is not null;

    private int _selectedTab;

    /// <summary>Local UI state for the General/Style/Position/Data property tab strip (not undoable).</summary>
    public int SelectedPropertyTab
    {
        get => _selectedTab;
        set { _selectedTab = value; OnPropertyChanged(); }
    }

    private bool _showBarcodeAppearance = true;

    /// <summary>Local UI state for the collapsible "Appearance" (module/bar height) section under Barcode.</summary>
    public bool ShowBarcodeAppearance
    {
        get => _showBarcodeAppearance;
        set { _showBarcodeAppearance = value; OnPropertyChanged(); }
    }
    public bool IsText => Obj is TextObject;
    public bool IsBarcode => Obj is BarcodeObject;
    public bool IsShape => Obj is ShapeObject;
    public bool IsImage => Obj is ImageObject;

    /// <summary>Installed font family names, for the Font picker (spec: choose a font from a list, not free text).</summary>
    public static IReadOnlyList<string> FontFamilies { get; } =
        Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    public string TypeName => Obj?.Kind.ToString() ?? "—";

    public string Name
    {
        get => Obj?.Name ?? string.Empty;
        set => Set(nameof(Name), () => Obj!.Name, v => Obj!.Name = v, value);
    }

    public double X
    {
        get => Out(Obj?.Bounds.X ?? 0);
        set => SetBounds(b => b with { X = In(value) });
    }

    public double Y
    {
        get => Out(Obj?.Bounds.Y ?? 0);
        set => SetBounds(b => b with { Y = In(value) });
    }

    public double Width
    {
        get => Out(Obj?.Bounds.Width ?? 0);
        set => SetBounds(b => Resize(b, Math.Max(0.1, In(value)), null));
    }

    public double Height
    {
        get => Out(Obj?.Bounds.Height ?? 0);
        set => SetBounds(b => Resize(b, null, Math.Max(0.1, In(value))));
    }

    private bool _lockAspect;

    /// <summary>
    /// When set, typing a width also updates the height (and vice versa) so the
    /// object keeps its proportions — the same rule the Shift key applies on canvas.
    /// </summary>
    public bool LockAspect
    {
        get => _lockAspect;
        set { _lockAspect = value; OnPropertyChanged(); }
    }

    private RectMm Resize(RectMm b, double? width, double? height)
    {
        if (!_lockAspect || b.Width <= 0 || b.Height <= 0)
        {
            return width is { } w ? b with { Width = w } : b with { Height = height!.Value };
        }

        double aspect = b.Width / b.Height;
        return width is { } nw
            ? b with { Width = nw, Height = nw / aspect }
            : b with { Height = height!.Value, Width = height!.Value * aspect };
    }

    public double RotationDegrees
    {
        get => Round(Obj?.RotationDegrees ?? 0);
        set => Set(nameof(RotationDegrees), () => Obj!.RotationDegrees, v => Obj!.RotationDegrees = v, value);
    }

    public double SkewX
    {
        get => Round(Obj?.SkewXDegrees ?? 0);
        set => Set(nameof(SkewX), () => Obj!.SkewXDegrees, v => Obj!.SkewXDegrees = v,
            Math.Clamp(value, -ObjectGeometry.MaxSkewDegrees, ObjectGeometry.MaxSkewDegrees));
    }

    public double SkewY
    {
        get => Round(Obj?.SkewYDegrees ?? 0);
        set => Set(nameof(SkewY), () => Obj!.SkewYDegrees, v => Obj!.SkewYDegrees = v,
            Math.Clamp(value, -ObjectGeometry.MaxSkewDegrees, ObjectGeometry.MaxSkewDegrees));
    }

    public bool Locked
    {
        get => Obj?.Locked ?? false;
        set => Set(nameof(Locked), () => Obj!.Locked, v => Obj!.Locked = v, value);
    }

    public bool Visible
    {
        get => Obj?.Visible ?? true;
        set => Set(nameof(Visible), () => Obj!.Visible, v => Obj!.Visible = v, value);
    }

    // ---- Text ----

    /// <summary>
    /// Plain-text view of the box. Edits are spliced into the existing rich text,
    /// so a bold word or a coloured price survives typing in this field.
    /// </summary>
    public string TextContent
    {
        get => (Obj as TextObject)?.Content.PlainText ?? string.Empty;
        set
        {
            if (Obj is TextObject t && t.Content.PlainText != (value ?? string.Empty).Replace("\r\n", "\n"))
            {
                Set(nameof(TextContent), () => t.Content, c => t.Content = c,
                    RichTextEditing.ApplyPlainText(t.Content, value ?? string.Empty));
            }
        }
    }

    /// <summary>True when some words carry their own formatting (set in the on-canvas editor).</summary>
    public bool HasMixedFormatting =>
        Obj is TextObject t && t.Content.Paragraphs.SelectMany(p => p.Runs).Any(r =>
            r.Bold is not null || r.Italic is not null || r.Underline is not null || r.Strikethrough is not null ||
            r.FontFamily is not null || r.FontSizePt is not null || r.Foreground is not null || r.Background is not null);

    public double FontSizePt
    {
        get => (Obj as TextObject)?.FontSizePt ?? 10;
        set => SetTextStyle(nameof(FontSizePt), t => t.FontSizePt, (t, v) => t.FontSizePt = v, Math.Clamp(value, 2, 400),
            r => r.FontSizePt = null);
    }

    public string FontFamily
    {
        get => (Obj as TextObject)?.FontFamily ?? "Segoe UI";
        set => SetTextStyle(nameof(FontFamily), t => t.FontFamily, (t, v) => t.FontFamily = v, value,
            r => r.FontFamily = null);
    }

    public bool Bold
    {
        get => (Obj as TextObject)?.Bold ?? false;
        set => SetTextStyle(nameof(Bold), t => t.Bold, (t, v) => t.Bold = v, value, r => r.Bold = null);
    }

    public bool Italic
    {
        get => (Obj as TextObject)?.Italic ?? false;
        set => SetTextStyle(nameof(Italic), t => t.Italic, (t, v) => t.Italic = v, value, r => r.Italic = null);
    }

    public bool Underline
    {
        get => (Obj as TextObject)?.Underline ?? false;
        set => SetTextStyle(nameof(Underline), t => t.Underline, (t, v) => t.Underline = v, value, r => r.Underline = null);
    }

    public bool Strikethrough
    {
        get => (Obj as TextObject)?.Strikethrough ?? false;
        set => SetTextStyle(nameof(Strikethrough), t => t.Strikethrough, (t, v) => t.Strikethrough = v, value,
            r => r.Strikethrough = null);
    }

    public string TextColorHex
    {
        get => ((Obj as TextObject)?.Foreground ?? ColorRgba.Black).ToHex();
        set
        {
            if (ColorRgba.TryParse(value, out ColorRgba c))
            {
                SetTextStyle(nameof(TextColorHex), t => t.Foreground, (t, v) => t.Foreground = v, c, r => r.Foreground = null);
            }
        }
    }

    public string HighlightHex
    {
        get => ((Obj as TextObject)?.Background ?? ColorRgba.Transparent).ToHex();
        set
        {
            if (ColorRgba.TryParse(value, out ColorRgba c))
            {
                SetTextStyle(nameof(HighlightHex), t => t.Background, (t, v) => t.Background = v, c, r => r.Background = null);
            }
        }
    }

    public Brush TextColorBrush => ToBrush((Obj as TextObject)?.Foreground ?? ColorRgba.Black);

    public Brush HighlightBrush => ToBrush((Obj as TextObject)?.Background ?? ColorRgba.Transparent);

    public static IReadOnlyList<HorizontalAlignment> HorizontalAlignments { get; } = Enum.GetValues<HorizontalAlignment>();

    public static IReadOnlyList<VerticalAlignment> VerticalAlignments { get; } = Enum.GetValues<VerticalAlignment>();

    public static IReadOnlyList<TextAutoFit> AutoFitModes { get; } = Enum.GetValues<TextAutoFit>();

    public static IReadOnlyList<TextDirection> TextDirections { get; } = Enum.GetValues<TextDirection>();

    /// <summary>Box alignment. Setting it also clears per-paragraph overrides so the whole box follows.</summary>
    public HorizontalAlignment TextAlign
    {
        get => (Obj as TextObject)?.HorizontalAlignment ?? HorizontalAlignment.Left;
        set
        {
            if (Obj is not TextObject t)
            {
                return;
            }

            RichTextContent content = t.Content.Clone();
            foreach (Paragraph p in content.Paragraphs)
            {
                p.Alignment = null;
            }

            Execute("Text alignment", new IUndoableCommand[]
            {
                new SetPropertyCommand<HorizontalAlignment>("Align", () => t.HorizontalAlignment, v => t.HorizontalAlignment = v, value),
                new SetPropertyCommand<RichTextContent>("Align", () => t.Content, v => t.Content = v, content),
            });
        }
    }

    public VerticalAlignment TextVerticalAlign
    {
        get => (Obj as TextObject)?.VerticalAlignment ?? VerticalAlignment.Top;
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(TextVerticalAlign), () => t.VerticalAlignment, v => t.VerticalAlignment = v, value);
            }
        }
    }

    public TextDirection TextDirectionValue
    {
        get => (Obj as TextObject)?.Direction ?? TextDirection.LeftToRight;
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(TextDirectionValue), () => t.Direction, v => t.Direction = v, value);
            }
        }
    }

    public bool WordWrap
    {
        get => (Obj as TextObject)?.WordWrap ?? true;
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(WordWrap), () => t.WordWrap, v => t.WordWrap = v, value);
            }
        }
    }

    public TextAutoFit AutoFit
    {
        get => (Obj as TextObject)?.AutoFit ?? TextAutoFit.None;
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(AutoFit), () => t.AutoFit, v => t.AutoFit = v, value);
                OnPropertyChanged(nameof(IsShrinkToFit));
            }
        }
    }

    public bool IsShrinkToFit => (Obj as TextObject)?.AutoFit == TextAutoFit.ShrinkToFit;

    public double MinAutoFitPt
    {
        get => (Obj as TextObject)?.MinAutoFitPt ?? 4;
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(MinAutoFitPt), () => t.MinAutoFitPt, v => t.MinAutoFitPt = v, Math.Clamp(value, 2, 200));
            }
        }
    }

    public double LineSpacing
    {
        get => Round((Obj as TextObject)?.LineSpacing ?? 1);
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(LineSpacing), () => t.LineSpacing, v => t.LineSpacing = v, Math.Round(Math.Clamp(value, 0.3, 10), 2));
            }
        }
    }

    /// <summary>Letter spacing in em (0 = normal).</summary>
    public double LetterSpacingEm
    {
        get => Round((Obj as TextObject)?.LetterSpacingEm ?? 0);
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(LetterSpacingEm), () => t.LetterSpacingEm, v => t.LetterSpacingEm = v, Math.Round(Math.Clamp(value, -0.5, 5), 3));
            }
        }
    }

    /// <summary>Uniform inner padding, in the display unit.</summary>
    public double TextPadding
    {
        get => Out((Obj as TextObject)?.PaddingMm.Top ?? 0.5);
        set
        {
            if (Obj is TextObject t)
            {
                double mm = Math.Clamp(In(value), 0, 50);
                Set(nameof(TextPadding), () => t.PaddingMm, v => t.PaddingMm = v, new LabelMaker.Core.Model.Objects.Thickness(mm));
            }
        }
    }

    /// <summary>
    /// Sets an object-level text style and, in the same undo step, removes the
    /// matching per-word overrides — otherwise ticking Bold would do nothing to
    /// words that were individually un-bolded in the canvas editor.
    /// </summary>
    private void SetTextStyle<T>(string name, Func<TextObject, T> get, Action<TextObject, T> set, T value, Action<TextRun> clearOverride)
    {
        if (Obj is not TextObject t)
        {
            return;
        }

        RichTextContent content = t.Content.Clone();
        bool hadOverrides = false;
        foreach (TextRun run in content.Paragraphs.SelectMany(p => p.Runs))
        {
            TextRun before = run.Clone();
            clearOverride(run);
            hadOverrides |= !SameRunStyle(before, run);
        }

        if (EqualityComparer<T>.Default.Equals(get(t), value) && !hadOverrides)
        {
            return;
        }

        var commands = new List<IUndoableCommand>
        {
            // The merge key folds a slider drag into one undo step.
            new SetPropertyCommand<T>($"Edit {name}", () => get(t), v => set(t, v), value, (t.Id, name)),
        };

        if (hadOverrides)
        {
            commands.Add(new SetPropertyCommand<RichTextContent>($"Edit {name}", () => t.Content, v => t.Content = v, content));
        }

        Execute($"Edit {name}", commands);
    }

    private static bool SameRunStyle(TextRun a, TextRun b) =>
        a.Bold == b.Bold && a.Italic == b.Italic && a.Underline == b.Underline && a.Strikethrough == b.Strikethrough &&
        a.FontFamily == b.FontFamily && a.FontSizePt == b.FontSizePt && a.Foreground == b.Foreground && a.Background == b.Background;

    private void Execute(string label, IReadOnlyList<IUndoableCommand> commands)
    {
        if (commands.Count == 0)
        {
            return;
        }

        _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand(label, commands));
        RefreshAll();
    }

    // ---- Barcode ----
    public IReadOnlyList<BarcodeSymbology> Symbologies { get; } = BarcodeEngine.SupportedSymbologies();

    public BarcodeSymbology Symbology
    {
        get => (Obj as BarcodeObject)?.Spec.Symbology ?? BarcodeSymbology.Code128;
        set
        {
            if (Obj is BarcodeObject b)
            {
                Set(nameof(Symbology), () => b.Spec.Symbology, v => b.Spec.Symbology = v, value);
            }
        }
    }

    public string BarcodeData
    {
        get => (Obj as BarcodeObject)?.Spec.Data ?? string.Empty;
        set
        {
            if (Obj is BarcodeObject b)
            {
                Set(nameof(BarcodeData), () => b.Spec.Data, v => b.Spec.Data = v, value);
            }
        }
    }

    public double ModuleWidthMm
    {
        get => (Obj as BarcodeObject)?.Spec.ModuleWidthMm ?? 0.33;
        set
        {
            if (Obj is BarcodeObject b)
            {
                Set(nameof(ModuleWidthMm), () => b.Spec.ModuleWidthMm, v => b.Spec.ModuleWidthMm = Math.Clamp(v, 0.05, 5), value);
            }
        }
    }

    public double BarHeightMm
    {
        get => (Obj as BarcodeObject)?.Spec.BarHeightMm ?? 15;
        set
        {
            if (Obj is BarcodeObject b)
            {
                Set(nameof(BarHeightMm), () => b.Spec.BarHeightMm, v => b.Spec.BarHeightMm = Math.Clamp(v, 1, 200), value);
            }
        }
    }

    public bool BarcodeAutoSize
    {
        get => (Obj as BarcodeObject)?.AutoSize ?? true;
        set
        {
            if (Obj is BarcodeObject b)
            {
                Set(nameof(BarcodeAutoSize), () => b.AutoSize, v => b.AutoSize = v, value);
            }
        }
    }

    /// <summary>Size of the human-readable number/text under the bars, independent of the bars themselves.</summary>
    public double HriFontSizePt
    {
        get => (Obj as BarcodeObject)?.Spec.HumanReadableFontSizePt ?? 8;
        set
        {
            if (Obj is BarcodeObject b)
            {
                Set(nameof(HriFontSizePt), () => b.Spec.HumanReadableFontSizePt, v => b.Spec.HumanReadableFontSizePt = Math.Clamp(v, 3, 72), value);
            }
        }
    }

    public bool ShowHumanReadable
    {
        get => (Obj as BarcodeObject)?.Spec.HumanReadable != HumanReadablePosition.None;
        set
        {
            if (Obj is BarcodeObject b)
            {
                Set(nameof(ShowHumanReadable), () => b.Spec.HumanReadable,
                    v => b.Spec.HumanReadable = v, value ? HumanReadablePosition.Below : HumanReadablePosition.None);
            }
        }
    }

    // ---- Barcode: the rest of the spec ----

    public static IReadOnlyList<HumanReadablePosition> HriPositions { get; } = Enum.GetValues<HumanReadablePosition>();

    public static IReadOnlyList<QrErrorCorrection> QrLevels { get; } = Enum.GetValues<QrErrorCorrection>();

    public static IReadOnlyList<string> Encodings { get; } = new[] { "UTF-8", "ISO-8859-1", "Windows-1252", "US-ASCII", "Shift_JIS", "EUC-KR" };

    private BarcodeSpec? Spec => (Obj as BarcodeObject)?.Spec;

    private void SetSpec<T>(string name, Func<BarcodeSpec, T> get, Action<BarcodeSpec, T> set, T value)
    {
        if (Spec is { } spec)
        {
            Set(name, () => get(spec), v => set(spec, v), value);
        }
    }

    /// <summary>Where the digits go: under, above, or hidden.</summary>
    public HumanReadablePosition HriPosition
    {
        get => Spec?.HumanReadable ?? HumanReadablePosition.Below;
        set
        {
            SetSpec(nameof(HriPosition), s => s.HumanReadable, (s, v) => s.HumanReadable = v, value);
            OnPropertyChanged(nameof(ShowHumanReadable));
        }
    }

    public string HriFontFamily
    {
        get => Spec?.HumanReadableFontFamily ?? "Consolas";
        set => SetSpec(nameof(HriFontFamily), s => s.HumanReadableFontFamily, (s, v) => s.HumanReadableFontFamily = v, value);
    }

    public double QuietZoneModules
    {
        get => Spec?.QuietZoneModules ?? 10;
        set => SetSpec(nameof(QuietZoneModules), s => s.QuietZoneModules, (s, v) => s.QuietZoneModules = v, (int)Math.Clamp(Math.Round(value), 0, 40));
    }

    public bool AddCheckDigit
    {
        get => Spec?.AddCheckDigit ?? false;
        set => SetSpec(nameof(AddCheckDigit), s => s.AddCheckDigit, (s, v) => s.AddCheckDigit = v, value);
    }

    /// <summary>Only Code 39, Interleaved 2 of 5 and MSI make a check digit optional.</summary>
    public bool CheckDigitIsOptional => Spec?.Symbology is BarcodeSymbology.Code39 or BarcodeSymbology.Interleaved2of5 or BarcodeSymbology.MsiPlessey;

    public bool Gs1Mode
    {
        get => Spec?.Gs1Mode ?? false;
        set => SetSpec(nameof(Gs1Mode), s => s.Gs1Mode, (s, v) => s.Gs1Mode = v, value);
    }

    public QrErrorCorrection QrEcLevel
    {
        get => Spec?.QrEcLevel ?? QrErrorCorrection.Medium;
        set => SetSpec(nameof(QrEcLevel), s => s.QrEcLevel, (s, v) => s.QrEcLevel = v, value);
    }

    public string CharacterEncoding
    {
        get => Spec?.CharacterEncoding ?? "UTF-8";
        set => SetSpec(nameof(CharacterEncoding), s => s.CharacterEncoding, (s, v) => s.CharacterEncoding = v, value);
    }

    public double Pdf417Columns
    {
        get => Spec?.Pdf417Columns ?? 0;
        set => SetSpec(nameof(Pdf417Columns), s => s.Pdf417Columns, (s, v) => s.Pdf417Columns = v, (int)Math.Clamp(Math.Round(value), 0, 30));
    }

    public string BarColorHex
    {
        get => (Spec?.Foreground ?? ColorRgba.Black).ToHex();
        set
        {
            if (ColorRgba.TryParse(value, out ColorRgba c))
            {
                SetSpec(nameof(BarColorHex), s => s.Foreground, (s, v) => s.Foreground = v, c);
            }
        }
    }

    public string BarBackgroundHex
    {
        get => (Spec?.Background ?? ColorRgba.White).ToHex();
        set
        {
            if (ColorRgba.TryParse(value, out ColorRgba c))
            {
                SetSpec(nameof(BarBackgroundHex), s => s.Background, (s, v) => s.Background = v, c);
            }
        }
    }

    public Brush BarColorBrush => ToBrush(Spec?.Foreground ?? ColorRgba.Black);

    public Brush BarBackgroundBrush => ToBrush(Spec?.Background ?? ColorRgba.White);

    /// <summary>Which symbology-specific options apply, so the panel only shows relevant ones.</summary>
    public bool IsQr => Spec?.Symbology is BarcodeSymbology.QrCode;

    public bool IsPdf417 => Spec?.Symbology is BarcodeSymbology.Pdf417;

    public bool Is2D => Spec is { } s && s.Symbology.Is2D();

    public bool IsLinear => Spec is not null && !Is2D;

    // ---- Table ----

    public bool IsTable => Obj is TableObject;

    public double TableRows
    {
        get => (Obj as TableObject)?.Rows ?? 2;
        set => ResizeTable(rows: (int)Math.Clamp(Math.Round(value), 1, 50), columns: null);
    }

    public double TableColumns
    {
        get => (Obj as TableObject)?.Columns ?? 2;
        set => ResizeTable(rows: null, columns: (int)Math.Clamp(Math.Round(value), 1, 20));
    }

    /// <summary>One editable box per cell, laid out row by row.</summary>
    public IReadOnlyList<TableCellItem> TableCells =>
        Obj is TableObject t
            ? Enumerable.Range(0, t.Rows * t.Columns).Select(i => new TableCellItem(this, t, i)).ToList()
            : Array.Empty<TableCellItem>();

    public bool TableHeader
    {
        get => (Obj as TableObject)?.FirstRowIsHeader ?? false;
        set
        {
            if (Obj is TableObject t)
            {
                Set(nameof(TableHeader), () => t.FirstRowIsHeader, v => t.FirstRowIsHeader = v, value);
            }
        }
    }

    public string TableHeaderFillHex
    {
        get => ((Obj as TableObject)?.HeaderFill ?? ColorRgba.Transparent).ToHex();
        set
        {
            if (Obj is TableObject t && ColorRgba.TryParse(value, out ColorRgba c))
            {
                Set(nameof(TableHeaderFillHex), () => t.HeaderFill, v => t.HeaderFill = v, c);
            }
        }
    }

    public string TableBorderHex
    {
        get => ((Obj as TableObject)?.BorderColor ?? ColorRgba.Black).ToHex();
        set
        {
            if (Obj is TableObject t && ColorRgba.TryParse(value, out ColorRgba c))
            {
                Set(nameof(TableBorderHex), () => t.BorderColor, v => t.BorderColor = v, c);
            }
        }
    }

    public double TableBorderWidth
    {
        get => Out((Obj as TableObject)?.BorderWidthMm ?? 0.2);
        set
        {
            if (Obj is TableObject t)
            {
                Set(nameof(TableBorderWidth), () => t.BorderWidthMm, v => t.BorderWidthMm = v, Math.Clamp(In(value), 0, 5));
            }
        }
    }

    public string TableFontFamily
    {
        get => (Obj as TableObject)?.FontFamily ?? "Segoe UI";
        set
        {
            if (Obj is TableObject t && !string.IsNullOrWhiteSpace(value))
            {
                Set(nameof(TableFontFamily), () => t.FontFamily, v => t.FontFamily = v, value);
            }
        }
    }

    public double TableFontSize
    {
        get => (Obj as TableObject)?.FontSizePt ?? 8;
        set
        {
            if (Obj is TableObject t)
            {
                Set(nameof(TableFontSize), () => t.FontSizePt, v => t.FontSizePt = v, Math.Clamp(value, 3, 72));
            }
        }
    }

    public HorizontalAlignment TableAlign
    {
        get => (Obj as TableObject)?.CellAlignment ?? HorizontalAlignment.Left;
        set
        {
            if (Obj is TableObject t)
            {
                Set(nameof(TableAlign), () => t.CellAlignment, v => t.CellAlignment = v, value);
            }
        }
    }

    public double TablePadding
    {
        get => Out((Obj as TableObject)?.CellPaddingMm ?? 1);
        set
        {
            if (Obj is TableObject t)
            {
                Set(nameof(TablePadding), () => t.CellPaddingMm, v => t.CellPaddingMm = v, Math.Clamp(In(value), 0, 20));
            }
        }
    }

    internal void SetTableCell(TableObject table, int index, string value)
    {
        if (index < 0 || index >= table.Cells.Count || table.Cells[index] == value)
        {
            return;
        }

        var cells = new List<string>(table.Cells) { [index] = value };
        _session.Execute(new SetPropertyCommand<List<string>>("Edit cell", () => table.Cells, v => table.Cells = v, cells));
    }

    /// <summary>
    /// Adds or removes rows/columns keeping existing cell text in place (the cell
    /// list is row-major, so changing the column count has to re-lay it out).
    /// </summary>
    private void ResizeTable(int? rows, int? columns)
    {
        if (Obj is not TableObject t)
        {
            return;
        }

        int newRows = rows ?? t.Rows;
        int newCols = columns ?? t.Columns;
        if (newRows == t.Rows && newCols == t.Columns)
        {
            return;
        }

        var cells = new List<string>(newRows * newCols);
        for (int r = 0; r < newRows; r++)
        {
            for (int c = 0; c < newCols; c++)
            {
                cells.Add(r < t.Rows && c < t.Columns ? t.CellAt(r, c) : string.Empty);
            }
        }

        var before = new TableLayout(t.Rows, t.Columns, t.Cells, t.ColumnWeights, t.RowWeights);
        var after = new TableLayout(
            newRows, newCols, cells,
            Enumerable.Range(0, newCols).Select(i => i < t.ColumnWeights.Count ? t.ColumnWeights[i] : 1.0).ToList(),
            Enumerable.Range(0, newRows).Select(i => i < t.RowWeights.Count ? t.RowWeights[i] : 1.0).ToList());

        _session.Execute(new SetPropertyCommand<TableLayout>(
            "Resize table", () => before, v => v.ApplyTo(t), after));
        RefreshAll();
    }

    private sealed record TableLayout(int Rows, int Columns, List<string> Cells, List<double> ColumnWeights, List<double> RowWeights)
    {
        public void ApplyTo(TableObject t)
        {
            t.Rows = Rows;
            t.Columns = Columns;
            t.Cells = new List<string>(Cells);
            t.ColumnWeights = new List<double>(ColumnWeights);
            t.RowWeights = new List<double>(RowWeights);
        }
    }

    public string? BarcodeValidationMessage
    {
        get
        {
            if (Obj is not BarcodeObject b)
            {
                return null;
            }

            string value = _session.CurrentResolveContext() is { } ctx
                ? new LabelMaker.Core.Variables.VariableResolver(ctx).Resolve(b.Spec.Data)
                : b.Spec.Data;
            BarcodeValidationResult v = BarcodeValidator.Validate(b.Spec, value);
            return v.IsValid ? null : string.Join("; ", v.Issues.Select(i => i.Message));
        }
    }

    // ---- Shape ----
    public static IReadOnlyList<ShapeKind> ShapeKinds { get; } = Enum.GetValues<ShapeKind>();

    public static IReadOnlyList<LabelMaker.Core.Model.Objects.DashStyle> DashStyles { get; } =
        Enum.GetValues<LabelMaker.Core.Model.Objects.DashStyle>();

    /// <summary>
    /// A compact palette for one-click fill/stroke colours. ColorRgba is #RRGGBB(AA),
    /// alpha last — "#00000000" is the transparent entry, not a black one.
    /// </summary>
    /// <summary>The palette offered for every colour, shared with the colour picker.</summary>
    public static IReadOnlyList<string> Swatches { get; } = Controls.ColorPalette.Colors.Select(c => c.Hex).ToList();

    public ShapeKind ShapeKindValue
    {
        get => (Obj as ShapeObject)?.Shape ?? ShapeKind.Rectangle;
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(ShapeKindValue), () => s.Shape, v => s.Shape = v, value);
                OnPropertyChanged(nameof(IsPolygon));
                OnPropertyChanged(nameof(IsRoundRect));
            }
        }
    }

    public bool IsPolygon => (Obj as ShapeObject)?.Shape == ShapeKind.Polygon;

    public bool IsRoundRect => (Obj as ShapeObject)?.Shape == ShapeKind.RoundRectangle;

    public LabelMaker.Core.Model.Objects.DashStyle DashStyleValue
    {
        get => (Obj as ShapeObject)?.DashStyle ?? LabelMaker.Core.Model.Objects.DashStyle.Solid;
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(DashStyleValue), () => s.DashStyle, v => s.DashStyle = v, value);
            }
        }
    }

    public double PolygonPoints
    {
        get => (Obj as ShapeObject)?.PolygonPoints ?? 5;
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(PolygonPoints), () => s.PolygonPoints, v => s.PolygonPoints = v, (int)Math.Clamp(value, 3, 64));
            }
        }
    }

    /// <summary>0 = plain N-gon; 0.1..0.9 turns it into a star with that inner radius.</summary>
    public double StarInnerRatio
    {
        get => Round((Obj as ShapeObject)?.StarInnerRatio ?? 0);
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(StarInnerRatio), () => s.StarInnerRatio, v => s.StarInnerRatio = v, Math.Clamp(value, 0, 0.95));
            }
        }
    }

    public double ArrowHeadSizeMm
    {
        get => Out((Obj as ShapeObject)?.ArrowHeadSizeMm ?? 3);
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(ArrowHeadSizeMm), () => s.ArrowHeadSizeMm, v => s.ArrowHeadSizeMm = Math.Clamp(v, 0.5, 50), In(value));
            }
        }
    }

    public string FillHex
    {
        get => (Obj as ShapeObject)?.Fill.ToHex() ?? "#00000000";
        set
        {
            if (Obj is ShapeObject s && ColorRgba.TryParse(value, out ColorRgba c))
            {
                Set(nameof(FillHex), () => s.Fill, v => s.Fill = v, c);
                OnPropertyChanged(nameof(FillBrush));
                OnPropertyChanged(nameof(FillAlpha));
            }
        }
    }

    public string StrokeHex
    {
        get => (Obj as ShapeObject)?.Stroke.ToHex() ?? "#000000";
        set
        {
            if (Obj is ShapeObject s && ColorRgba.TryParse(value, out ColorRgba c))
            {
                Set(nameof(StrokeHex), () => s.Stroke, v => s.Stroke = v, c);
                OnPropertyChanged(nameof(StrokeBrush));
            }
        }
    }

    /// <summary>Live preview swatches for the two colour buttons.</summary>
    public Brush FillBrush => ToBrush((Obj as ShapeObject)?.Fill ?? ColorRgba.Transparent);

    public Brush StrokeBrush => ToBrush((Obj as ShapeObject)?.Stroke ?? ColorRgba.Black);

    /// <summary>Fill opacity as 0..100, so it can sit on a slider.</summary>
    public double FillAlpha
    {
        get => Math.Round(((Obj as ShapeObject)?.Fill.A ?? 0) / 255.0 * 100);
        set
        {
            if (Obj is ShapeObject s)
            {
                ColorRgba c = s.Fill;
                var next = new ColorRgba(c.R, c.G, c.B, (byte)Math.Clamp(Math.Round(value / 100 * 255), 0, 255));
                Set(nameof(FillAlpha), () => s.Fill, v => s.Fill = v, next);
                OnPropertyChanged(nameof(FillBrush));
                OnPropertyChanged(nameof(FillHex));
            }
        }
    }

    /// <summary>Turns the flat fill into a two-stop linear gradient and back.</summary>
    public bool UseGradient
    {
        get => (Obj as ShapeObject)?.HasGradient == true;
        set
        {
            if (Obj is not ShapeObject s)
            {
                return;
            }

            ColorRgba? next = value
                ? (s.GradientTo ?? Lighten(s.Fill.A == 0 ? new ColorRgba(0x2C, 0x6F, 0xE8) : s.Fill))
                : null;

            if (value && s.Fill.IsTransparent)
            {
                // A gradient from "nothing" is invisible; start from the accent colour.
                Set(nameof(FillHex), () => s.Fill, v => s.Fill = v, new ColorRgba(0x2C, 0x6F, 0xE8));
            }

            Set(nameof(UseGradient), () => s.GradientTo, v => s.GradientTo = v, next);
            OnPropertyChanged(nameof(GradientToHex));
            OnPropertyChanged(nameof(GradientPreview));
            OnPropertyChanged(nameof(FillBrush));
        }
    }

    public string GradientToHex
    {
        get => ((Obj as ShapeObject)?.GradientTo ?? ColorRgba.White).ToHex();
        set
        {
            if (Obj is ShapeObject s && ColorRgba.TryParse(value, out ColorRgba c))
            {
                Set(nameof(GradientToHex), () => s.GradientTo, v => s.GradientTo = v, c);
                OnPropertyChanged(nameof(GradientPreview));
            }
        }
    }

    public double GradientAngle
    {
        get => Math.Round((Obj as ShapeObject)?.GradientAngleDegrees ?? 90);
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(GradientAngle), () => s.GradientAngleDegrees, v => s.GradientAngleDegrees = v,
                    Math.Round(Math.Clamp(value, 0, 360)));
                OnPropertyChanged(nameof(GradientPreview));
            }
        }
    }

    /// <summary>Live preview of the gradient exactly as the renderer will draw it.</summary>
    public Brush GradientPreview
    {
        get
        {
            if (Obj is not ShapeObject { GradientTo: { } to } s)
            {
                return FillBrush;
            }

            double radians = s.GradientAngleDegrees * Math.PI / 180.0;
            double dx = Math.Cos(radians) / 2.0;
            double dy = Math.Sin(radians) / 2.0;
            return new LinearGradientBrush(
                Color.FromArgb(s.Fill.A, s.Fill.R, s.Fill.G, s.Fill.B),
                Color.FromArgb(to.A, to.R, to.G, to.B),
                new System.Windows.Point(0.5 - dx, 0.5 - dy),
                new System.Windows.Point(0.5 + dx, 0.5 + dy));
        }
    }

    private static ColorRgba Lighten(ColorRgba c) => new(
        (byte)Math.Min(255, c.R + 70), (byte)Math.Min(255, c.G + 70), (byte)Math.Min(255, c.B + 70), c.A);

    public double StrokeWidthMm
    {
        get => Out((Obj as ShapeObject)?.StrokeWidthMm ?? 0.25);
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(StrokeWidthMm), () => s.StrokeWidthMm, v => s.StrokeWidthMm = Math.Clamp(v, 0, 20), In(value));
            }
        }
    }

    public double CornerRadiusMm
    {
        get => Out((Obj as ShapeObject)?.CornerRadiusMm ?? 0);
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(CornerRadiusMm), () => s.CornerRadiusMm, v => s.CornerRadiusMm = Math.Max(0, v), In(value));
            }
        }
    }

    /// <summary>Applies a palette swatch or picked colour to the fill, stroke or gradient end.</summary>
    public void ApplySwatch(string target, string hex)
    {
        switch (target)
        {
            case "stroke":
                StrokeHex = hex;
                break;
            case "gradient":
                GradientToHex = hex;
                break;
            case "text":
                TextColorHex = hex;
                break;
            case "highlight":
                HighlightHex = hex;
                break;
            case "bar":
                BarColorHex = hex;
                break;
            case "barbg":
                BarBackgroundHex = hex;
                break;
            case "tablefill":
                TableHeaderFillHex = hex;
                break;
            case "tableborder":
                TableBorderHex = hex;
                break;
            default:
                FillHex = hex;
                break;
        }
    }

    private static Brush ToBrush(ColorRgba c) =>
        new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));

    // ---- Image ----
    public IReadOnlyList<ImageFitMode> ImageFitModes { get; } = Enum.GetValues<ImageFitMode>();

    public ImageFitMode FitMode
    {
        get => (Obj as ImageObject)?.FitMode ?? ImageFitMode.Fit;
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(FitMode), () => i.FitMode, v => i.FitMode = v, value);
            }
        }
    }

    /// <summary>0..1 (LabelObject.Opacity is shared by every object type, surfaced here for images).</summary>
    public double ImageOpacity
    {
        get => Round(Obj?.Opacity ?? 1.0);
        set => Set(nameof(ImageOpacity), () => Obj!.Opacity, v => Obj!.Opacity = v, Math.Clamp(value, 0, 1));
    }

    public double Brightness
    {
        get => Round((Obj as ImageObject)?.Adjustments.Brightness ?? 0);
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(Brightness), () => i.Adjustments.Brightness, v => i.Adjustments.Brightness = v, Math.Clamp(value, -1, 1));
            }
        }
    }

    public double Contrast
    {
        get => Round((Obj as ImageObject)?.Adjustments.Contrast ?? 0);
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(Contrast), () => i.Adjustments.Contrast, v => i.Adjustments.Contrast = v, Math.Clamp(value, -1, 1));
            }
        }
    }

    public bool Grayscale
    {
        get => (Obj as ImageObject)?.Adjustments.Grayscale ?? false;
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(Grayscale), () => i.Adjustments.Grayscale, v => i.Adjustments.Grayscale = v, value);
            }
        }
    }

    public bool FlipHorizontal
    {
        get => (Obj as ImageObject)?.Adjustments.FlipHorizontal ?? false;
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(FlipHorizontal), () => i.Adjustments.FlipHorizontal, v => i.Adjustments.FlipHorizontal = v, value);
            }
        }
    }

    public bool FlipVertical
    {
        get => (Obj as ImageObject)?.Adjustments.FlipVertical ?? false;
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(FlipVertical), () => i.Adjustments.FlipVertical, v => i.Adjustments.FlipVertical = v, value);
            }
        }
    }

    public string? ImageSourceFileName => (Obj as ImageObject)?.SourceFileName;

    /// <summary>
    /// Stretch means "fill the box, ignore the aspect ratio". Exposed as a plain
    /// switch because that is the question people actually ask when resizing.
    /// </summary>
    public bool AllowStretch
    {
        get => (Obj as ImageObject)?.FitMode == ImageFitMode.Stretch;
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(AllowStretch), () => i.FitMode, v => i.FitMode = v,
                    value ? ImageFitMode.Stretch : ImageFitMode.Fit);
                OnPropertyChanged(nameof(FitMode));
            }
        }
    }

    public string CropSummary
    {
        get
        {
            if (Obj is not ImageObject i)
            {
                return "—";
            }

            ImageAdjustments a = i.Adjustments;
            return a.HasCrop
                ? $"{a.CropWidth * 100:0}% × {a.CropHeight * 100:0}% from ({a.CropX * 100:0}%, {a.CropY * 100:0}%)"
                : "Whole image";
        }
    }

    public bool HasCrop => (Obj as ImageObject)?.Adjustments.HasCrop == true;

    public static IReadOnlyList<ImageClipShape> ClipShapes { get; } = Enum.GetValues<ImageClipShape>();

    /// <summary>Crop-to-shape: clips the image to a circle or rounded box.</summary>
    public ImageClipShape ClipShape
    {
        get => (Obj as ImageObject)?.ClipShape ?? ImageClipShape.None;
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(ClipShape), () => i.ClipShape, v => i.ClipShape = v, value);
                OnPropertyChanged(nameof(IsRoundedClip));
            }
        }
    }

    public bool IsRoundedClip => (Obj as ImageObject)?.ClipShape == ImageClipShape.RoundedRectangle;

    public double ClipCornerRadiusMm
    {
        get => Out((Obj as ImageObject)?.ClipCornerRadiusMm ?? 3);
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(ClipCornerRadiusMm), () => i.ClipCornerRadiusMm, v => i.ClipCornerRadiusMm = Math.Max(0, v), In(value));
            }
        }
    }

    /// <summary>Effective print resolution at the current placed size — under ~200 DPI looks soft.</summary>
    public string ImageResolution
    {
        get
        {
            if (Obj is not ImageObject i || i.SourcePixelWidth <= 0)
            {
                return "—";
            }

            double dpi = i.EffectiveDpiAt(i.Bounds.Width * Math.Clamp(i.Adjustments.CropWidth, 0.01, 1));
            return $"{i.SourcePixelWidth} × {i.SourcePixelHeight} px · {dpi:0} DPI at this size";
        }
    }

    public void ClearCrop()
    {
        if (Obj is ImageObject i)
        {
            ImageAdjustments next = i.Adjustments.Clone();
            next.CropX = 0;
            next.CropY = 0;
            next.CropWidth = 1;
            next.CropHeight = 1;
            Set("Reset crop", () => i.Adjustments, v => i.Adjustments = v, next);
        }
    }

    public void RefreshAll() => OnPropertyChanged(null);

    private void SetBounds(Func<RectMm, RectMm> mutate)
    {
        if (Obj is not { } o)
        {
            return;
        }

        RectMm target = mutate(o.Bounds);
        var command = new SetPropertyCommand<RectMm>(
            "Edit bounds", () => o.Bounds, v => o.Bounds = v, target, (o.Id, "bounds"));
        _session.Execute(command);
        RefreshAll();
    }

    private void Set<T>(string name, Func<T> getter, Action<T> setter, T value)
    {
        if (Obj is not { } o || EqualityComparer<T>.Default.Equals(getter(), value))
        {
            return;
        }

        var command = new SetPropertyCommand<T>($"Edit {name}", getter, setter, value, (o.Id, name));
        _session.Execute(command);
        RefreshAll();
    }

    private static double Round(double v) => Math.Round(v, 2);
}
