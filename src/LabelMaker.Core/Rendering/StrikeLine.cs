namespace LabelMaker.Core.Rendering;

/// <summary>
/// Where the line of struck-through text sits, the same on screen, on paper, in PDF and in SVG.
/// Fonts put their own strikethrough at the middle of the small letters, which runs low through
/// digits and capitals, so an old price such as "$23.99" looked underlined rather than crossed out.
/// </summary>
public static class StrikeLine
{
    /// <summary>The line's thickness, as a share of the font size.</summary>
    public const double ThicknessEm = 0.06;

    // Middle of the capitals and digits (about 0.7 em tall), and of the small letters (about 0.5 em).
    private const double CapitalsEm = 0.35;
    private const double SmallLettersEm = 0.27;

    /// <summary>
    /// How far above the baseline the line runs, as a share of the font size: through the middle
    /// of the digits and capitals when <paramref name="text"/> has only those (prices, codes,
    /// "SALE"), otherwise through the middle of the small letters. One height for a whole run of
    /// text, so the line stays straight.
    /// </summary>
    public static double HeightEm(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Any(c => char.IsLetter(c) && !char.IsUpper(c)) ? SmallLettersEm : CapitalsEm;
    }
}
