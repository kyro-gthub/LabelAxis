using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace LabelMaker.Export.Pdf;

/// <summary>
/// A PDF whose pages are pictures of printed sheets, each page exactly its paper size. Used to
/// print on network printers that take PDF but not PWG Raster: the printer gets the same
/// pixels the preview showed, at the same physical size.
/// </summary>
public static class RasterPdf
{
    /// <param name="pages">Each sheet as a PNG (or JPEG) picture and its paper size in millimetres.</param>
    public static void Write(Stream output, IEnumerable<(byte[] Image, double WidthMm, double HeightMm)> pages, string title)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(pages);
        using var document = new PdfDocument();
        document.Info.Title = title;
        document.Info.Creator = "LabelAxis";

        foreach ((byte[] image, double widthMm, double heightMm) in pages)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(widthMm);
            page.Height = XUnit.FromMillimeter(heightMm);
            using XGraphics gfx = XGraphics.FromPdfPage(page);
            using var ms = new MemoryStream(image, writable: false);
            using XImage picture = XImage.FromStream(ms);
            gfx.DrawImage(picture, 0, 0, page.Width.Point, page.Height.Point);
        }

        document.Save(output, closeStream: false);
    }
}
