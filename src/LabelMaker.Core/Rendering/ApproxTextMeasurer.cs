using LabelMaker.Core.Units;

namespace LabelMaker.Core.Rendering;

/// <summary>
/// A font-metric-free text measurer for headless rendering (CLI, SVG export,
/// tests). Estimates width from an average glyph-advance ratio per family class.
/// The WPF app supplies a real <see cref="ITextMeasurer"/> backed by
/// <c>FormattedText</c>; this is only the fallback.
/// </summary>
public sealed class ApproxTextMeasurer : ITextMeasurer
{
    public double MeasureWidthMm(string text, string fontFamily, double fontSizePt, bool bold, bool italic)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        double emMm = UnitConverter.PointsToMm(fontSizePt);
        double ratio = IsMonospace(fontFamily) ? 0.60 : 0.52;
        if (bold)
        {
            ratio += 0.02;
        }

        double width = 0;
        foreach (char c in text)
        {
            width += c switch
            {
                ' ' => 0.30,
                'i' or 'l' or 'I' or 'j' or '.' or ',' or '\'' or '|' or '!' => ratio * 0.42,
                'm' or 'M' or 'W' or 'w' => ratio * 1.45,
                >= '0' and <= '9' => ratio,
                _ => ratio,
            } * emMm;
        }

        return width;
    }

    public double LineHeightMm(string fontFamily, double fontSizePt) =>
        UnitConverter.PointsToMm(fontSizePt) * 1.25;

    private static bool IsMonospace(string family) =>
        family.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
        family.Contains("Consol", StringComparison.OrdinalIgnoreCase) ||
        family.Contains("Courier", StringComparison.OrdinalIgnoreCase);
}
