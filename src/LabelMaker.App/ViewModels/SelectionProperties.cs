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

    public SelectionProperties(DocumentSession session)
    {
        _session = session;
        _session.SelectionChanged += (_, _) => RefreshAll();
        _session.DocumentChanged += (_, _) => RefreshAll();
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
        get => Round(Obj?.Bounds.X ?? 0);
        set => SetBounds(b => b with { X = value });
    }

    public double Y
    {
        get => Round(Obj?.Bounds.Y ?? 0);
        set => SetBounds(b => b with { Y = value });
    }

    public double Width
    {
        get => Round(Obj?.Bounds.Width ?? 0);
        set => SetBounds(b => b with { Width = Math.Max(0.1, value) });
    }

    public double Height
    {
        get => Round(Obj?.Bounds.Height ?? 0);
        set => SetBounds(b => b with { Height = Math.Max(0.1, value) });
    }

    public double RotationDegrees
    {
        get => Round(Obj?.RotationDegrees ?? 0);
        set => Set(nameof(RotationDegrees), () => Obj!.RotationDegrees, v => Obj!.RotationDegrees = v, value);
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
    public string TextContent
    {
        get => (Obj as TextObject)?.Content.PlainText ?? string.Empty;
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(TextContent), () => t.Content, c => t.Content = c, RichTextContent.FromPlainText(value));
            }
        }
    }

    public double FontSizePt
    {
        get => (Obj as TextObject)?.FontSizePt ?? 10;
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(FontSizePt), () => t.FontSizePt, v => t.FontSizePt = Math.Clamp(v, 2, 400), value);
            }
        }
    }

    public string FontFamily
    {
        get => (Obj as TextObject)?.FontFamily ?? "Segoe UI";
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(FontFamily), () => t.FontFamily, v => t.FontFamily = v, value);
            }
        }
    }

    public bool Bold
    {
        get => (Obj as TextObject)?.Bold ?? false;
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(Bold), () => t.Bold, v => t.Bold = v, value);
            }
        }
    }

    public bool Italic
    {
        get => (Obj as TextObject)?.Italic ?? false;
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(Italic), () => t.Italic, v => t.Italic = v, value);
            }
        }
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
    public string FillHex
    {
        get => (Obj as ShapeObject)?.Fill.ToHex() ?? "#00000000";
        set
        {
            if (Obj is ShapeObject s && ColorRgba.TryParse(value, out ColorRgba c))
            {
                Set(nameof(FillHex), () => s.Fill, v => s.Fill = v, c);
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
            }
        }
    }

    public double StrokeWidthMm
    {
        get => (Obj as ShapeObject)?.StrokeWidthMm ?? 0.25;
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(StrokeWidthMm), () => s.StrokeWidthMm, v => s.StrokeWidthMm = Math.Clamp(v, 0, 20), value);
            }
        }
    }

    public double CornerRadiusMm
    {
        get => (Obj as ShapeObject)?.CornerRadiusMm ?? 0;
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(CornerRadiusMm), () => s.CornerRadiusMm, v => s.CornerRadiusMm = Math.Max(0, v), value);
            }
        }
    }

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
