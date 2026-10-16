using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;

namespace LabelMaker.Core.Printing.Thermal;

/// <summary>
/// How one design prints in <see cref="ThermalSendMode.PrinterNative"/>: which objects the printer
/// draws itself, which go into the background picture stored on the printer once, and which are
/// sent as a small picture with every label.
/// <list type="bullet">
/// <item>Barcodes of a type both printer languages have (Code 128, GS1-128, EAN, UPC-A, Code 39,
/// Code 93, Interleaved 2 of 5, Codabar, QR, Data Matrix) are drawn by the printer, so their
/// bars land on whole dots.</item>
/// <item>Text that changes from label to label is drawn in the printer's own font, line by line
/// where LabelAxis breaks the lines.</item>
/// <item>Everything that never changes (fixed text, shapes, pictures, the label background) is
/// the stored background, so it keeps its exact look and is sent only once per job.</item>
/// <item>Changing things the printer cannot draw (a changing table, light text on a dark box,
/// text at an angle, Arabic text) are sent as a picture of just their area, which is left
/// blank in the background so the picture prints exactly as on screen.</item>
/// </list>
/// A value the printer's font or barcode command cannot carry (say a name with letters
/// beyond Western European ones) is sent as a picture for that label only.
/// </summary>
public sealed class NativeDesign
{
    private readonly HashSet<LabelObject> _drawn;
    private readonly HashSet<LabelObject> _pictures;

    private NativeDesign(LabelDocument document, List<LabelObject> drawn, List<LabelObject> pictures)
    {
        Document = document;
        Drawn = drawn;
        Pictures = pictures;
        _drawn = new HashSet<LabelObject>(drawn);
        _pictures = new HashSet<LabelObject>(pictures);
    }

    public LabelDocument Document { get; }

    /// <summary>Objects the printer draws itself, in drawing order.</summary>
    public IReadOnlyList<LabelObject> Drawn { get; }

    /// <summary>Changing objects always sent as a picture of their area.</summary>
    public IReadOnlyList<LabelObject> Pictures { get; }

    public static NativeDesign For(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var drawn = new List<LabelObject>();
        var pictures = new List<LabelObject>();
        foreach (LabelObject obj in document.Objects)
        {
            if (!obj.Visible)
            {
                continue;
            }

            if (obj is BarcodeObject barcode && CanDraw(barcode))
            {
                drawn.Add(obj);
            }
            else if (IsVariable(obj))
            {
                (obj is TextObject text && CanDraw(text) ? drawn : pictures).Add(obj);
            }
        }

        return new NativeDesign(document, drawn, pictures);
    }

    /// <summary>The design without the objects drawn by the printer or sent as pictures: what is stored once.</summary>
    public LabelDocument BackgroundDocument() => new()
    {
        Id = Document.Id,
        Name = Document.Name,
        SizeMm = Document.SizeMm,
        Background = Document.Background,
        Objects = Document.Objects.Where(o => !_drawn.Contains(o) && !_pictures.Contains(o)).ToList(),
    };

    /// <summary>True for objects whose words or value come from a record, a variable, a counter or the date.</summary>
    public static bool IsVariable(LabelObject obj) => obj switch
    {
        TextObject text => text.Content.PlainText.Contains('{', StringComparison.Ordinal),
        BarcodeObject barcode => barcode.Spec.Data.Contains('{', StringComparison.Ordinal),
        TableObject table => table.Cells.Any(c => c.Contains('{', StringComparison.Ordinal)),
        _ => false,
    };

    /// <summary>Dark, upright (or a quarter turn), unstyled text on nothing: what a printer font can stand in for.</summary>
    public static bool CanDraw(TextObject text)
    {
        if (!IsSquare(text) || text.Opacity < 0.99 || text.Direction == TextDirection.RightToLeft ||
            !text.Background.IsTransparent || text.BackgroundTo is not null ||
            text.Underline || text.Strikethrough || !IsDark(text.Foreground))
        {
            return false;
        }

        return text.Content.Paragraphs.SelectMany(p => p.Runs).All(r =>
            r.Underline != true && r.Strikethrough != true &&
            (r.Foreground is not { } fg || IsDark(fg)) &&
            (r.Background is not { } bg || bg.IsTransparent));
    }

    public static bool CanDraw(BarcodeObject barcode)
    {
        BarcodeSpec spec = barcode.Spec;
        return IsSquare(barcode) && barcode.Opacity >= 0.99 && SymbologyOf(spec) is not null &&
               IsDark(spec.Foreground) && (spec.Background.IsTransparent || !IsDark(spec.Background));
    }

    /// <summary>The printer's barcode for this spec, or null when the printer has none like it.</summary>
    public static NativeSymbology? SymbologyOf(BarcodeSpec spec) => spec.Symbology switch
    {
        _ when spec.Symbology.IsCode128Family() => NativeSymbology.Code128,
        // GS1 inside a QR / Data Matrix needs FNC1 handling the printers do differently: sent as a picture.
        BarcodeSymbology.QrCode when !spec.Gs1Mode => NativeSymbology.QrCode,
        BarcodeSymbology.DataMatrix when !spec.Gs1Mode => NativeSymbology.DataMatrix,
        BarcodeSymbology.Ean13 or BarcodeSymbology.Isbn13 => NativeSymbology.Ean13,
        BarcodeSymbology.Ean8 => NativeSymbology.Ean8,
        BarcodeSymbology.UpcA => NativeSymbology.UpcA,
        BarcodeSymbology.Code39 => NativeSymbology.Code39,
        BarcodeSymbology.Code93 => NativeSymbology.Code93,
        BarcodeSymbology.Interleaved2of5 => NativeSymbology.Interleaved2of5,
        BarcodeSymbology.Codabar => NativeSymbology.Codabar,
        _ => null,
    };

    /// <summary>Printers turn text and barcodes only by quarter turns, and never slant them.</summary>
    internal static bool IsSquare(LabelObject obj) =>
        QuarterTurn(obj.RotationDegrees) is not null && Math.Abs(obj.SkewXDegrees) < 0.01 && Math.Abs(obj.SkewYDegrees) < 0.01;

    internal static NativeRotation? QuarterTurn(double degrees)
    {
        double d = ((degrees % 360) + 360) % 360;
        foreach (NativeRotation r in Enum.GetValues<NativeRotation>())
        {
            double diff = Math.Abs(d - (int)r);
            if (diff < 0.01 || Math.Abs(diff - 360) < 0.01)
            {
                return r;
            }
        }

        return null;
    }

    /// <summary>Prints as black on a thermal printer (it has no other ink).</summary>
    internal static bool IsDark(ColorRgba c) =>
        c.A > 127 && ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) < 128;
}
