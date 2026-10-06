using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using PdfSharp.Drawing;

namespace LabelMaker.Export.Pdf;

/// <summary>
/// An <see cref="IDrawingSurface"/> backed by PdfSharp's <see cref="XGraphics"/>.
/// Text, shapes and barcode bars are written as PDF vector operators — no
/// rasterisation (spec §17). Millimetres are converted to points here; the page
/// is sized to the true physical media so 40 mm prints 40 mm.
/// </summary>
public sealed class PdfDrawingSurface : IDrawingSurface
{
    private const double MmToPt = 72.0 / 25.4;

    private readonly XGraphics _gfx;
    private readonly Func<string, (byte[] Bytes, int Width, int Height)?> _assetResolver;
    private readonly Stack<XGraphicsState> _states = new();
    private readonly Stack<double> _opacity = new();

    public PdfDrawingSurface(
        XGraphics gfx,
        Func<string, (byte[] Bytes, int Width, int Height)?> assetResolver,
        ITextMeasurer? measurer = null)
    {
        _gfx = gfx;
        _assetResolver = assetResolver;
        _opacity.Push(1.0);
        TextMeasurer = measurer ?? new PdfTextMeasurer(gfx);
        Images = new AssetDimensionSource(assetResolver);
    }

    public ITextMeasurer TextMeasurer { get; }

