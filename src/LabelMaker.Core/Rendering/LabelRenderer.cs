using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;

namespace LabelMaker.Core.Rendering;

/// <summary>Options that vary between screen preview, PDF and print output.</summary>
public sealed class LabelRenderOptions
{
    public LabelShape LabelShape { get; init; } = LabelShape.Rectangle;
    public double CornerRadiusMm { get; init; }
    public LabelOutlineStyle Outline { get; init; } = LabelOutlineStyle.None;
    public bool ClipToLabel { get; init; } = true;

    /// <summary>Draw a light checkerboard where a background image is transparent (preview only).</summary>
    public bool ShowTransparencyPattern { get; init; }
}

/// <summary>
/// The one renderer. Walks a <see cref="LabelDocument"/> in z-order, resolves
/// <c>{tokens}</c> against a <see cref="ResolveContext"/>, and issues vector
/// primitives to an <see cref="IDrawingSurface"/>. The designer canvas, PDF
/// export and the printer all call this — "what you see is what prints"
/// (spec §29, §30).
/// </summary>
public sealed class LabelRenderer
{
    public void Render(
        LabelDocument document,
        ResolveContext context,
        IDrawingSurface surface,
        LabelRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(surface);
        options ??= new LabelRenderOptions();

        var resolver = new VariableResolver(context);
        RectMm labelRect = document.Bounds;

        if (options.ClipToLabel)
        {
            PushLabelClip(surface, labelRect, options);
        }

        DrawBackground(document.Background, labelRect, surface);

        foreach (LabelObject obj in document.Objects)
        {
            if (!obj.Visible)
            {
                continue;
            }

            RenderObject(obj, resolver, surface);
        }

        if (options.ClipToLabel)
        {
            surface.Pop();
        }

        DrawOutline(surface, labelRect, options);
    }

    private static void PushLabelClip(IDrawingSurface surface, RectMm labelRect, LabelRenderOptions options)
    {
        if (options.LabelShape == LabelShape.Ellipse)
        {
            surface.PushClipEllipse(labelRect);
        }
        else
        {
            double radius = options.LabelShape == LabelShape.RoundRectangle ? options.CornerRadiusMm : 0;
            surface.PushClipRectangle(labelRect, radius);
        }
    }

    private static void DrawOutline(IDrawingSurface surface, RectMm labelRect, LabelRenderOptions options)
    {
        switch (options.Outline)
        {
            case LabelOutlineStyle.Hairline:
                surface.StrokeRectangle(labelRect, new ColorRgba(180, 180, 180), 0.1, DashStyle.Dash,
                    options.LabelShape == LabelShape.RoundRectangle ? options.CornerRadiusMm : 0);
                break;
            case LabelOutlineStyle.Crop:
                DrawCropMarks(surface, labelRect);
                break;
        }
    }

    private static void DrawCropMarks(IDrawingSurface surface, RectMm r)
    {
        const double len = 3.0;
        const double gap = 1.5;
        var c = new ColorRgba(0, 0, 0);
        void Mark(PointMm from, PointMm to) => surface.StrokeLine(from, to, c, 0.12);

        Mark(new PointMm(r.Left - gap - len, r.Top), new PointMm(r.Left - gap, r.Top));
        Mark(new PointMm(r.Left, r.Top - gap - len), new PointMm(r.Left, r.Top - gap));
        Mark(new PointMm(r.Right + gap, r.Top), new PointMm(r.Right + gap + len, r.Top));
        Mark(new PointMm(r.Right, r.Top - gap - len), new PointMm(r.Right, r.Top - gap));
        Mark(new PointMm(r.Left - gap - len, r.Bottom), new PointMm(r.Left - gap, r.Bottom));
        Mark(new PointMm(r.Left, r.Bottom + gap), new PointMm(r.Left, r.Bottom + gap + len));
        Mark(new PointMm(r.Right + gap, r.Bottom), new PointMm(r.Right + gap + len, r.Bottom));
        Mark(new PointMm(r.Right, r.Bottom + gap), new PointMm(r.Right, r.Bottom + gap + len));
    }

