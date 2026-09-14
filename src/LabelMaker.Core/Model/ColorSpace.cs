namespace LabelMaker.Core.Model;

/// <summary>Hue / saturation / value conversions for the colour chooser.</summary>
public static class ColorSpace
{
    /// <summary>Hue 0..360, saturation and value 0..1. Greys report hue 0.</summary>
    public static (double H, double S, double V) ToHsv(ColorRgba c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0;
        if (delta > 1e-9)
        {
            if (max == r)
            {
                h = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                h = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                h = 60 * (((r - g) / delta) + 4);
            }
        }

        if (h < 0)
        {
            h += 360;
        }

        double s = max <= 1e-9 ? 0 : delta / max;
        return (h, s, max);
    }

    public static ColorRgba FromHsv(double h, double s, double v, byte alpha = 255)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);

        double c = v * s;
        double x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
        double m = v - c;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        static byte B(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
        return new ColorRgba(B(r + m), B(g + m), B(b + m), alpha);
    }
}
