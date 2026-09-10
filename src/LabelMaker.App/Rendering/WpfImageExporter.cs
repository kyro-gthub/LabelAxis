using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Rendering;

public enum RasterFormat
{
    Png = 0,
    Jpeg = 1,
}

/// <summary>
/// Rasterises sheets / labels to PNG or JPEG at an exact DPI via
/// <see cref="RenderTargetBitmap"/> (Windows-only, exact device pixels — spec §17).
/// PDF export stays vector in <c>LabelMaker.Export</c>.
/// </summary>
public sealed class WpfImageExporter
{
    private readonly SheetVisualRenderer _renderer;

    public WpfImageExporter(SheetVisualRenderer renderer) => _renderer = renderer;

    public byte[] RenderSheetToImage(
        LabelTemplate template,
        Core.Model.LabelDocument master,
        IReadOnlyList<PlacedLabel> labels,
        int dpi,
        RasterFormat format,
        bool transparent = false,
        int jpegQuality = 92)
    {
        PageSize page = template.EffectivePage;
        var mapper = new PrintCoordinateMapper(SheetMedia.FullBleed(page.WidthMm, page.HeightMm));
        DrawingVisual content = _renderer.RenderSheet(template, master, labels, _renderer.OptionsFor(template), mapper);

        return Rasterise(content, page.WidthMm, page.HeightMm, dpi, format, transparent, jpegQuality);
    }

    public byte[] RenderLabelToImage(
        Core.Model.LabelDocument document,
        Core.Variables.ResolveContext context,
        LabelTemplate template,
        int dpi,
        RasterFormat format,
        bool transparent = false,
        int jpegQuality = 92)
    {
        var options = _renderer.OptionsFor(template);
        DrawingVisual content = _renderer.RenderSingleLabel(
            document, context, options,
            transparent ? new Core.Model.ColorRgba(0, 0, 0, 0) : Core.Model.ColorRgba.White);

        return Rasterise(content, document.SizeMm.Width, document.SizeMm.Height, dpi, format, transparent, jpegQuality);
    }

    private static byte[] Rasterise(
        DrawingVisual content, double widthMm, double heightMm, int dpi,
        RasterFormat format, bool transparent, int jpegQuality)
    {
        double scale = dpi / UnitConverter.DiuPerInch; // DIU -> device px
        int pxW = Math.Max(1, (int)Math.Round(UnitConverter.MmToDevicePixels(widthMm, dpi)));
        int pxH = Math.Max(1, (int)Math.Round(UnitConverter.MmToDevicePixels(heightMm, dpi)));

        var host = new DrawingVisual();
        using (DrawingContext dc = host.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawDrawing(content.Drawing);
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(pxW, pxH, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(host);

        BitmapEncoder encoder = format == RasterFormat.Jpeg
            ? new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 10, 100) }
            : new PngBitmapEncoder();

        if (format == RasterFormat.Jpeg || !transparent)
        {
            // Flatten onto white for formats without alpha, or when opaque requested.
            var flattened = new RenderTargetBitmap(pxW, pxH, dpi, dpi, PixelFormats.Pbgra32);
            var flat = new DrawingVisual();
            using (DrawingContext dc = flat.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pxW, pxH));
                dc.DrawImage(rtb, new Rect(0, 0, pxW, pxH));
            }

            flattened.Render(flat);
            encoder.Frames.Add(BitmapFrame.Create(flattened));
        }
        else
        {
            encoder.Frames.Add(BitmapFrame.Create(rtb));
        }

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
