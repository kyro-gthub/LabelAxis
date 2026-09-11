using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;

namespace LabelMaker.App.Rendering;

/// <summary>
/// Rasterises a single barcode (no label, no other objects) to PNG/JPEG, by building
/// a throwaway one-object <see cref="LabelDocument"/> sized to the symbol's natural
/// extent and reusing the normal label renderer — so the pixels match exactly what
/// the designer canvas and the label PDF/PNG exporters already produce.
/// </summary>
public static class BarcodeRasterizer
{
    public static byte[] RenderPng(WpfImageExporter exporter, BarcodeSpec spec, string value, int dpi = 300, bool transparent = false)
    {
        // The renderer re-resolves BarcodeObject.Spec.Data itself (it doesn't take a
        // separate value), so clone the spec and pin Data to the literal value here —
        // this also keeps the caller's own spec instance untouched.
        BarcodeSpec resolved = spec.Clone();
        resolved.Data = value;

        if (!BarcodeEngine.TryEncode(resolved, value, out BarcodeSymbol? symbol, out _) || symbol is null)
        {
            throw new InvalidOperationException($"Cannot encode \"{value}\" as {resolved.Symbology}.");
        }

        SizeMm natural = BarcodeLayout.NaturalSize(symbol, resolved);
        var document = new LabelDocument { SizeMm = natural };
        document.Objects.Add(new BarcodeObject
        {
            AutoSize = true,
            Bounds = new RectMm(0, 0, natural.Width, natural.Height),
            Spec = resolved,
        });

        var options = new LabelRenderOptions { ClipToLabel = false };
        return exporter.RenderLabelToImage(document, new ResolveContext(), options, dpi, RasterFormat.Png, transparent);
    }
}
