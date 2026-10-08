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

/// <summary>The Properties panel for a barcode: kind, value, size, digits and colours.</summary>
public sealed partial class SelectionProperties
{
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

    /// <summary>Barcodes that can carry GS1 data: GS1-128 (Code 128), GS1 DataMatrix and GS1 QR.</summary>
    public bool CanBeGs1 => Spec?.Symbology is { } s && (s.IsCode128Family() || s is BarcodeSymbology.DataMatrix or BarcodeSymbology.QrCode);

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
}
