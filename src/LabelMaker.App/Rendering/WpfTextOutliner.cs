using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Rendering;

/// <summary>
/// Outlines of text from WPF's text engine, which joins Arabic letters and orders right-to-left
/// text properly. The PDF exporter fills these for Arabic and Hebrew, so a PDF matches the
/// screen and the printout exactly.
/// </summary>
public sealed class WpfTextOutliner : ITextOutliner
{
    private const double K = UnitConverter.DiuPerInch / UnitConverter.MmPerInch; // DIU per mm

    // About a hundredth of a millimetre: invisible even when printed large.
    private const double ToleranceDiu = 0.04;

    public IReadOnlyList<IReadOnlyList<PointMm>> Outline(TextSpan span)
    {
        ArgumentNullException.ThrowIfNull(span);
        Typeface typeface = WpfFontCache.Typeface(span.FontFamily, span.Bold, span.Italic);
        var text = new FormattedText(
            Bidi.ForDisplay(span),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            Math.Max(0.1, UnitConverter.PointsToMm(span.FontSizePt) * K),
            Brushes.Black,
            1.0);

        // Put the baseline on y = 0, as the PDF surface draws from the baseline.
        Geometry geometry = text.BuildGeometry(new Point(0, -text.Baseline));
        PathGeometry flat = geometry.GetFlattenedPathGeometry(ToleranceDiu, ToleranceType.Absolute);

        var figures = new List<IReadOnlyList<PointMm>>(flat.Figures.Count);
        foreach (PathFigure figure in flat.Figures)
        {
            var points = new List<PointMm> { Mm(figure.StartPoint) };
            foreach (PathSegment segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        points.Add(Mm(line.Point));
                        break;
                    case PolyLineSegment poly:
                        points.AddRange(poly.Points.Select(Mm));
                        break;
                }
            }

            if (points.Count >= 3)
            {
                figures.Add(points);
            }
        }

        return figures;
    }

    private static readonly WpfTextMeasurer Measurer = new();

    public double MeasureWidthMm(string text, string fontFamily, double fontSizePt, bool bold, bool italic) =>
        Measurer.MeasureWidthMm(text, fontFamily, fontSizePt, bold, italic);

    private static PointMm Mm(Point p) => new(p.X / K, p.Y / K);
}
