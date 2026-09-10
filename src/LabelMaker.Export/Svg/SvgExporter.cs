using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;

namespace LabelMaker.Export.Svg;

/// <summary>Renders a single label, or a standalone barcode, to an SVG string (spec §7, §17).</summary>
public sealed class SvgExporter
{
    private readonly LabelRenderer _renderer = new();
    private readonly ITextMeasurer _measurer = new ApproxTextMeasurer();

    public string ExportLabel(
        Core.Model.LabelDocument label,
        ResolveContext context,
        AssetStore? assets = null,
        LabelShape shape = LabelShape.Rectangle,
        double cornerRadiusMm = 0)
    {
        ArgumentNullException.ThrowIfNull(label);
        var images = assets is null ? NullImages.Instance : (IImageSource)new ExportImageSource(assets);
        var surface = new SvgDrawingSurface(label.SizeMm.Width, label.SizeMm.Height, _measurer, images);

        _renderer.Render(label, context, surface, new LabelRenderOptions
        {
            LabelShape = shape,
            CornerRadiusMm = cornerRadiusMm,
            ClipToLabel = true,
        });

        return surface.ToSvg();
    }

    /// <summary>Standalone barcode SVG (spec §7).</summary>
    public string ExportBarcode(BarcodeSpec spec, string value, double? widthMm = null, double? heightMm = null, bool transparentBackground = false)
    {
        ArgumentNullException.ThrowIfNull(spec);
        BarcodeSymbol symbol = BarcodeEngine.Encode(spec, value);

        PlacedBarcode placed = widthMm is { } w && heightMm is { } h
            ? BarcodeLayout.PlaceFit(symbol, spec, new SizeMm(w, h))
            : BarcodeLayout.PlaceNatural(symbol, spec);

        SizeMm size = new(
            widthMm ?? placed.NaturalSizeMm.Width,
            heightMm ?? placed.NaturalSizeMm.Height);

        var surface = new SvgDrawingSurface(size.Width, size.Height, _measurer, NullImages.Instance);

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
                Core.Model.Text.RichTextContent.FromPlainText(placed.HriText), style, hri, _measurer);
            surface.DrawTextBlock(block, hri, Core.Model.Text.VerticalAlignment.Middle);
        }

        return surface.ToSvg(transparentBackground ? null : spec.Background);
    }

    private sealed class NullImages : IImageSource
    {
        public static readonly NullImages Instance = new();

        public IDecodedImage? Get(string assetId) => null;
    }
}
