using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using CoreColor = LabelMaker.Core.Model.ColorRgba;
using HAlign = LabelMaker.Core.Model.Text.HorizontalAlignment;
using TextBlock = LabelMaker.Core.Rendering.TextBlock;
using VAlign = LabelMaker.Core.Model.Text.VerticalAlignment;
using WpfDashStyle = LabelMaker.Core.Model.Objects.DashStyle;

namespace LabelMaker.App.Rendering;

/// <summary>
/// Bridges the core <see cref="IDrawingSurface"/> to a WPF <see cref="DrawingContext"/>.
/// The renderer speaks millimetres; this converts to device-independent units
/// (1 DIU = 1/96"). Zoom (screen) and DPI (print / raster) are applied by the
/// caller as an outer transform so the physical mapping stays exact.
/// </summary>
public sealed class WpfDrawingSurface : IDrawingSurface
{
    private const double K = UnitConverter.DiuPerInch / UnitConverter.MmPerInch; // DIU per mm

    private readonly DrawingContext _dc;
    private int _pushCount;

    public WpfDrawingSurface(DrawingContext dc, ITextMeasurer measurer, IImageSource images)
    {
        _dc = dc;
        TextMeasurer = measurer;
        Images = images;
    }

    public ITextMeasurer TextMeasurer { get; }

    public IImageSource Images { get; }

    /// <summary>Number of unmatched Push* calls — callers can assert this is 0 after Render.</summary>
    public int OpenPushDepth => _pushCount;

    public void PushTransform(double tx, double ty, double rot, double sx, double sy)
    {
        var group = new TransformGroup();
        if (Math.Abs(sx - 1) > 1e-9 || Math.Abs(sy - 1) > 1e-9)
        {
            group.Children.Add(new ScaleTransform(sx, sy));
        }

        if (Math.Abs(rot) > 1e-9)
        {
            group.Children.Add(new RotateTransform(rot));
        }

        group.Children.Add(new TranslateTransform(tx * K, ty * K));
        group.Freeze();
        _dc.PushTransform(group);
        _pushCount++;
    }

    public void Pop()
    {
        if (_pushCount > 0)
        {
            _dc.Pop();
            _pushCount--;
        }
    }

    public void PushOpacity(double opacity)
    {
        _dc.PushOpacity(Math.Clamp(opacity, 0, 1));
        _pushCount++;
    }

    public void PushClipRectangle(RectMm rectMm, double cornerRadiusMm = 0)
    {
        Geometry geo = cornerRadiusMm > 0
            ? new RectangleGeometry(Rect(rectMm), cornerRadiusMm * K, cornerRadiusMm * K)
            : new RectangleGeometry(Rect(rectMm));
        geo.Freeze();
        _dc.PushClip(geo);
        _pushCount++;
    }

    public void PushClipEllipse(RectMm rectMm)
    {
        var geo = new EllipseGeometry(Point(rectMm.Center), rectMm.Width / 2 * K, rectMm.Height / 2 * K);
        geo.Freeze();
        _dc.PushClip(geo);
        _pushCount++;
    }

    public void FillRectangle(RectMm rectMm, CoreColor fill, double cornerRadiusMm = 0)
    {
        if (fill.IsTransparent)
        {
            return;
        }

        if (cornerRadiusMm > 0)
        {
            _dc.DrawRoundedRectangle(Brush(fill), null, Rect(rectMm), cornerRadiusMm * K, cornerRadiusMm * K);
        }
        else
        {
            _dc.DrawRectangle(Brush(fill), null, Rect(rectMm));
        }
    }

    public void FillRectangleGradient(
        RectMm rectMm, CoreColor from, CoreColor to, double angleDegrees, double cornerRadiusMm = 0)
    {
        Brush brush = Gradient(from, to, angleDegrees);
        if (cornerRadiusMm > 0)
        {
            _dc.DrawRoundedRectangle(brush, null, Rect(rectMm), cornerRadiusMm * K, cornerRadiusMm * K);
        }
        else
        {
            _dc.DrawRectangle(brush, null, Rect(rectMm));
        }
    }

    public void FillEllipseGradient(RectMm rectMm, CoreColor from, CoreColor to, double angleDegrees) =>
        _dc.DrawEllipse(Gradient(from, to, angleDegrees), null, Point(rectMm.Center),
            rectMm.Width / 2 * K, rectMm.Height / 2 * K);

