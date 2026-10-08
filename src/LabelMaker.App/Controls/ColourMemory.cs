using System.Globalization;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Controls;

/// <summary>
/// The colour window's lists and rules that need no window: the recently used colours and
/// gradients, how a gradient is kept in the settings, and the ready-made gradients.
/// </summary>
public static class ColourMemory
{
    public const int MaxRecentColours = 20;

    public const int MaxRecentGradients = 12;

    /// <summary>Ready-made gradients: start and end colour, drawn left to right.</summary>
    public static IReadOnlyList<(string Name, string From, string To)> PresetGradients { get; } = new[]
    {
        ("Ocean", "#2C6FE8", "#6FD3F7"),
        ("Violet", "#7B5CFA", "#E16BD8"),
        ("Sunset", "#FF7A59", "#FFC15E"),
        ("Mint", "#1FAF7A", "#B8F1D2"),
        ("Berry", "#D9346C", "#7A2CF0"),
        ("Gold", "#B7862F", "#F6E27A"),
        ("Fire", "#E5322D", "#F7A325"),
        ("Forest", "#17633F", "#6DBE45"),
        ("Peach", "#FFD3A5", "#FD6585"),
        ("Night", "#141E30", "#3B5A86"),
        ("Sky", "#A1C4FD", "#C2E9FB"),
        ("Lime", "#F9F047", "#0FD850"),
        ("Rose", "#F5C6CF", "#B5577D"),
        ("Steel", "#3A4556", "#9AA7B8"),
        ("Silver", "#F2F4F7", "#A9B1BF"),
        ("Fade to clear", "#000000", "#00000000"),
    };

    /// <summary>
    /// Puts <paramref name="entry"/> first in <paramref name="list"/>, removing any earlier copy
    /// (in any letter case) and the oldest entries beyond <paramref name="max"/>.
    /// </summary>
    public static void Remember(IList<string> list, string entry, int max)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (string.Equals(list[i], entry, StringComparison.OrdinalIgnoreCase))
            {
                list.RemoveAt(i);
            }
        }

        list.Insert(0, entry);
        while (list.Count > max)
        {
            list.RemoveAt(list.Count - 1);
        }
    }

    /// <summary>How a gradient is kept in the settings: "#start #end angle".</summary>
    public static string FormatGradient(ColorRgba from, ColorRgba to, double angle) =>
        string.Create(CultureInfo.InvariantCulture, $"{from.ToHex()} {to.ToHex()} {angle:0}");

    public static (ColorRgba From, ColorRgba To, double Angle)? ParseGradient(string? text)
    {
        string[] parts = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3 && ColorRgba.TryParse(parts[0], out ColorRgba from) && ColorRgba.TryParse(parts[1], out ColorRgba to) &&
               double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double angle)
            ? (from, to, angle)
            : null;
    }

    /// <summary>A direction as 0 to under 360 degrees: -90 is 270, 450 is 90.</summary>
    public static double NormaliseAngle(double degrees) => ((degrees % 360) + 360) % 360;

    /// <summary>A second colour that goes well with <paramref name="c"/>: a neighbouring hue, lighter or darker.</summary>
    public static ColorRgba PartnerOf(ColorRgba c)
    {
        (double h, double s, double v) = ColorSpace.ToHsv(c);
        return v > 0.6
            ? ColorSpace.FromHsv((h + 20) % 360, Math.Min(1, s + 0.15), Math.Max(0, v - 0.35), c.A)
            : ColorSpace.FromHsv((h + 340) % 360, Math.Max(0, s - 0.2), Math.Min(1, v + 0.4), c.A);
    }
}
