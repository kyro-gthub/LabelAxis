using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Model.Text;

namespace LabelMaker.Core.Printing.Thermal;

/// <summary>A rectangle in print-head dots, measured from the top-left of the label.</summary>
public readonly record struct DotRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public DotRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };
}

/// <summary>Quarter turns clockwise, the only turns printers draw text and barcodes at.</summary>
public enum NativeRotation
{
    None = 0,
    Quarter = 90,
    Half = 180,
    ThreeQuarter = 270,
}

/// <summary>The barcode commands both printer languages share.</summary>
public enum NativeSymbology
{
    Code128,
    Ean13,
    Ean8,
    UpcA,
    Code39,
    Code93,
    Interleaved2of5,
    Codabar,
    QrCode,
    DataMatrix,
}

/// <summary>One thing the printer draws on a label.</summary>
public abstract record NativeElement;

/// <summary>
/// One line of text in the printer's own font. <see cref="Box"/> is where the line goes on the
/// label (after turning); the text is aligned inside the box along its own direction.
/// </summary>
public sealed record NativeText(DotRect Box, int FontDots, HorizontalAlignment Alignment, NativeRotation Rotation, string Text) : NativeElement;

/// <summary>
/// A barcode the printer draws. <see cref="Box"/> covers the bars only (no quiet zone, no text
/// under it; that is a separate <see cref="NativeText"/>). For Code 128 the printer is given
/// <see cref="Code128Codewords"/>, the exact code values LabelAxis encodes, so its bars match.
/// </summary>
public sealed record NativeBarcode(
    DotRect Box,
    NativeRotation Rotation,
    NativeSymbology Symbology,
    string Data,
    int ModuleDots,
    int BarHeightDots,
    double WideRatio,
    QrErrorCorrection ErrorCorrection,
    IReadOnlyList<int>? Code128Codewords) : NativeElement;

/// <summary>A small picture for something the printer cannot draw itself (a photo, Arabic text, a light-on-dark field).</summary>
public sealed record NativePicture(int X, int Y, MonochromeImage Image) : NativeElement;

/// <summary>One label of a printed row: where it sits, which stored background it uses, and what is drawn over it.</summary>
public sealed class NativeLabel
{
    /// <summary>The label's top-left in the row, in dots.</summary>
    public int OffsetX { get; init; }

    public int OffsetY { get; init; }

    /// <summary>Index of the stored background (one per distinct design in the job).</summary>
    public int Background { get; init; }

    public List<NativeElement> Elements { get; } = new();
}

/// <summary>One printer "label": a row of labels across the roll.</summary>
public sealed class NativeRow
{
    public required int WidthDots { get; init; }

    public required int HeightDots { get; init; }

    public List<NativeLabel> Labels { get; } = new();
}
