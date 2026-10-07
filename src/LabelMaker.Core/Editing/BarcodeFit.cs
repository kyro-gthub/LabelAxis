using System.Globalization;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Units;

namespace LabelMaker.Core.Editing;

/// <summary>
/// Keeps a barcode that is drawn at its natural size (<see cref="BarcodeObject.AutoSize"/>) and
/// its box the same size, so the selection on the canvas is always the barcode itself:
/// <list type="bullet">
/// <item>when the box is resized, the bars are made to fill it (narrower or wider bars, a
/// lower or taller barcode) and the box takes the barcode's exact size;</item>
/// <item>when what changes is the barcode (its data, type, bar width, text…), the box takes the
/// barcode's new size around the same centre.</item>
/// </list>
/// </summary>
public static class BarcodeFit
{
    /// <summary>The thinnest bar a resize may make (a 600 dpi printer's dot).</summary>
    public const double MinModuleMm = 0.042;

    /// <summary>Each natural-size barcode's box and size-setting settings, before an edit.</summary>
    public static Dictionary<BarcodeObject, (RectMm Bounds, string Settings)> Snapshot(LabelDocument? document) =>
        document is null
            ? new Dictionary<BarcodeObject, (RectMm, string)>()
            : document.Objects.OfType<BarcodeObject>().Where(b => b.AutoSize)
                .ToDictionary(b => b, b => (b.Bounds, Settings(b.Spec)));

    /// <summary>Everything about a barcode's settings that changes its size, as one string to compare.</summary>
    public static string Settings(BarcodeSpec s) => string.Join('\u001f',
        s.Symbology, s.Data, s.ModuleWidthMm.ToString("R", CultureInfo.InvariantCulture), s.BarHeightMm.ToString("R", CultureInfo.InvariantCulture),
        s.HumanReadable, s.HumanReadableFontSizePt.ToString("R", CultureInfo.InvariantCulture), s.QuietZoneModules, s.Gs1Mode,
        s.AddCheckDigit, s.QrEcLevel, s.Pdf417Columns, s.CharacterEncoding);

    /// <summary>
    /// After an edit: fits every natural-size barcode and its box together (see the class
    /// summary), returning what changed so it can be undone with the edit.
    /// </summary>
    public static List<(BarcodeObject Barcode, RectMm BoundsBefore, RectMm BoundsAfter, BarcodeSpec SpecBefore, BarcodeSpec SpecAfter)> Fit(
        LabelDocument? document, IReadOnlyDictionary<BarcodeObject, (RectMm Bounds, string Settings)> before, Func<string, string>? resolve)
    {
        var changed = new List<(BarcodeObject, RectMm, RectMm, BarcodeSpec, BarcodeSpec)>();
        if (document is null)
        {
            return changed;
        }

        foreach (BarcodeObject barcode in document.Objects.OfType<BarcodeObject>().Where(b => b.AutoSize && !b.Locked))
        {
            string value = resolve?.Invoke(barcode.Spec.Data) ?? barcode.Spec.Data;
            bool resized = before.TryGetValue(barcode, out var was) && was.Settings == Settings(barcode.Spec) &&
                           (Math.Abs(was.Bounds.Width - barcode.Bounds.Width) > 0.01 || Math.Abs(was.Bounds.Height - barcode.Bounds.Height) > 0.01);
            RectMm boundsBefore = barcode.Bounds;
            BarcodeSpec specBefore = barcode.Spec;
            if (resized)
            {
                if (ToBox(barcode.Spec, barcode.Bounds, value) is { } fitted)
                {
                    barcode.Spec = fitted.Spec;
                    barcode.Bounds = fitted.Bounds;
                }
            }
            else if (NaturalSize(barcode.Spec, value) is { } natural &&
                     (Math.Abs(natural.Width - barcode.Bounds.Width) > 0.01 || Math.Abs(natural.Height - barcode.Bounds.Height) > 0.01))
            {
                // The barcode is drawn centred in its box, so keeping the centre keeps the bars still.
                barcode.Bounds = RectMm.FromCenter(barcode.Bounds.Center, natural);
            }

            if (barcode.Bounds != boundsBefore || !ReferenceEquals(barcode.Spec, specBefore))
            {
                changed.Add((barcode, boundsBefore, barcode.Bounds, specBefore, barcode.Spec));
            }
        }

        return changed;
    }

    /// <summary>The size a barcode is drawn at, or null when its value cannot be encoded (yet).</summary>
    public static SizeMm? NaturalSize(BarcodeSpec spec, string value) =>
        BarcodeEngine.TryEncode(spec, value, out BarcodeSymbol? symbol, out _) && symbol is not null
            ? BarcodeLayout.NaturalSize(symbol, spec)
            : null;

    /// <summary>
    /// The barcode made to fill <paramref name="box"/>: bars as wide as the box allows (a square
    /// code as large as fits), as tall as the box less the text, and the box it then takes,
    /// from the box's top-left corner. Null when the value cannot be encoded.
    /// </summary>
    public static (BarcodeSpec Spec, RectMm Bounds)? ToBox(BarcodeSpec spec, RectMm box, string value)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!BarcodeEngine.TryEncode(spec, value, out BarcodeSymbol? symbol, out _) || symbol is null)
        {
            return null;
        }

        BarcodeSpec fitted = spec.Clone();
        SizeMm natural = BarcodeLayout.NaturalSize(symbol, spec);
        double text = natural.Height - (symbol.Is2D ? natural.Width : spec.BarHeightMm);
        if (symbol.Is2D)
        {
            double side = Math.Max(0.5, Math.Min(box.Width, box.Height - text));
            fitted.ModuleWidthMm = Math.Max(MinModuleMm, spec.ModuleWidthMm * side / Math.Max(0.01, natural.Width));
        }
        else
        {
            fitted.ModuleWidthMm = Math.Max(MinModuleMm, spec.ModuleWidthMm * box.Width / Math.Max(0.01, natural.Width));
            fitted.BarHeightMm = Math.Max(0.5, box.Height - text);
        }

        SizeMm size = BarcodeLayout.NaturalSize(symbol, fitted);
        return (fitted, new RectMm(box.X, box.Y, size.Width, size.Height));
    }
}
