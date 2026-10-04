using System.Globalization;
using System.Text;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;

namespace LabelMaker.Export.Svg;

/// <summary>
/// An <see cref="IDrawingSurface"/> that writes a self-contained SVG document.
/// Millimetres map 1:1 to user units (the root <c>viewBox</c> is in mm), so
/// output is exactly the physical size. Text and shapes stay vector; barcodes are
/// emitted as <c>&lt;rect&gt;</c> runs.
/// </summary>
public sealed class SvgDrawingSurface : IDrawingSurface
{
    private readonly StringBuilder _body = new();
    private readonly StringBuilder _defs = new();
    private readonly double _widthMm;
    private readonly double _heightMm;
    private int _openGroups;
    private int _clipCounter;
    private int _gradientCounter;

    public SvgDrawingSurface(double widthMm, double heightMm, ITextMeasurer measurer, IImageSource images)
    {
        _widthMm = widthMm;
        _heightMm = heightMm;
        TextMeasurer = measurer;
        Images = images;
    }

    public ITextMeasurer TextMeasurer { get; }

    public IImageSource Images { get; }

    public string ToSvg(ColorRgba? background = null)
    {
        while (_openGroups > 0)
        {
            _body.Append("</g>");
            _openGroups--;
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(_widthMm)}mm\" height=\"{F(_heightMm)}mm\" viewBox=\"0 0 {F(_widthMm)} {F(_heightMm)}\">");
        if (_defs.Length > 0)
        {
            sb.Append("<defs>").Append(_defs).Append("</defs>");
        }

