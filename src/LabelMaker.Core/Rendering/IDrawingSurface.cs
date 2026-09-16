using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Units;

namespace LabelMaker.Core.Rendering;

/// <summary>A single styled span to draw (already resolved — no tokens left).</summary>
public sealed record TextSpan
{
    public required string Text { get; init; }
    public required string FontFamily { get; init; }
    public required double FontSizePt { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }
    public ColorRgba Foreground { get; init; } = ColorRgba.Black;
    public ColorRgba? Background { get; init; }
    public double LetterSpacingEm { get; init; }
}

public sealed class TextBlock
{
    public required IReadOnlyList<TextLine> Lines { get; init; }

    /// <summary>From the top of the first line to the bottom of the last line's descenders.</summary>
    public double TotalHeightMm { get; init; }

    /// <summary>From the top of the first line down to the top of its capital letters (the font's empty space above them).</summary>
    public double CapTopMm { get; init; }

    /// <summary>From the top of the first line down to the last line's baseline.</summary>
    public double LastBaselineMm { get; init; }

    /// <summary>Height of the visible text: from the capitals' tops to the bottom of the descenders.</summary>
    public double InkHeightMm => Math.Max(0, TotalHeightMm - CapTopMm);

    /// <summary>
    /// Where the first line's top goes, measured down from the top of a box
    /// <paramref name="boxHeightMm"/> tall, so the letters themselves sit where asked: Top puts
    /// the capitals against the top edge, Bottom the descenders against the bottom edge, and
    /// Middle centres the capitals' tops and the last baseline. The font's empty space above the
    /// capitals used to count as text, so top-aligned text started well below the box's edge
    /// and centred text sat low. Text taller than the box starts at the top.
    /// </summary>
    public double TopOffsetMm(VerticalAlignment alignment, double boxHeightMm)
    {
        double top = -CapTopMm;
        if (InkHeightMm > boxHeightMm + 0.01)
        {
            return top;
        }

        return alignment switch
        {
            VerticalAlignment.Bottom => boxHeightMm - TotalHeightMm,
            VerticalAlignment.Middle => Math.Max(top, Math.Min(((boxHeightMm - (LastBaselineMm - CapTopMm)) / 2) - CapTopMm, boxHeightMm - TotalHeightMm)),
            _ => top,
        };
    }
}

public sealed class TextLine
{
    public required IReadOnlyList<TextSpan> Spans { get; init; }
    public double WidthMm { get; init; }
    public double HeightMm { get; init; }
    public double BaselineOffsetMm { get; init; }

    /// <summary>Height of the tallest capital letters on the line, above the baseline.</summary>
    public double CapHeightMm { get; init; }

    /// <summary>Extra space after each word-ending space, for justified lines; 0 otherwise.</summary>
    public double WordGapMm { get; init; }

    public HorizontalAlignment Alignment { get; init; }
}

/// <summary>Opaque, surface-native decoded image handle produced by an <see cref="IImageSource"/>.</summary>
public interface IDecodedImage
{
    int PixelWidth { get; }
    int PixelHeight { get; }
}

public interface IImageSource
{
    /// <summary>Return a cached decoded image for an asset id, or null if unavailable.</summary>
    IDecodedImage? Get(string assetId);
}

/// <summary>Measures resolved text so the portable layouter can wrap and autofit it.</summary>
public interface ITextMeasurer
{
    /// <summary>Width in millimetres of <paramref name="text"/> at the given face/size (no wrapping).</summary>
    double MeasureWidthMm(string text, string fontFamily, double fontSizePt, bool bold, bool italic);

    /// <summary>Line height in millimetres for the given face/size.</summary>
    double LineHeightMm(string fontFamily, double fontSizePt);

    /// <summary>Distance from the top of a line to its baseline. Measurers without real metrics use a typical 80%.</summary>
    double BaselineMm(string fontFamily, double fontSizePt) => LineHeightMm(fontFamily, fontSizePt) * 0.8;

    /// <summary>Height of capital letters above the baseline. Measurers without real metrics use 70% of the font size.</summary>
    double CapHeightMm(string fontFamily, double fontSizePt) => UnitConverter.PointsToMm(fontSizePt) * 0.7;
}

public enum LabelOutlineStyle
{
    None = 0,
    Hairline = 1,
    Crop = 2,
}

/// <summary>
/// The one drawing contract the <see cref="LabelRenderer"/> targets. All
/// coordinates are millimetres in the current transform space. Implemented by
/// WPF (<c>DrawingContext</c>), PdfSharp (<c>XGraphics</c>) and SVG. Nothing here
/// rasterises — 1D/2D barcodes arrive as rectangle lists.
/// </summary>
public interface IDrawingSurface
{
    ITextMeasurer TextMeasurer { get; }

    IImageSource Images { get; }

    void PushTransform(double translateXMm, double translateYMm, double rotationDegrees, double scaleX, double scaleY);

    /// <summary>
    /// Pushes a general affine transform (needed for skew). <paramref name="m"/>'s
    /// translation is in millimetres. The default only keeps translation and
    /// rotation, so a surface that cannot shear still draws the object in place;
    /// every shipping surface overrides it.
    /// </summary>
    void PushMatrix(Model.Affine m) =>
        PushTransform(m.E, m.F, Math.Atan2(m.B, m.A) * 180.0 / Math.PI, 1, 1);

    void Pop();

    void PushOpacity(double opacity);

    void PushClipRectangle(RectMm rectMm, double cornerRadiusMm = 0);

    void PushClipEllipse(RectMm rectMm);

    void FillRectangle(RectMm rectMm, ColorRgba fill, double cornerRadiusMm = 0);

    /// <summary>
    /// Two-stop linear gradient fill. Implemented as a default method so a surface
    /// that cannot express gradients still draws something sensible (the start
    /// colour) rather than nothing at all.
    /// </summary>
    void FillRectangleGradient(
        RectMm rectMm, ColorRgba from, ColorRgba to, double angleDegrees, double cornerRadiusMm = 0) =>
        FillRectangle(rectMm, from, cornerRadiusMm);

    void FillEllipseGradient(RectMm rectMm, ColorRgba from, ColorRgba to, double angleDegrees) =>
        FillEllipse(rectMm, from);

    void FillPolygonGradient(
        IReadOnlyList<PointMm> points, ColorRgba from, ColorRgba to, double angleDegrees,
        ColorRgba stroke, double strokeWidthMm) =>
        FillPolygon(points, from, stroke, strokeWidthMm);

    void StrokeRectangle(RectMm rectMm, ColorRgba stroke, double strokeWidthMm, DashStyle dash = DashStyle.Solid, double cornerRadiusMm = 0);

    void FillEllipse(RectMm rectMm, ColorRgba fill);

    void StrokeEllipse(RectMm rectMm, ColorRgba stroke, double strokeWidthMm, DashStyle dash = DashStyle.Solid);

    void StrokeLine(PointMm a, PointMm b, ColorRgba stroke, double strokeWidthMm, DashStyle dash = DashStyle.Solid);

    void FillPolygon(IReadOnlyList<PointMm> points, ColorRgba fill, ColorRgba stroke, double strokeWidthMm);

    void DrawImage(IDecodedImage image, RectMm destRectMm, ImageAdjustments adjustments, double opacity);

    void DrawTextBlock(TextBlock block, RectMm rectMm, VerticalAlignment verticalAlignment);

    /// <summary>Fill many small rectangles efficiently (barcode bars / matrix cells).</summary>
    void FillRectangles(IReadOnlyList<RectMm> rectsMm, ColorRgba fill);
}
