using System.Collections.ObjectModel;
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
/// in the property grid participate in undo/redo (spec §21, §31). The panel for each kind
/// of object is in its own file (SelectionProperties.Text.cs and so on).
/// </summary>
public sealed partial class SelectionProperties : ObservableObject
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

    /// <summary>
    /// Font family names for the Font picker (spec: choose a font from a list, not free text):
    /// the installed fonts, the Arabic fonts that come with LabelAxis and the fonts the user
    /// added, kept up to date as fonts are added or removed.
    /// </summary>
    public static ObservableCollection<string> FontFamilies { get; } = BuildFontFamilies();

    private static ObservableCollection<string> BuildFontFamilies()
    {
        var list = new ObservableCollection<string>(AllFontFamilies());
        Rendering.CustomFonts.Changed += (_, _) =>
        {
            List<string> now = AllFontFamilies();
            foreach (string gone in list.Except(now).ToList())
            {
                list.Remove(gone);
            }

            for (int i = 0; i < now.Count; i++)
            {
                if (i >= list.Count || !string.Equals(list[i], now[i], StringComparison.Ordinal))
                {
                    list.Insert(i, now[i]);
                }
            }
        };
        return list;
    }

    private static List<string> AllFontFamilies() =>
        Fonts.SystemFontFamilies.Select(f => f.Source)
            .Concat(Rendering.BundledFonts.Names)       // the Arabic fonts that come with LabelAxis
            .Concat(Rendering.CustomFonts.Names)        // and the ones added from font files
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

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

    // ---- shared by every kind ----

    private void Execute(string label, IReadOnlyList<IUndoableCommand> commands)
    {
        if (commands.Count == 0)
        {
            return;
        }

        _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand(label, commands));
        RefreshAll();
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
            case "imageborder":
                ImageBorderHex = hex;
                break;
            default:
                FillHex = hex;
                break;
        }
    }

    /// <summary>
    /// Sets a fill from the colour window, a single colour or a gradient, as one undo step:
    /// a shape's fill (<c>"fill"</c>) or a text box's background (<c>"highlight"</c>).
    /// </summary>
    public void ApplyFill(string target, FillChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        double angle = Math.Round(((choice.AngleDegrees % 360) + 360) % 360);
        var commands = new List<IUndoableCommand>();
        if (target == "fill" && Obj is ShapeObject s)
        {
            Add(commands, "Fill", () => s.Fill, v => s.Fill = v, choice.Color);
            Add(commands, "Fill", () => s.GradientTo, v => s.GradientTo = v, choice.GradientTo);
            if (choice.GradientTo is not null)
            {
                Add(commands, "Fill", () => s.GradientAngleDegrees, v => s.GradientAngleDegrees = v, angle);
            }
        }
        else if (target == "highlight" && Obj is TextObject t)
        {
            Add(commands, "Highlight", () => t.Background, v => t.Background = v, choice.Color);
            Add(commands, "Highlight", () => t.BackgroundTo, v => t.BackgroundTo = v, choice.GradientTo);
            if (choice.GradientTo is not null)
            {
                Add(commands, "Highlight", () => t.BackgroundAngleDegrees, v => t.BackgroundAngleDegrees = v, angle);
            }

            // Highlights set on single words would hide the new background, as with a plain colour.
            RichTextContent content = t.Content.Clone();
            bool hadOverrides = false;
            foreach (TextRun run in content.Paragraphs.SelectMany(p => p.Runs))
            {
                hadOverrides |= run.Background is not null;
                run.Background = null;
            }

            if (hadOverrides)
            {
                commands.Add(new SetPropertyCommand<RichTextContent>("Edit Highlight", () => t.Content, v => t.Content = v, content));
            }
        }
        else
        {
            ApplySwatch(target, choice.Color.ToHex());
            return;
        }

        Execute(choice.GradientTo is null ? "Edit colour" : "Edit gradient", commands);
    }

    private static void Add<T>(List<IUndoableCommand> commands, string name, Func<T> get, Action<T> set, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(get(), value))
        {
            commands.Add(new SetPropertyCommand<T>($"Edit {name}", get, set, value));
        }
    }

    /// <summary>The little colour square beside a colour's name (see <see cref="Controls.ColourBrushes.Swatch(ColorRgba)"/>).</summary>
    private static Brush ToBrush(ColorRgba c) => Controls.ColourBrushes.Swatch(c);

    private static Brush ToBrush(ColorRgba from, ColorRgba to, double angleDegrees) => Controls.ColourBrushes.Swatch(from, to, angleDegrees);

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