    private void DrawBackground(BackgroundSpec background, RectMm labelRect, IDrawingSurface surface)
    {
        if (!background.Enabled)
        {
            return;
        }

        if (!background.FillColor.IsTransparent)
        {
            surface.FillRectangle(labelRect, background.FillColor);
        }

        if (background.HasImage && background.AssetId is { } assetId)
        {
            IDecodedImage? image = surface.Images.Get(assetId);
            if (image is not null)
            {
                RectMm dest = ImageFit.Compute(background.FitMode, labelRect, image.PixelWidth, image.PixelHeight);
                surface.PushOpacity(Math.Clamp(background.Opacity, 0, 1));
                if (Math.Abs(background.RotationDegrees) > 0.01)
                {
                    surface.PushTransform(labelRect.Center.X, labelRect.Center.Y, background.RotationDegrees, 1, 1);
                    surface.DrawImage(image, dest.Offset(-labelRect.Center.X, -labelRect.Center.Y), background.Adjustments, 1.0);
                    surface.Pop();
                }
                else
                {
                    surface.DrawImage(image, dest, background.Adjustments, 1.0);
                }

                surface.Pop();
            }
        }
    }

    private void RenderObject(LabelObject obj, VariableResolver resolver, IDrawingSurface surface)
    {
        RectMm b = obj.Bounds;
        PointMm center = b.Center;
        var local = new RectMm(-b.Width / 2.0, -b.Height / 2.0, b.Width, b.Height);

        if (ObjectGeometry.HasSkew(obj))
        {
            surface.PushMatrix(ObjectGeometry.Matrix(obj));
        }
        else
        {
            surface.PushTransform(center.X, center.Y, obj.RotationDegrees, 1, 1);
        }

        surface.PushOpacity(Math.Clamp(obj.Opacity, 0, 1));

        try
        {
            switch (obj)
            {
                case TextObject text:
                    RenderText(text, local, resolver, surface);
                    break;
                case ImageObject image:
                    RenderImage(image, local, surface);
                    RenderImageBorder(image, local, surface);
                    break;
                case ShapeObject shape:
                    RenderShape(shape, local, surface);
                    break;
                case BarcodeObject barcode:
                    RenderBarcode(barcode, local, resolver, surface);
                    break;
                case TableObject table:
                    RenderTable(table, local, resolver, surface);
                    break;
            }
        }
        finally
        {
            surface.Pop(); // opacity
            surface.Pop(); // transform
        }
    }

    private static void RenderText(TextObject text, RectMm local, VariableResolver resolver, IDrawingSurface surface)
    {
        if (!text.Background.IsTransparent)
        {
            surface.FillRectangle(local, text.Background);
        }

        RichTextContent resolved = ResolveContentTokens(text.Content, resolver);
        RectMm contentRect = new(
            local.X + text.PaddingMm.Left,
            local.Y + text.PaddingMm.Top,
            Math.Max(0.5, local.Width - text.PaddingMm.Horizontal),
            Math.Max(0.5, local.Height - text.PaddingMm.Vertical));

        TextBlock block = TextLayouter.Layout(resolved, text, contentRect, surface.TextMeasurer);
        surface.DrawTextBlock(block, contentRect, text.VerticalAlignment);
    }

    private static RichTextContent ResolveContentTokens(RichTextContent source, VariableResolver resolver)
    {
        var clone = source.Clone();
        foreach (Paragraph paragraph in clone.Paragraphs)
        {
            foreach (TextRun run in paragraph.Runs)
            {
                if (run.Text.IndexOf('{') >= 0)
                {
                    run.Text = resolver.Resolve(run.Text);
                }
            }
        }

        return clone;
    }