    public void FillPolygonGradient(
        IReadOnlyList<PointMm> points, CoreColor from, CoreColor to, double angleDegrees,
        CoreColor stroke, double strokeWidthMm)
    {
        if (points.Count < 2)
        {
            return;
        }

        Geometry geo = PolygonGeometry(points, filled: true);
        Pen? pen = stroke.IsTransparent || strokeWidthMm <= 0 ? null : Pen(stroke, strokeWidthMm, WpfDashStyle.Solid);
        _dc.DrawGeometry(Gradient(from, to, angleDegrees), pen, geo);
    }

    /// <summary>
    /// Angle is degrees clockwise from left-to-right, expressed in the brush's own
    /// 0..1 space so it follows the shape whatever its aspect ratio.
    /// </summary>
    private static Brush Gradient(CoreColor from, CoreColor to, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180.0;
        double dx = Math.Cos(radians) / 2.0;
        double dy = Math.Sin(radians) / 2.0;

        var brush = new LinearGradientBrush(
            System.Windows.Media.Color.FromArgb(from.A, from.R, from.G, from.B),
            System.Windows.Media.Color.FromArgb(to.A, to.R, to.G, to.B),
            new Point(0.5 - dx, 0.5 - dy),
            new Point(0.5 + dx, 0.5 + dy));

        brush.Freeze();
        return brush;
    }

    public void StrokeRectangle(RectMm rectMm, CoreColor stroke, double strokeWidthMm, WpfDashStyle dash = WpfDashStyle.Solid, double cornerRadiusMm = 0)
    {
        if (stroke.IsTransparent || strokeWidthMm <= 0)
        {
            return;
        }

        Pen pen = Pen(stroke, strokeWidthMm, dash);
        Rect r = Rect(rectMm);
        r.Inflate(-pen.Thickness / 2, -pen.Thickness / 2);
        if (cornerRadiusMm > 0)
        {
            _dc.DrawRoundedRectangle(null, pen, r, cornerRadiusMm * K, cornerRadiusMm * K);
        }
        else
        {
            _dc.DrawRectangle(null, pen, r);
        }
    }

    public void FillEllipse(RectMm rectMm, CoreColor fill)
    {
        if (!fill.IsTransparent)
        {
            _dc.DrawEllipse(Brush(fill), null, Point(rectMm.Center), rectMm.Width / 2 * K, rectMm.Height / 2 * K);
        }
    }

    public void StrokeEllipse(RectMm rectMm, CoreColor stroke, double strokeWidthMm, WpfDashStyle dash = WpfDashStyle.Solid)
    {
        if (!stroke.IsTransparent && strokeWidthMm > 0)
        {
            _dc.DrawEllipse(null, Pen(stroke, strokeWidthMm, dash), Point(rectMm.Center),
                (rectMm.Width / 2 * K) - (strokeWidthMm * K / 2), (rectMm.Height / 2 * K) - (strokeWidthMm * K / 2));
        }
    }

    public void StrokeLine(PointMm a, PointMm b, CoreColor stroke, double strokeWidthMm, WpfDashStyle dash = WpfDashStyle.Solid)
    {
        if (!stroke.IsTransparent && strokeWidthMm > 0)
        {
            _dc.DrawLine(Pen(stroke, strokeWidthMm, dash), Point(a), Point(b));
        }
    }

    public void FillPolygon(IReadOnlyList<PointMm> points, CoreColor fill, CoreColor stroke, double strokeWidthMm)
    {
        if (points.Count < 2)
        {
            return;
        }

        Geometry geo = PolygonGeometry(points, filled: !fill.IsTransparent);
        Brush? brush = fill.IsTransparent ? null : Brush(fill);
        Pen? pen = stroke.IsTransparent || strokeWidthMm <= 0 ? null : Pen(stroke, strokeWidthMm, WpfDashStyle.Solid);
        _dc.DrawGeometry(brush, pen, geo);
    }

    private Geometry PolygonGeometry(IReadOnlyList<PointMm> points, bool filled)
    {
        var figure = new PathFigure { StartPoint = Point(points[0]), IsClosed = true, IsFilled = filled };
        for (int i = 1; i < points.Count; i++)
        {
            figure.Segments.Add(new LineSegment(Point(points[i]), true));
        }

        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        geo.Freeze();
        return geo;
    }

