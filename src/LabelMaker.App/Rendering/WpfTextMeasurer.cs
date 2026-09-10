using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Rendering;

/// <summary>Real font metrics via WPF <see cref="FormattedText"/> — the accurate path for the designer and print.</summary>
public sealed class WpfTextMeasurer : ITextMeasurer
{
    private const double K = UnitConverter.DiuPerInch / UnitConverter.MmPerInch;

    private static readonly double PixelsPerDip =
        VisualTreeHelper.GetDpi(new System.Windows.Controls.Border()).PixelsPerDip;

    public double MeasureWidthMm(string text, string fontFamily, double fontSizePt, bool bold, bool italic)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        FormattedText ft = Build(text, fontFamily, fontSizePt, bold, italic);
        return ft.WidthIncludingTrailingWhitespace / K;
    }

    public double LineHeightMm(string fontFamily, double fontSizePt)
    {
        FormattedText ft = Build("Ag", fontFamily, fontSizePt, false, false);
        return ft.Height / K;
    }

    private static FormattedText Build(string text, string fontFamily, double fontSizePt, bool bold, bool italic)
    {
        var typeface = new Typeface(
            new FontFamily(fontFamily),
            italic ? FontStyles.Italic : FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        double emDiu = Math.Max(0.1, UnitConverter.PointsToMm(fontSizePt) * K);
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, emDiu, Brushes.Black, PixelsPerDip);
    }
}