    private static void RenderImage(ImageObject image, RectMm local, IDrawingSurface surface)
    {
        IDecodedImage? decoded = surface.Images.Get(image.AssetId);
        if (decoded is null)
        {
            surface.StrokeRectangle(local, new ColorRgba(200, 60, 60), 0.2, DashStyle.Dash);
            return;
        }

        ImageAdjustments adj = image.Adjustments;
        double opacity = image.IsWatermark ? 0.25 : 1.0;

        // "Shape crop": clip the whole object to a circle or rounded box first.
        bool shapeClipped = image.ClipShape != ImageClipShape.None;
        if (shapeClipped)
        {
            if (image.ClipShape == ImageClipShape.Ellipse)
            {
                surface.PushClipEllipse(local);
            }
            else
            {
                surface.PushClipRectangle(local, Math.Max(0, image.ClipCornerRadiusMm));
            }
        }

        double cropW = Math.Clamp(adj.CropWidth, 0.01, 1.0);
        double cropH = Math.Clamp(adj.CropHeight, 0.01, 1.0);
        double cropX = Math.Clamp(adj.CropX, 0.0, 1.0 - cropW);
        double cropY = Math.Clamp(adj.CropY, 0.0, 1.0 - cropH);

        // Fit on the *visible* part of the image, so a cropped photo keeps the
        // proportions the user sees rather than the original file's.
        int visibleW = Math.Max(1, (int)Math.Round(decoded.PixelWidth * cropW));
        int visibleH = Math.Max(1, (int)Math.Round(decoded.PixelHeight * cropH));
        RectMm dest = ImageFit.Compute(image.FitMode, local, visibleW, visibleH);

        if (!adj.HasCrop)
        {
            surface.DrawImage(decoded, dest, adj, opacity);
            if (shapeClipped)
            {
                surface.Pop();
            }

            return;
        }

        // Crop without a surface-level crop API: clip to the destination, then draw
        // the whole image enlarged so the crop window lands exactly on that rect.
        // Works identically on WPF, PDF and SVG because all three support clipping.
        var full = new RectMm(
            dest.X - (cropX * dest.Width / cropW),
            dest.Y - (cropY * dest.Height / cropH),
            dest.Width / cropW,
            dest.Height / cropH);

        surface.PushClipRectangle(dest);
        surface.DrawImage(decoded, full, adj, opacity);
        surface.Pop();

        if (shapeClipped)
        {
            surface.Pop();
        }
    }

    /// <summary>
    /// The outline border, drawn just inside the box so it never sticks out past it, and
    /// following the crop shape (circle or rounded box). A double border is two lines a
    /// third of the thickness each, with a third left open between them.
    /// </summary>
    internal static void RenderImageBorder(ImageObject image, RectMm local, IDrawingSurface surface)
    {
        if (!image.HasBorder)
        {
            return;
        }

        double width = Math.Min(image.BorderWidthMm, Math.Min(local.Width, local.Height) / 2.0);
        if (image.BorderLines == ImageBorderLines.Double)
        {
            double line = width / 3.0;
            StrokeImageOutline(image, local, line / 2.0, line, surface);
            StrokeImageOutline(image, local, width - (line / 2.0), line, surface);
        }
        else
        {
            StrokeImageOutline(image, local, width / 2.0, width, surface);
        }
    }

    /// <summary>One outline line whose centre runs <paramref name="inset"/> inside the box.</summary>
    private static void StrokeImageOutline(ImageObject image, RectMm local, double inset, double strokeWidth, IDrawingSurface surface)
    {
        RectMm r = local.Deflate(inset);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        switch (image.ClipShape)
        {
            case ImageClipShape.Ellipse:
                surface.StrokeEllipse(r, image.BorderColor, strokeWidth, image.BorderDash);
                break;
            case ImageClipShape.RoundedRectangle:
                surface.StrokeRectangle(r, image.BorderColor, strokeWidth, image.BorderDash, Math.Max(0, image.ClipCornerRadiusMm - inset));
                break;
            default:
                surface.StrokeRectangle(r, image.BorderColor, strokeWidth, image.BorderDash);
                break;
        }
    }

    private static void RenderShape(ShapeObject shape, RectMm local, IDrawingSurface surface)
    {
        double sw = shape.StrokeWidthMm;
        ColorRgba? to = shape.GradientTo;
        double angle = shape.GradientAngleDegrees;

        switch (shape.Shape)
        {
            case ShapeKind.Rectangle:
                if (to is { } rectTo)
                {
                    surface.FillRectangleGradient(local, shape.Fill, rectTo, angle);
                }
                else
                {
                    surface.FillRectangle(local, shape.Fill);
                }

                if (sw > 0)
                {
                    surface.StrokeRectangle(local, shape.Stroke, sw, shape.DashStyle);
                }

                break;

            case ShapeKind.RoundRectangle:
                if (to is { } roundTo)
                {
                    surface.FillRectangleGradient(local, shape.Fill, roundTo, angle, shape.CornerRadiusMm);
                }
                else
                {
                    surface.FillRectangle(local, shape.Fill, shape.CornerRadiusMm);
                }

                if (sw > 0)
                {
                    surface.StrokeRectangle(local, shape.Stroke, sw, shape.DashStyle, shape.CornerRadiusMm);
                }

                break;

            case ShapeKind.Ellipse:
                if (to is { } ellipseTo)
                {
                    surface.FillEllipseGradient(local, shape.Fill, ellipseTo, angle);
                }
                else
                {
                    surface.FillEllipse(local, shape.Fill);
                }

                if (sw > 0)
                {
                    surface.StrokeEllipse(local, shape.Stroke, sw, shape.DashStyle);
                }

                break;

            case ShapeKind.Line:
                surface.StrokeLine(
                    new PointMm(local.Left, local.Center.Y),
                    new PointMm(local.Right, local.Center.Y),
                    shape.Stroke, Math.Max(0.05, sw), shape.DashStyle);
                break;

            case ShapeKind.Arrow:
                DrawArrow(surface, local, shape);
                break;

            case ShapeKind.Polygon:
                DrawPolygon(surface, local, shape);
                break;

            case ShapeKind.Custom:
                DrawCustom(surface, local, shape);
                break;
        }
    }