        if (background is { IsTransparent: false } bg)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<rect x=\"0\" y=\"0\" width=\"{F(_widthMm)}\" height=\"{F(_heightMm)}\" fill=\"{Rgb(bg)}\"/>");
        }

        sb.Append(_body).Append("</svg>");
        return sb.ToString();
    }

    public void PushMatrix(Affine m)
    {
        _body.Append("<g transform=\"matrix(")
            .Append(F(m.A)).Append(' ').Append(F(m.B)).Append(' ')
            .Append(F(m.C)).Append(' ').Append(F(m.D)).Append(' ')
            .Append(F(m.E)).Append(' ').Append(F(m.F)).Append(")\">");
        _openGroups++;
    }

    public void PushTransform(double tx, double ty, double rot, double sx, double sy)
    {
        var t = new StringBuilder("translate(").Append(F(tx)).Append(' ').Append(F(ty)).Append(')');
        if (Math.Abs(rot) > 1e-6)
        {
            t.Append(" rotate(").Append(F(rot)).Append(')');
        }

        if (Math.Abs(sx - 1) > 1e-9 || Math.Abs(sy - 1) > 1e-9)
        {
            t.Append(" scale(").Append(F(sx)).Append(' ').Append(F(sy)).Append(')');
        }

        _body.Append("<g transform=\"").Append(t).Append("\">");
        _openGroups++;
    }

    public void Pop()
    {
        if (_openGroups > 0)
        {
            _body.Append("</g>");
            _openGroups--;
        }
    }

    public void PushOpacity(double opacity)
    {
        _body.Append("<g opacity=\"").Append(F(Math.Clamp(opacity, 0, 1))).Append("\">");
        _openGroups++;
    }

    public void PushClipRectangle(RectMm r, double cornerRadiusMm = 0)
    {
        string id = $"clip{_clipCounter++}";
        _defs.Append("<clipPath id=\"").Append(id).Append("\"><rect x=\"").Append(F(r.X)).Append("\" y=\"").Append(F(r.Y))
            .Append("\" width=\"").Append(F(r.Width)).Append("\" height=\"").Append(F(r.Height));
        if (cornerRadiusMm > 0)
        {
            _defs.Append("\" rx=\"").Append(F(cornerRadiusMm));
        }

        _defs.Append("\"/></clipPath>");
        _body.Append("<g clip-path=\"url(#").Append(id).Append(")\">");
        _openGroups++;
    }

    public void PushClipEllipse(RectMm r)
    {
        string id = $"clip{_clipCounter++}";
        _defs.Append("<clipPath id=\"").Append(id).Append("\"><ellipse cx=\"").Append(F(r.Center.X)).Append("\" cy=\"")
            .Append(F(r.Center.Y)).Append("\" rx=\"").Append(F(r.Width / 2)).Append("\" ry=\"").Append(F(r.Height / 2))
            .Append("\"/></clipPath>");
        _body.Append("<g clip-path=\"url(#").Append(id).Append(")\">");
        _openGroups++;
    }

    public void FillRectangle(RectMm r, ColorRgba fill, double cornerRadiusMm = 0)
    {
        if (fill.IsTransparent)
        {
            return;
        }

        _body.Append("<rect x=\"").Append(F(r.X)).Append("\" y=\"").Append(F(r.Y)).Append("\" width=\"").Append(F(r.Width))
            .Append("\" height=\"").Append(F(r.Height));
        if (cornerRadiusMm > 0)
        {
            _body.Append("\" rx=\"").Append(F(cornerRadiusMm));
        }

        _body.Append("\" fill=\"").Append(Rgb(fill)).Append("\"").Append(Alpha(fill, "fill-opacity")).Append("/>");
    }

    public void StrokeRectangle(RectMm r, ColorRgba stroke, double w, DashStyle dash = DashStyle.Solid, double cornerRadiusMm = 0)
    {
        _body.Append("<rect x=\"").Append(F(r.X)).Append("\" y=\"").Append(F(r.Y)).Append("\" width=\"").Append(F(r.Width))
            .Append("\" height=\"").Append(F(r.Height));
        if (cornerRadiusMm > 0)
        {
            _body.Append("\" rx=\"").Append(F(cornerRadiusMm));
        }

        _body.Append("\" fill=\"none\" stroke=\"").Append(Rgb(stroke)).Append("\" stroke-width=\"").Append(F(w))
            .Append("\"").Append(Dash(dash, w)).Append("/>");
    }

    public void FillRectangleGradient(
        RectMm r, ColorRgba from, ColorRgba to, double angleDegrees, double cornerRadiusMm = 0)
    {
        string id = Gradient(from, to, angleDegrees);
        _body.Append("<rect x=\"").Append(F(r.X)).Append("\" y=\"").Append(F(r.Y)).Append("\" width=\"").Append(F(r.Width))
            .Append("\" height=\"").Append(F(r.Height));
        if (cornerRadiusMm > 0)
        {
            _body.Append("\" rx=\"").Append(F(cornerRadiusMm));
        }

        _body.Append("\" fill=\"url(#").Append(id).Append(")\"/>");
    }

    public void FillEllipseGradient(RectMm r, ColorRgba from, ColorRgba to, double angleDegrees)
    {
        string id = Gradient(from, to, angleDegrees);
        _body.Append("<ellipse cx=\"").Append(F(r.Center.X)).Append("\" cy=\"").Append(F(r.Center.Y)).Append("\" rx=\"")
            .Append(F(r.Width / 2)).Append("\" ry=\"").Append(F(r.Height / 2))
            .Append("\" fill=\"url(#").Append(id).Append(")\"/>");
    }

    public void FillPolygonGradient(
        IReadOnlyList<PointMm> points, ColorRgba from, ColorRgba to, double angleDegrees,
        ColorRgba stroke, double w)
    {
        string id = Gradient(from, to, angleDegrees);
        _body.Append("<polygon points=\"");
        foreach (PointMm p in points)
        {
            _body.Append(F(p.X)).Append(',').Append(F(p.Y)).Append(' ');
        }

        _body.Append("\" fill=\"url(#").Append(id).Append(')').Append('"');
        if (w > 0 && !stroke.IsTransparent)
        {
            _body.Append(" stroke=\"").Append(Rgb(stroke)).Append("\" stroke-width=\"").Append(F(w)).Append('"');
        }

        _body.Append("/>");
    }

    /// <summary>
    /// Emits a &lt;linearGradient&gt; in objectBoundingBox units and returns its id,
    /// so the gradient follows the shape whatever its size.
    /// </summary>
    private string Gradient(ColorRgba from, ColorRgba to, double angleDegrees)
    {
        string id = $"grad{_gradientCounter++}";
        double radians = angleDegrees * Math.PI / 180.0;
        double dx = Math.Cos(radians) / 2.0;
        double dy = Math.Sin(radians) / 2.0;

        _defs.Append("<linearGradient id=\"").Append(id).Append('"')
            .Append(" x1=\"").Append(F(0.5 - dx)).Append("\" y1=\"").Append(F(0.5 - dy))
            .Append("\" x2=\"").Append(F(0.5 + dx)).Append("\" y2=\"").Append(F(0.5 + dy)).Append("\">")
            .Append("<stop offset=\"0\" stop-color=\"").Append(Rgb(from)).Append('"')
            .Append(Alpha(from, "stop-opacity")).Append("/>")
            .Append("<stop offset=\"1\" stop-color=\"").Append(Rgb(to)).Append('"')
            .Append(Alpha(to, "stop-opacity")).Append("/>")
            .Append("</linearGradient>");

        return id;
    }

    public void FillEllipse(RectMm r, ColorRgba fill)
    {
        if (fill.IsTransparent)
        {
            return;
        }

        _body.Append("<ellipse cx=\"").Append(F(r.Center.X)).Append("\" cy=\"").Append(F(r.Center.Y)).Append("\" rx=\"")
            .Append(F(r.Width / 2)).Append("\" ry=\"").Append(F(r.Height / 2)).Append("\" fill=\"").Append(Rgb(fill))
            .Append("\"").Append(Alpha(fill, "fill-opacity")).Append("/>");
    }

    public void StrokeEllipse(RectMm r, ColorRgba stroke, double w, DashStyle dash = DashStyle.Solid)
    {
        _body.Append("<ellipse cx=\"").Append(F(r.Center.X)).Append("\" cy=\"").Append(F(r.Center.Y)).Append("\" rx=\"")
            .Append(F(r.Width / 2)).Append("\" ry=\"").Append(F(r.Height / 2)).Append("\" fill=\"none\" stroke=\"")
            .Append(Rgb(stroke)).Append("\" stroke-width=\"").Append(F(w)).Append("\"").Append(Dash(dash, w)).Append("/>");
    }

    public void StrokeLine(PointMm a, PointMm b, ColorRgba stroke, double w, DashStyle dash = DashStyle.Solid)
    {
        _body.Append("<line x1=\"").Append(F(a.X)).Append("\" y1=\"").Append(F(a.Y)).Append("\" x2=\"").Append(F(b.X))
            .Append("\" y2=\"").Append(F(b.Y)).Append("\" stroke=\"").Append(Rgb(stroke)).Append("\" stroke-width=\"")
            .Append(F(w)).Append("\"").Append(Dash(dash, w)).Append("/>");
    }

    public void FillPolygon(IReadOnlyList<PointMm> points, ColorRgba fill, ColorRgba stroke, double w)
    {
        _body.Append("<polygon points=\"");
        foreach (PointMm p in points)
        {
            _body.Append(F(p.X)).Append(',').Append(F(p.Y)).Append(' ');
        }

        _body.Append("\" fill=\"").Append(fill.IsTransparent ? "none" : Rgb(fill)).Append('"');
        if (w > 0 && !stroke.IsTransparent)
        {
            _body.Append(" stroke=\"").Append(Rgb(stroke)).Append("\" stroke-width=\"").Append(F(w)).Append('"');
        }

        _body.Append("/>");
    }

    public void DrawImage(IDecodedImage image, RectMm dest, ImageAdjustments adjustments, double opacity)
    {
        if (image is not SvgEmbeddedImage embedded)
        {
            _body.Append("<rect x=\"").Append(F(dest.X)).Append("\" y=\"").Append(F(dest.Y)).Append("\" width=\"")
                .Append(F(dest.Width)).Append("\" height=\"").Append(F(dest.Height))
                .Append("\" fill=\"none\" stroke=\"#c83c3c\" stroke-dasharray=\"1,1\" stroke-width=\"0.2\"/>");
            return;
        }

        _body.Append("<image x=\"").Append(F(dest.X)).Append("\" y=\"").Append(F(dest.Y)).Append("\" width=\"")
            .Append(F(dest.Width)).Append("\" height=\"").Append(F(dest.Height)).Append("\" opacity=\"")
            .Append(F(Math.Clamp(opacity, 0, 1))).Append("\" preserveAspectRatio=\"none\" href=\"")
            .Append(embedded.DataUri).Append("\"/>");
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
                double spanWidth = TextMeasurer.MeasureWidthMm(span.Text, span.FontFamily, span.FontSizePt, span.Bold, span.Italic);
                _body.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(baseline)).Append("\" font-family=\"")
                    .Append(Escape(span.FontFamily)).Append("\" font-size=\"").Append(F(UnitConverter.PointsToMm(span.FontSizePt)))
                    .Append("\" fill=\"").Append(Rgb(span.Foreground)).Append('"');
                double trackingMm = span.LetterSpacingEm * UnitConverter.PointsToMm(span.FontSizePt);
                if (Math.Abs(trackingMm) >= 1e-6 && !Bidi.NeedsShaping(span.Text))
                {
                    _body.Append(" letter-spacing=\"").Append(F(trackingMm)).Append('"');
                    spanWidth += span.Text.Length * trackingMm;
                }

                if (span.Bold)
                {
                    _body.Append(" font-weight=\"bold\"");
                }

                if (span.Italic)
                {
                    _body.Append(" font-style=\"italic\"");
                }

                if (span.Underline || span.Strikethrough)
                {
                    _body.Append(" text-decoration=\"")
                        .Append(span.Underline ? "underline " : string.Empty)
                        .Append(span.Strikethrough ? "line-through" : string.Empty).Append('"');
                }

                _body.Append(">").Append(Escape(Bidi.ForDisplay(span))).Append("</text>");
                x += spanWidth + (span.Text.EndsWith(' ') ? line.WordGapMm : 0);
            }
        }
    }

    public void FillRectangles(IReadOnlyList<RectMm> rects, ColorRgba fill)
    {
        if (rects.Count == 0 || fill.IsTransparent)
        {
            return;
        }

        _body.Append("<g fill=\"").Append(Rgb(fill)).Append("\">");
        foreach (RectMm r in rects)
        {
            _body.Append("<rect x=\"").Append(F(r.X)).Append("\" y=\"").Append(F(r.Y)).Append("\" width=\"")
                .Append(F(r.Width)).Append("\" height=\"").Append(F(r.Height)).Append("\"/>");
        }

        _body.Append("</g>");
    }

    private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Rgb(ColorRgba c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string Alpha(ColorRgba c, string attr) =>
        c.A == 255 ? string.Empty : $" {attr}=\"{F(c.A / 255.0)}\"";

    private static string Dash(DashStyle dash, double w) => dash switch
    {
        DashStyle.Dash => $" stroke-dasharray=\"{F(w * 3)},{F(w * 2)}\"",
        DashStyle.Dot => $" stroke-dasharray=\"{F(w)},{F(w * 2)}\"",
        DashStyle.DashDot => $" stroke-dasharray=\"{F(w * 3)},{F(w * 2)},{F(w)},{F(w * 2)}\"",
        _ => string.Empty,
    };

    private static string Escape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}

/// <summary>An image the SVG surface can inline, carrying a base64 data URI.</summary>
public sealed class SvgEmbeddedImage : IDecodedImage
{
    public required int PixelWidth { get; init; }
    public required int PixelHeight { get; init; }
    public required string DataUri { get; init; }
}