    /// <summary>
    /// Draws Arabic and Hebrew text as outlines from a full text engine (set by the app at
    /// start-up), because PdfSharp writes letters one by one, unjoined and left to right.
    /// Without one, such text is written as plain PDF text.
    /// </summary>
    public static ITextOutliner? ComplexTextOutliner { get; set; }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> Outlined = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Marks a font family whose letters PdfSharp cannot embed (an OpenType font with PostScript
    /// outlines): its text is drawn as outlines by <see cref="ComplexTextOutliner"/> too, with
    /// invisible text on top so it can still be selected and searched.
    /// </summary>
    public static void DrawAsOutlines(string family) => Outlined[family] = true;

    private static bool NeedsOutline(string text, string family) => Bidi.HasRightToLeft(text) || Outlined.ContainsKey(family);

    public IImageSource Images { get; }

    private double Opacity => _opacity.Peek();

    public void PushMatrix(LabelMaker.Core.Model.Affine m)
    {
        _states.Push(_gfx.Save());
        _opacity.Push(Opacity);
        _gfx.MultiplyTransform(new XMatrix(m.A, m.B, m.C, m.D, m.E * MmToPt, m.F * MmToPt));
    }

    public void PushTransform(double tx, double ty, double rot, double sx, double sy)
    {
        _states.Push(_gfx.Save());
        _opacity.Push(Opacity);
        _gfx.TranslateTransform(tx * MmToPt, ty * MmToPt);
        if (Math.Abs(rot) > 1e-9)
        {
            _gfx.RotateTransform(rot);
        }

        if (Math.Abs(sx - 1) > 1e-9 || Math.Abs(sy - 1) > 1e-9)
        {
            _gfx.ScaleTransform(sx, sy);
        }
    }

    public void Pop()
    {
        if (_states.Count > 0)
        {
            _gfx.Restore(_states.Pop());
            _opacity.Pop();
        }
    }

    public void PushOpacity(double opacity)
    {
        _states.Push(_gfx.Save());
        _opacity.Push(Math.Clamp(Opacity * opacity, 0, 1));
    }

    public void PushClipRectangle(RectMm r, double cornerRadiusMm = 0)
    {
        _states.Push(_gfx.Save());
        _opacity.Push(Opacity);
        _gfx.IntersectClip(Pt(r));
    }

    public void PushClipEllipse(RectMm r)
    {
        _states.Push(_gfx.Save());
        _opacity.Push(Opacity);
        var path = new XGraphicsPath();
        path.AddEllipse(Pt(r));
        _gfx.IntersectClip(path);
    }

    public void FillRectangle(RectMm r, ColorRgba fill, double cornerRadiusMm = 0)
    {
        if (fill.IsTransparent)
        {
            return;
        }

        if (cornerRadiusMm > 0)
        {
            _gfx.DrawRoundedRectangle(Brush(fill), Pt(r),
                new XSize(cornerRadiusMm * MmToPt * 2, cornerRadiusMm * MmToPt * 2));
        }
        else
        {
            _gfx.DrawRectangle(Brush(fill), Pt(r));
        }
    }

    public void StrokeRectangle(RectMm r, ColorRgba stroke, double w, DashStyle dash = DashStyle.Solid, double cornerRadiusMm = 0)
    {
        if (stroke.IsTransparent || w <= 0)
        {
            return;
        }

        if (cornerRadiusMm > 0)
        {
            _gfx.DrawRoundedRectangle(Pen(stroke, w, dash), Pt(r),
                new XSize(cornerRadiusMm * MmToPt * 2, cornerRadiusMm * MmToPt * 2));
        }
        else
        {
            _gfx.DrawRectangle(Pen(stroke, w, dash), Pt(r));
        }
    }

    public void FillRectangleGradient(
        RectMm r, ColorRgba from, ColorRgba to, double angleDegrees, double cornerRadiusMm = 0)
    {
        var outline = new XGraphicsPath();
        if (cornerRadiusMm > 0)
        {
            XRect box = Pt(r);
            outline.AddRoundedRectangle(box.X, box.Y, box.Width, box.Height, cornerRadiusMm * MmToPt * 2, cornerRadiusMm * MmToPt * 2);
        }
        else
        {
            outline.AddRectangle(Pt(r));
        }

        if (SeeThrough(from, to))
        {
            FillBands(outline, r, from, to, angleDegrees);
            return;
        }

        XBrush brush = Gradient(r, from, to, angleDegrees);
        if (cornerRadiusMm > 0)
        {
            _gfx.DrawRoundedRectangle(brush, Pt(r), new XSize(cornerRadiusMm * MmToPt * 2, cornerRadiusMm * MmToPt * 2));
        }
        else
        {
            _gfx.DrawRectangle(brush, Pt(r));
        }
    }

    public void FillEllipseGradient(RectMm r, ColorRgba from, ColorRgba to, double angleDegrees)
    {
        if (SeeThrough(from, to))
        {
            var outline = new XGraphicsPath();
            outline.AddEllipse(Pt(r));
            FillBands(outline, r, from, to, angleDegrees);
            return;
        }

        _gfx.DrawEllipse(Gradient(r, from, to, angleDegrees), Pt(r));
    }

    public void FillPolygonGradient(
        IReadOnlyList<PointMm> points, ColorRgba from, ColorRgba to, double angleDegrees,
        ColorRgba stroke, double strokeWidthMm)
    {
        if (points.Count < 3)
        {
            return;
        }

        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        var bounds = new RectMm(minX, minY, maxX - minX, maxY - minY);

        XPoint[] pts = points.Select(p => new XPoint(p.X * MmToPt, p.Y * MmToPt)).ToArray();
        XPen? pen = stroke.IsTransparent || strokeWidthMm <= 0 ? null : Pen(stroke, strokeWidthMm, DashStyle.Solid);
        if (SeeThrough(from, to))
        {
            var outline = new XGraphicsPath { FillMode = XFillMode.Alternate };
            outline.AddPolygon(pts);
            FillBands(outline, bounds, from, to, angleDegrees);
            if (pen is not null)
            {
                _gfx.DrawPolygon(pen, pts);
            }

            return;
        }

        _gfx.DrawPolygon(pen, Gradient(bounds, from, to, angleDegrees), pts, XFillMode.Alternate);
    }

    /// <summary>
    /// PdfSharp only offers four gradient directions, so the requested angle snaps to
    /// the nearest of them; pointing the other way round swaps the two colours. Everything
    /// else about the fill is exact.
    /// </summary>
    private XBrush Gradient(RectMm r, ColorRgba from, ColorRgba to, double angleDegrees)
    {
        double a = ((angleDegrees % 360) + 360) % 360;
        XLinearGradientMode mode = a switch
        {
            < 22.5 or >= 337.5 => XLinearGradientMode.Horizontal,
            < 67.5 => XLinearGradientMode.ForwardDiagonal,
            < 112.5 => XLinearGradientMode.Vertical,
            < 157.5 => XLinearGradientMode.BackwardDiagonal,
            < 202.5 => XLinearGradientMode.Horizontal,
            < 247.5 => XLinearGradientMode.ForwardDiagonal,
            < 292.5 => XLinearGradientMode.Vertical,
            _ => XLinearGradientMode.BackwardDiagonal,
        };

        // From 157.5° to 337.5° the colours run right to left, upwards, or up to the right.
        bool reversed = a >= 157.5 && a < 337.5;
        return reversed
            ? new XLinearGradientBrush(Pt(r), ApplyOpacity(to), ApplyOpacity(from), mode)
            : new XLinearGradientBrush(Pt(r), ApplyOpacity(from), ApplyOpacity(to), mode);
    }

    /// <summary>PDF gradients cannot fade: a see-through colour or a faded object is drawn in bands instead.</summary>
    private bool SeeThrough(ColorRgba from, ColorRgba to) => from.A < 255 || to.A < 255 || Opacity < 0.999;

    /// <summary>How many bands make up a gradient drawn in bands: too fine to see on any label.</summary>
    private const int GradientBands = 96;

    /// <summary>
    /// A gradient as thin bands of solid colour, each with its own opacity, across the outline
    /// at exactly the requested angle.
    /// </summary>
    private void FillBands(XGraphicsPath outline, RectMm r, ColorRgba from, ColorRgba to, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180.0;
        double dx = Math.Cos(radians), dy = Math.Sin(radians);
        double cx = (r.X + (r.Width / 2)) * MmToPt, cy = (r.Y + (r.Height / 2)) * MmToPt;
        // The gradient runs across the box along the angle, from edge to edge (as on screen).
        double half = ((Math.Abs(dx) * r.Width) + (Math.Abs(dy) * r.Height)) / 2 * MmToPt;
        double reach = Math.Sqrt((r.Width * r.Width) + (r.Height * r.Height)) * MmToPt;
        if (half <= 0)
        {
            return;
        }

        XGraphicsState state = _gfx.Save();
        _gfx.IntersectClip(outline);
        double step = 2 * half / GradientBands;
        for (int i = 0; i < GradientBands; i++)
        {
            double t = (i + 0.5) / GradientBands;
            var colour = new ColorRgba(
                Mix(from.R, to.R, t), Mix(from.G, to.G, t), Mix(from.B, to.B, t), Mix(from.A, to.A, t));
            if (colour.IsTransparent)
            {
                continue;
            }

            // The first and last bands reach past the box so nothing at the corners is left out.
            double start = i == 0 ? -reach : -half + (i * step);
            double end = i == GradientBands - 1 ? reach : -half + ((i + 1) * step);
            XPoint Along(double s, double side) => new(cx + (dx * s) - (dy * side), cy + (dy * s) + (dx * side));
            XPoint[] band = { Along(start, -reach), Along(end, -reach), Along(end, reach), Along(start, reach) };
            _gfx.DrawPolygon(Brush(colour), band, XFillMode.Winding);
        }

        _gfx.Restore(state);
    }

    private static byte Mix(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * t));