    private static void DrawArrow(IDrawingSurface surface, RectMm local, ShapeObject shape)
    {
        var start = new PointMm(local.Left, local.Center.Y);
        var end = new PointMm(local.Right, local.Center.Y);
        double sw = Math.Max(0.05, shape.StrokeWidthMm);
        surface.StrokeLine(start, end, shape.Stroke, sw, shape.DashStyle);

        double head = Math.Max(1.0, shape.ArrowHeadSizeMm);
        var p1 = new PointMm(end.X - head, end.Y - (head * 0.6));
        var p2 = new PointMm(end.X - head, end.Y + (head * 0.6));
        surface.FillPolygon(new[] { end, p1, p2 }, shape.Stroke, shape.Stroke, 0);
    }

    /// <summary>A shape drawn dot by dot: filled when closed, an open line through the dots otherwise.</summary>
    private static void DrawCustom(IDrawingSurface surface, RectMm local, ShapeObject shape)
    {
        List<PointMm> points = CustomShape.PointsIn(shape, local);
        if (points.Count < 2)
        {
            return;
        }

        if (shape.ClosedPath && points.Count >= 3)
        {
            if (shape.GradientTo is { } to)
            {
                surface.FillPolygonGradient(points, shape.Fill, to, shape.GradientAngleDegrees, shape.Stroke, shape.StrokeWidthMm);
            }
            else
            {
                surface.FillPolygon(points, shape.Fill, shape.Stroke, shape.StrokeWidthMm);
            }

            return;
        }

        double sw = Math.Max(0.05, shape.StrokeWidthMm);
        for (int i = 1; i < points.Count; i++)
        {
            surface.StrokeLine(points[i - 1], points[i], shape.Stroke, sw, shape.DashStyle);
        }
    }

    private static void DrawPolygon(IDrawingSurface surface, RectMm local, ShapeObject shape)
    {
        int n = Math.Clamp(shape.PolygonPoints, 3, 64);
        var points = new List<PointMm>();
        double cx = local.Center.X;
        double cy = local.Center.Y;
        double rx = local.Width / 2.0;
        double ry = local.Height / 2.0;
        bool star = shape.StarInnerRatio is > 0 and < 1;
        int steps = star ? n * 2 : n;

        for (int i = 0; i < steps; i++)
        {
            double angle = (-Math.PI / 2.0) + (i * 2 * Math.PI / steps);
            double factor = star && i % 2 == 1 ? shape.StarInnerRatio : 1.0;
            points.Add(new PointMm(cx + (Math.Cos(angle) * rx * factor), cy + (Math.Sin(angle) * ry * factor)));
        }

        if (shape.GradientTo is { } to)
        {
            surface.FillPolygonGradient(
                points, shape.Fill, to, shape.GradientAngleDegrees, shape.Stroke, shape.StrokeWidthMm);
        }
        else
        {
            surface.FillPolygon(points, shape.Fill, shape.Stroke, shape.StrokeWidthMm);
        }
    }

