using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace LabelMaker.Export.Pdf;

/// <summary>Renders a single barcode to a one-page PDF sized to the symbol (spec §7).</summary>
public static class BarcodePdf
{
    public static byte[] Render(BarcodeSpec spec, string value, double? widthMm = null, double? heightMm = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        WindowsFontResolver.Register();

        BarcodeSymbol symbol = BarcodeEngine.Encode(spec, value);
        PlacedBarcode placed = widthMm is { } w && heightMm is { } h
            ? BarcodeLayout.PlaceFit(symbol, spec, new SizeMm(w, h))
            : BarcodeLayout.PlaceNatural(symbol, spec);

        double pageW = widthMm ?? placed.NaturalSizeMm.Width;
        double pageH = heightMm ?? placed.NaturalSizeMm.Height;

        using var document = new PdfDocument();
        document.Info.Title = $"{spec.Symbology.DisplayName()} {value}";
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(pageW);
        page.Height = XUnit.FromMillimeter(pageH);

        using XGraphics gfx = XGraphics.FromPdfPage(page);
        var surface = new PdfDrawingSurface(gfx, _ => null);

        if (!spec.Background.IsTransparent)
        {
            surface.FillRectangle(new RectMm(0, 0, pageW, pageH), spec.Background);
        }

        surface.FillRectangles(placed.DarkRects.ToList(), spec.Foreground);

        if (placed.HriRect is { } hri && placed.HriText.Length > 0)
        {
            var style = new Core.Model.Objects.TextObject
            {
                FontFamily = spec.HumanReadableFontFamily,
                FontSizePt = spec.HumanReadableFontSizePt,
                Foreground = spec.Foreground,
                HorizontalAlignment = Core.Model.Text.HorizontalAlignment.Center,
                WordWrap = false,
            };
            TextBlock block = TextLayouter.Layout(
                Core.Model.Text.RichTextContent.FromPlainText(placed.HriText), style, hri, surface.TextMeasurer);
            surface.DrawTextBlock(block, hri, Core.Model.Text.VerticalAlignment.Middle);
        }

        using var ms = new MemoryStream();
        document.Save(ms);
        return ms.ToArray();
    }
}