    public void DrawImage(IDecodedImage image, RectMm destRectMm, ImageAdjustments adjustments, double opacity)
    {
        if (image is not WpfDecodedImage wpf)
        {
            return;
        }

        ImageSource source = wpf.ApplyAdjustments(adjustments);
        if (opacity < 1.0)
        {
            _dc.PushOpacity(opacity);
        }

        _dc.DrawImage(source, Rect(destRectMm));
        if (opacity < 1.0)
        {
            _dc.Pop();
        }
    }

    public void DrawTextBlock(TextBlock block, RectMm rectMm, VAlign verticalAlignment)
    {
        double y = rectMm.Y + verticalAlignment switch
        {
            VAlign.Middle => Math.Max(0, (rectMm.Height - block.TotalHeightMm) / 2.0),
            VAlign.Bottom => Math.Max(0, rectMm.Height - block.TotalHeightMm),
            _ => 0,
        };

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
                HAlign.Center => rectMm.X + Math.Max(0, (rectMm.Width - line.WidthMm) / 2.0),
                HAlign.Right => rectMm.X + Math.Max(0, rectMm.Width - line.WidthMm),
                _ => rectMm.X,
            };

            foreach (TextSpan span in line.Spans)
            {
                FormattedText ft = BuildFormattedText(span);
                double baseline = lineTop + line.BaselineOffsetMm;
                _dc.DrawText(ft, new Point(x * K, (baseline * K) - ft.Baseline));
                x += ft.WidthIncludingTrailingWhitespace / K;
            }
        }
    }

    public void FillRectangles(IReadOnlyList<RectMm> rectsMm, CoreColor fill)
    {
        if (rectsMm.Count == 0 || fill.IsTransparent)
        {
            return;
        }

        Brush brush = Brush(fill);
        var geo = new StreamGeometry();
        using (StreamGeometryContext ctx = geo.Open())
        {
            foreach (RectMm r in rectsMm)
            {
                ctx.BeginFigure(new Point(r.X * K, r.Y * K), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(r.Right * K, r.Y * K), false, false);
                ctx.LineTo(new Point(r.Right * K, r.Bottom * K), false, false);
                ctx.LineTo(new Point(r.X * K, r.Bottom * K), false, false);
            }
        }

        geo.Freeze();
        _dc.DrawGeometry(brush, null, geo);
    }

    private FormattedText BuildFormattedText(TextSpan span)
    {
        var typeface = new Typeface(
            new FontFamily(span.FontFamily),
            span.Italic ? FontStyles.Italic : FontStyles.Normal,
            span.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        double emDiu = UnitConverter.PointsToMm(span.FontSizePt) * K;
        var ft = new FormattedText(
            span.Text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            Math.Max(0.1, emDiu),
            new SolidColorBrush(Color(span.Foreground)),
            1.0)
        {
            TextAlignment = TextAlignment.Left,
        };

        if (span.Underline || span.Strikethrough)
        {
            var decorations = new TextDecorationCollection();
            if (span.Underline)
            {
                decorations.Add(TextDecorations.Underline);
            }

            if (span.Strikethrough)
            {
                decorations.Add(TextDecorations.Strikethrough);
            }

            ft.SetTextDecorations(decorations);
        }

        return ft;
    }

    private static Rect Rect(RectMm r) => new(r.X * K, r.Y * K, Math.Max(0, r.Width * K), Math.Max(0, r.Height * K));

    private static Point Point(PointMm p) => new(p.X * K, p.Y * K);

    private static Color Color(CoreColor c) => System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B);

    private static SolidColorBrush Brush(CoreColor c)
    {
        var b = new SolidColorBrush(Color(c));
        b.Freeze();
        return b;
    }

    private static Pen Pen(CoreColor c, double widthMm, WpfDashStyle dash)
    {
        var pen = new Pen(Brush(c), Math.Max(0.05, widthMm * K));
        pen.DashStyle = dash switch
        {
            WpfDashStyle.Dash => DashStyles.Dash,
            WpfDashStyle.Dot => DashStyles.Dot,
            WpfDashStyle.DashDot => DashStyles.DashDot,
            _ => DashStyles.Solid,
        };
        pen.Freeze();
        return pen;
    }
}