    private static void RenderBarcode(BarcodeObject barcode, RectMm local, VariableResolver resolver, IDrawingSurface surface)
    {
        string value = resolver.Resolve(barcode.Spec.Data);
        if (!BarcodeEngine.TryEncode(barcode.Spec, value, out BarcodeSymbol? symbol, out _) || symbol is null)
        {
            surface.StrokeRectangle(local, new ColorRgba(200, 60, 60), 0.2, DashStyle.Dash);
            return;
        }

        PlacedBarcode placed = barcode.AutoSize
            ? BarcodeLayout.PlaceNatural(symbol, barcode.Spec)
            : BarcodeLayout.PlaceFit(symbol, barcode.Spec, local.Size);

        // Centre the placement within the object's local rect.
        double dx = barcode.AutoSize ? local.X + ((local.Width - placed.NaturalSizeMm.Width) / 2.0) : local.X;
        double dy = barcode.AutoSize ? local.Y + ((local.Height - placed.NaturalSizeMm.Height) / 2.0) : local.Y;

        if (!barcode.Spec.Background.IsTransparent)
        {
            surface.FillRectangle(
                new RectMm(dx + placed.SymbolRect.X, dy + placed.SymbolRect.Y, placed.SymbolRect.Width, placed.SymbolRect.Height),
                barcode.Spec.Background);
        }

        var shifted = new List<RectMm>(placed.DarkRects.Count);
        foreach (RectMm r in placed.DarkRects)
        {
            shifted.Add(r.Offset(dx, dy));
        }

        surface.FillRectangles(shifted, barcode.Spec.Foreground);

        if (placed.HriRect is { } hri && placed.HriText.Length > 0)
        {
            var hriStyle = new TextObject
            {
                FontFamily = barcode.Spec.HumanReadableFontFamily,
                FontSizePt = barcode.Spec.HumanReadableFontSizePt,
                Foreground = barcode.Spec.Foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Middle,
                WordWrap = false,
                PaddingMm = Thickness.Zero,
            };
            RectMm hriRect = hri.Offset(dx, dy);
            TextBlock block = TextLayouter.Layout(RichTextContent.FromPlainText(placed.HriText), hriStyle, hriRect, surface.TextMeasurer);
            surface.DrawTextBlock(block, hriRect, VerticalAlignment.Middle);
        }
    }

    private static void RenderTable(TableObject table, RectMm local, VariableResolver resolver, IDrawingSurface surface)
    {
        table.Normalize();
        double totalColWeight = table.ColumnWeights.Sum();
        double totalRowWeight = table.RowWeights.Sum();
        if (totalColWeight <= 0 || totalRowWeight <= 0)
        {
            return;
        }

        var colX = new double[table.Columns + 1];
        colX[0] = local.X;
        for (int c = 0; c < table.Columns; c++)
        {
            colX[c + 1] = colX[c] + (local.Width * table.ColumnWeights[c] / totalColWeight);
        }

        var rowY = new double[table.Rows + 1];
        rowY[0] = local.Y;
        for (int r = 0; r < table.Rows; r++)
        {
            rowY[r + 1] = rowY[r] + (local.Height * table.RowWeights[r] / totalRowWeight);
        }

        for (int r = 0; r < table.Rows; r++)
        {
            for (int c = 0; c < table.Columns; c++)
            {
                var cellRect = RectMm.FromLtrb(colX[c], rowY[r], colX[c + 1], rowY[r + 1]);
                if (r == 0 && table.FirstRowIsHeader && !table.HeaderFill.IsTransparent)
                {
                    surface.FillRectangle(cellRect, table.HeaderFill);
                }

                if (table.BorderWidthMm > 0)
                {
                    surface.StrokeRectangle(cellRect, table.BorderColor, table.BorderWidthMm);
                }

                string cellText = resolver.Resolve(table.CellAt(r, c));
                if (cellText.Length == 0)
                {
                    continue;
                }

                var cellStyle = new TextObject
                {
                    FontFamily = table.FontFamily,
                    FontSizePt = table.FontSizePt,
                    Bold = table.Bold,
                    Foreground = r == 0 && table.FirstRowIsHeader && table.HeaderTextColor is { } header ? header : table.TextColor,
                    HorizontalAlignment = table.CellAlignment,
                    VerticalAlignment = VerticalAlignment.Middle,
                    WordWrap = true,
                    PaddingMm = new Thickness(table.CellPaddingMm),
                };
                RectMm padded = cellRect.Deflate(table.CellPaddingMm);
                TextBlock block = TextLayouter.Layout(RichTextContent.FromPlainText(cellText), cellStyle, padded, surface.TextMeasurer);
                surface.DrawTextBlock(block, padded, VerticalAlignment.Middle);
            }
        }
    }
}