    public void FillEllipse(RectMm r, ColorRgba fill)
    {
        if (!fill.IsTransparent)
        {
            _gfx.DrawEllipse(Brush(fill), Pt(r));
        }
    }

    public void StrokeEllipse(RectMm r, ColorRgba stroke, double w, DashStyle dash = DashStyle.Solid)
    {
        if (!stroke.IsTransparent && w > 0)
        {
            _gfx.DrawEllipse(Pen(stroke, w, dash), Pt(r));
        }
    }

    public void StrokeLine(PointMm a, PointMm b, ColorRgba stroke, double w, DashStyle dash = DashStyle.Solid)
    {
        if (!stroke.IsTransparent && w > 0)
        {
            _gfx.DrawLine(Pen(stroke, w, dash), a.X * MmToPt, a.Y * MmToPt, b.X * MmToPt, b.Y * MmToPt);
        }
    }

    public void FillPolygon(IReadOnlyList<PointMm> points, ColorRgba fill, ColorRgba stroke, double w)
    {
        if (points.Count < 2)
        {
            return;
        }

        XPoint[] xs = points.Select(p => new XPoint(p.X * MmToPt, p.Y * MmToPt)).ToArray();
        if (!fill.IsTransparent && !stroke.IsTransparent && w > 0)
        {
            _gfx.DrawPolygon(Pen(stroke, w, DashStyle.Solid), Brush(fill), xs, XFillMode.Alternate);
        }
        else if (!fill.IsTransparent)
        {
            _gfx.DrawPolygon(Brush(fill), xs, XFillMode.Alternate);
        }
        else if (!stroke.IsTransparent && w > 0)
        {
            _gfx.DrawPolygon(Pen(stroke, w, DashStyle.Solid), xs);
        }
    }

    public void DrawImage(IDecodedImage image, RectMm destRectMm, ImageAdjustments adjustments, double opacity)
    {
        if (image is not AssetImageHandle handle)
        {
            return;
        }

        var payload = _assetResolver(handle.AssetId);
        if (payload is not { } data)
        {
            return;
        }

        try
        {
            using var ms = new MemoryStream(data.Bytes, writable: false);
            using XImage ximage = XImage.FromStream(ms);
            _gfx.DrawImage(ximage, Pt(destRectMm));
        }
        catch (Exception)
        {
            _gfx.DrawRectangle(Pen(new ColorRgba(200, 60, 60), 0.2, DashStyle.Dash), Pt(destRectMm));
        }
    }

