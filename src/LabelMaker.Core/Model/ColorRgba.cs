using System.Globalization;

namespace LabelMaker.Core.Model;

/// <summary>
/// A straight (non-premultiplied) 8-bit-per-channel colour, independent of any UI
/// framework. Serialized as "#RRGGBB" or "#RRGGBBAA".
/// </summary>
public readonly record struct ColorRgba(byte R, byte G, byte B, byte A = 255)
{
    public static readonly ColorRgba Black = new(0, 0, 0);
    public static readonly ColorRgba White = new(255, 255, 255);
    public static readonly ColorRgba Transparent = new(0, 0, 0, 0);

    public bool IsTransparent => A == 0;
    public bool IsOpaque => A == 255;

    public ColorRgba WithAlpha(byte a) => this with { A = a };

    public ColorRgba WithOpacity(double opacity) =>
        this with { A = (byte)Math.Clamp(Math.Round(A * Math.Clamp(opacity, 0, 1)), 0, 255) };

    public string ToHex() => A == 255
        ? $"#{R:X2}{G:X2}{B:X2}"
        : $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    public override string ToString() => ToHex();

    public static ColorRgba Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException($"'{text}' is not a colour.");

    public static bool TryParse(string? text, out ColorRgba color)
    {
        color = Black;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string s = text.Trim().TrimStart('#');
        if (s.Length is not (3 or 6 or 8))
        {
            return false;
        }

        if (s.Length == 3)
        {
            s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        }

        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
        {
            return false;
        }

        if (s.Length == 6)
        {
            color = new ColorRgba((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
            return true;
        }

        color = new ColorRgba(
            (byte)((v >> 24) & 0xFF),
            (byte)((v >> 16) & 0xFF),
            (byte)((v >> 8) & 0xFF),
            (byte)(v & 0xFF));
        return true;
    }
}
