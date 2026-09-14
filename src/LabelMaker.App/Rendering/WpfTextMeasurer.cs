using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Rendering;

/// <summary>
/// Real font metrics via WPF <see cref="FormattedText"/> — the accurate path for the
/// designer and print. Results are cached: label jobs measure the same words
/// ("Batch", "—", "Best before") thousands of times, and line height depends only
/// on the font and size, not on the word.
/// </summary>
public sealed class WpfTextMeasurer : ITextMeasurer
{
    private const double K = UnitConverter.DiuPerInch / UnitConverter.MmPerInch;

    /// <summary>Upper bound on cached word widths; the cache is simply cleared when it fills.</summary>
    private const int MaxCachedWidths = 50_000;

    private static readonly double PixelsPerDip =
        VisualTreeHelper.GetDpi(new System.Windows.Controls.Border()).PixelsPerDip;

    private readonly ConcurrentDictionary<(string Text, string Family, double Size, bool Bold, bool Italic), double> _widths = new();
    private readonly ConcurrentDictionary<(string Family, double Size), double> _lineHeights = new();

    public double MeasureWidthMm(string text, string fontFamily, double fontSizePt, bool bold, bool italic)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var key = (text, fontFamily, Math.Round(fontSizePt, 3), bold, italic);
        if (_widths.TryGetValue(key, out double cached))
        {
            return cached;
        }

        if (_widths.Count >= MaxCachedWidths)
        {
            _widths.Clear();
        }

        double width = Build(text, fontFamily, fontSizePt, bold, italic).WidthIncludingTrailingWhitespace / K;
        _widths[key] = width;
        return width;
    }

    public double LineHeightMm(string fontFamily, double fontSizePt) =>
        _lineHeights.GetOrAdd((fontFamily, Math.Round(fontSizePt, 3)),
            key => Build("Ag", key.Family, key.Size, false, false).Height / K);

    /// <summary>The font's own ascent; a fixed fraction of the line put the baseline too high or low for many fonts.</summary>
    public double BaselineMm(string fontFamily, double fontSizePt) =>
        _baselines.GetOrAdd((fontFamily, Math.Round(fontSizePt, 3)),
            key => Build("Ag", key.Family, key.Size, false, false).Baseline / K);

    private readonly ConcurrentDictionary<(string Family, double Size), double> _baselines = new();

    private static FormattedText Build(string text, string fontFamily, double fontSizePt, bool bold, bool italic)
    {
        double emDiu = Math.Max(0.1, UnitConverter.PointsToMm(fontSizePt) * K);
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            WpfFontCache.Typeface(fontFamily, bold, italic), emDiu, Brushes.Black, PixelsPerDip);
    }
}