    public void DrawTextBlock(TextBlock block, RectMm rect, VerticalAlignment verticalAlignment)
    {
        double y = rect.Y + block.TopOffsetMm(verticalAlignment, rect.Height);

        foreach (TextLine line in block.Lines)
        {
            double lineTop = y;
            y += line.HeightMm;
            if (line.Spans.Count == 0)
            {
                continue;
            }

            double x = line.Alignment switch
            {
                HorizontalAlignment.Center => rect.X + Math.Max(0, (rect.Width - line.WidthMm) / 2.0),
                HorizontalAlignment.Right => rect.X + Math.Max(0, rect.Width - line.WidthMm),
                _ => rect.X,
            };
            double baseline = lineTop + line.BaselineOffsetMm;

            foreach (TextSpan span in line.Spans)
            {
                XFont font = MakeFont(span);
                var brush = Brush(span.Foreground);
                double advance;
                double trackingMm = span.LetterSpacingEm * UnitConverter.PointsToMm(span.FontSizePt);
                if (ComplexTextOutliner is { } outliner && NeedsOutline(span.Text, span.FontFamily))
                {
                    var path = new XGraphicsPath { FillMode = XFillMode.Winding };
                    foreach (IReadOnlyList<PointMm> figure in outliner.Outline(span))
                    {
                        if (figure.Count >= 3)
                        {
                            path.AddPolygon(figure.Select(p => new XPoint((x + p.X) * MmToPt, (baseline + p.Y) * MmToPt)).ToArray());
                        }
                    }

                    _gfx.DrawPath(brush, path);
                    advance = TextMeasurer.MeasureWidthMm(span.Text, span.FontFamily, span.FontSizePt, span.Bold, span.Italic);
                    DrawSearchableText(span, font, x, baseline, advance);
                }
                else if (Math.Abs(trackingMm) < 1e-6)
                {
                    _gfx.DrawString(span.Text, font, brush, x * MmToPt, baseline * MmToPt, XStringFormats.BaseLineLeft);
                    advance = TextMeasurer.MeasureWidthMm(span.Text, span.FontFamily, span.FontSizePt, span.Bold, span.Italic);
                }
                else
                {
                    // Letter spacing: place each character with the extra gap after it.
                    advance = 0;
                    System.Globalization.TextElementEnumerator characters = System.Globalization.StringInfo.GetTextElementEnumerator(span.Text);
                    while (characters.MoveNext())
                    {
                        string c = characters.GetTextElement();
                        _gfx.DrawString(c, font, brush, (x + advance) * MmToPt, baseline * MmToPt, XStringFormats.BaseLineLeft);
                        advance += TextMeasurer.MeasureWidthMm(c, span.FontFamily, span.FontSizePt, span.Bold, span.Italic) + trackingMm;
                    }
                }

                if (span.Underline)
                {
                    _gfx.DrawLine(Pen(span.Foreground, span.FontSizePt * 0.06, DashStyle.Solid),
                        x * MmToPt, (baseline + 0.6) * MmToPt, (x + advance) * MmToPt, (baseline + 0.6) * MmToPt);
                }

                if (span.Strikethrough)
                {
                    double strike = baseline - (UnitConverter.PointsToMm(span.FontSizePt) * 0.3);
                    _gfx.DrawLine(Pen(span.Foreground, span.FontSizePt * 0.06, DashStyle.Solid),
                        x * MmToPt, strike * MmToPt, (x + advance) * MmToPt, strike * MmToPt);
                }

                x += advance + (span.Text.EndsWith(' ') ? line.WordGapMm : 0);
            }
        }
    }

    // Fully transparent: the text is there for selecting, copying and searching, never seen.
    private static readonly XSolidBrush InvisibleInk = new(XColor.FromArgb(0, 0, 0, 0));

    /// <summary>
    /// Under text drawn as outlines, the same words as real but invisible PDF text, in the
    /// order they sit on the page and stretched to the outlines' width, the way scanned and
    /// recognised PDFs are made. PDF viewers turn it back into reading order, so Arabic in an
    /// exported PDF can be selected, copied and found with Find.
    /// </summary>
    private void DrawSearchableText(TextSpan span, XFont font, double xMm, double baselineMm, double widthMm)
    {
        string onPage = Bidi.VisualString(span.Text.TrimEnd(), span.RightToLeft);
        if (onPage.Trim().Length == 0 || widthMm <= 0)
        {
            return;
        }

        try
        {
            double natural = _gfx.MeasureString(onPage, font).Width;
            if (natural <= 0)
            {
                return;
            }

            double x = xMm * MmToPt;
            XGraphicsState state = _gfx.Save();
            _gfx.ScaleAtTransform(widthMm * MmToPt / natural, 1, new XPoint(x, baselineMm * MmToPt));
            _gfx.DrawString(onPage, font, InvisibleInk, x, baselineMm * MmToPt, XStringFormats.BaseLineLeft);
            _gfx.Restore(state);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // A font without those letters: the outlines are still there; only searching is lost.
        }
    }

