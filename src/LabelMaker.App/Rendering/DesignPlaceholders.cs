using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Rendering;

/// <summary>
/// The picture a design's logo or photo box shows until the user puts their own in (Image
/// Settings › Replace image): a light grey card with a picture sign and what goes there. It
/// prints faintly, so a label printed before the picture is changed still looks tidy.
/// </summary>
public static class DesignPlaceholders
{
    private const double PixelsPerMm = 12;

    public static (byte[] Png, int Width, int Height)? Make(string kind, SizeMm size)
    {
        int w = Math.Clamp((int)Math.Round(size.Width * PixelsPerMm), 8, 2400);
        int h = Math.Clamp((int)Math.Round(size.Height * PixelsPerMm), 8, 2400);
        string word = kind.Trim().ToLowerInvariant() switch
        {
            "photo" => "Photo",
            "logo" => "Your logo",
            "" => "Picture",
            var other => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(other),
        };

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brush(0xEE, 0xF1, 0xF5), null, new Rect(0, 0, w, h));

            // A picture sign: a frame with a hill and a sun, as large as the box allows.
            bool withWord = h > 40 && w > 60;
            double sign = Math.Min(w * 0.42, (withWord ? h * 0.5 : h * 0.7));
            double sx = (w - sign) / 2, sy = withWord ? (h * 0.42) - (sign * 0.5) : (h - sign) / 2;
            var ink = new Pen(Brush(0x9A, 0xA4, 0xB2), Math.Max(1, sign * 0.06)) { LineJoin = PenLineJoin.Round };
            dc.DrawRoundedRectangle(null, ink, new Rect(sx, sy + (sign * 0.12), sign, sign * 0.76), sign * 0.08, sign * 0.08);
            var hill = new StreamGeometry();
            using (StreamGeometryContext g = hill.Open())
            {
                g.BeginFigure(new Point(sx + (sign * 0.1), sy + (sign * 0.78)), true, true);
                g.LineTo(new Point(sx + (sign * 0.4), sy + (sign * 0.45)), true, true);
                g.LineTo(new Point(sx + (sign * 0.62), sy + (sign * 0.66)), true, true);
                g.LineTo(new Point(sx + (sign * 0.74), sy + (sign * 0.55)), true, true);
                g.LineTo(new Point(sx + (sign * 0.9), sy + (sign * 0.78)), true, true);
            }

            dc.DrawGeometry(Brush(0xC3, 0xCA, 0xD4), null, hill);
            dc.DrawEllipse(Brush(0xC3, 0xCA, 0xD4), null, new Point(sx + (sign * 0.72), sy + (sign * 0.33)), sign * 0.08, sign * 0.08);

            if (withWord)
            {
                var text = new FormattedText(word, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"), Math.Max(8, Math.Min(h * 0.13, w * 0.11)), Brush(0x7A, 0x84, 0x92), 1.0)
                { MaxTextWidth = w * 0.9, TextAlignment = TextAlignment.Center, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                dc.DrawText(text, new Point(w * 0.05, Math.Min(h - text.Height - 2, sy + sign + (h * 0.04))));
            }
        }

        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return (stream.ToArray(), w, h);
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