    public void FillRectangles(IReadOnlyList<RectMm> rects, ColorRgba fill)
    {
        if (rects.Count == 0 || fill.IsTransparent)
        {
            return;
        }

        XSolidBrush brush = Brush(fill);
        foreach (RectMm r in rects)
        {
            _gfx.DrawRectangle(brush, Pt(r));
        }
    }

    private XRect Pt(RectMm r) => new(r.X * MmToPt, r.Y * MmToPt, r.Width * MmToPt, r.Height * MmToPt);

    private XSolidBrush Brush(ColorRgba c) => new(ApplyOpacity(c));

    private XPen Pen(ColorRgba c, double widthMm, DashStyle dash)
    {
        var pen = new XPen(ApplyOpacity(c), Math.Max(0.05, widthMm * MmToPt))
        {
            DashStyle = dash switch
            {
                DashStyle.Dash => XDashStyle.Dash,
                DashStyle.Dot => XDashStyle.Dot,
                DashStyle.DashDot => XDashStyle.DashDot,
                _ => XDashStyle.Solid,
            },
        };
        return pen;
    }

    private XColor ApplyOpacity(ColorRgba c)
    {
        double a = c.A / 255.0 * Opacity;
        return XColor.FromArgb((int)Math.Round(a * 255), c.R, c.G, c.B);
    }

    private static XFont MakeFont(TextSpan span)
    {
        XFontStyleEx style = XFontStyleEx.Regular;
        if (span.Bold)
        {
            style |= XFontStyleEx.Bold;
        }

        if (span.Italic)
        {
            style |= XFontStyleEx.Italic;
        }

        try
        {
            return new XFont(span.FontFamily, span.FontSizePt, style);
        }
        catch (Exception)
        {
            return new XFont("Arial", span.FontSizePt, style);
        }
    }

    private sealed class AssetImageHandle : IDecodedImage
    {
        public required string AssetId { get; init; }
        public int PixelWidth { get; init; }
        public int PixelHeight { get; init; }
    }

    private sealed class AssetDimensionSource : IImageSource
    {
        private readonly Func<string, (byte[] Bytes, int Width, int Height)?> _resolver;

        public AssetDimensionSource(Func<string, (byte[] Bytes, int Width, int Height)?> resolver) => _resolver = resolver;

        public IDecodedImage? Get(string assetId)
        {
            var data = _resolver(assetId);
            return data is { } d
                ? new AssetImageHandle { AssetId = assetId, PixelWidth = d.Width, PixelHeight = d.Height }
                : null;
        }
    }

    private sealed class PdfTextMeasurer : ITextMeasurer
    {
        private readonly XGraphics _gfx;
        private readonly ApproxTextMeasurer _fallback = new();

        public PdfTextMeasurer(XGraphics gfx) => _gfx = gfx;

        public double MeasureWidthMm(string text, string fontFamily, double fontSizePt, bool bold, bool italic)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            // Arabic and Hebrew are drawn by the outliner, so they are measured by it as well.
            if (ComplexTextOutliner is { } outliner && NeedsOutline(text, fontFamily))
            {
                return outliner.MeasureWidthMm(text, fontFamily, fontSizePt, bold, italic);
            }

            try
            {
                XFontStyleEx style = XFontStyleEx.Regular;
                if (bold)
                {
                    style |= XFontStyleEx.Bold;
                }

                if (italic)
                {
                    style |= XFontStyleEx.Italic;
                }

                var font = new XFont(fontFamily, fontSizePt, style);
                double widthPt = _gfx.MeasureString(text, font).Width;
                return widthPt / MmToPt;
            }
            catch (Exception)
            {
                return _fallback.MeasureWidthMm(text, fontFamily, fontSizePt, bold, italic);
            }
        }

        public double LineHeightMm(string fontFamily, double fontSizePt)
        {
            try
            {
                var font = new XFont(fontFamily, fontSizePt);
                return font.GetHeight() / MmToPt;
            }
            catch (Exception)
            {
                return _fallback.LineHeightMm(fontFamily, fontSizePt);
            }
        }
    }
}
