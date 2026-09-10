using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;

namespace LabelMaker.Tests.Support;

/// <summary>An <see cref="IDrawingSurface"/> that records calls so tests can assert on renderer output.</summary>
internal sealed class RecordingSurface : IDrawingSurface
{
    public List<string> Ops { get; } = new();
    public List<string> DrawnText { get; } = new();
    public int RectanglesFilled { get; private set; }
    public int BarcodeRectBatches { get; private set; }

    public ITextMeasurer TextMeasurer { get; } = new MonospaceMeasurer();
    public IImageSource Images { get; } = new NullImages();

    public void PushTransform(double tx, double ty, double rot, double sx, double sy) => Ops.Add($"push({tx:0.##},{ty:0.##},{rot:0.##})");
    public void Pop() => Ops.Add("pop");
    public void PushOpacity(double opacity) => Ops.Add($"opacity({opacity:0.##})");
    public void PushClipRectangle(RectMm rectMm, double cornerRadiusMm = 0) => Ops.Add("clipRect");
    public void PushClipEllipse(RectMm rectMm) => Ops.Add("clipEllipse");
    public void FillRectangle(RectMm rectMm, ColorRgba fill, double cornerRadiusMm = 0) { RectanglesFilled++; Ops.Add("fillRect"); }
    public void StrokeRectangle(RectMm rectMm, ColorRgba stroke, double strokeWidthMm, DashStyle dash = DashStyle.Solid, double cornerRadiusMm = 0) => Ops.Add("strokeRect");
    public void FillEllipse(RectMm rectMm, ColorRgba fill) => Ops.Add("fillEllipse");
    public void StrokeEllipse(RectMm rectMm, ColorRgba stroke, double strokeWidthMm, DashStyle dash = DashStyle.Solid) => Ops.Add("strokeEllipse");
    public void StrokeLine(PointMm a, PointMm b, ColorRgba stroke, double strokeWidthMm, DashStyle dash = DashStyle.Solid) => Ops.Add("line");
    public void FillPolygon(IReadOnlyList<PointMm> points, ColorRgba fill, ColorRgba stroke, double strokeWidthMm) => Ops.Add($"polygon({points.Count})");
    public void DrawImage(IDecodedImage image, RectMm destRectMm, ImageAdjustments adjustments, double opacity) => Ops.Add("image");

    public void DrawTextBlock(TextBlock block, RectMm rectMm, VerticalAlignment verticalAlignment)
    {
        Ops.Add("text");
        foreach (TextLine line in block.Lines)
        {
            string text = string.Concat(line.Spans.Select(s => s.Text));
            if (text.Length > 0)
            {
                DrawnText.Add(text);
            }
        }
    }

    public void FillRectangles(IReadOnlyList<RectMm> rectsMm, ColorRgba fill)
    {
        BarcodeRectBatches++;
        RectanglesFilled += rectsMm.Count;
        Ops.Add($"fillRects({rectsMm.Count})");
    }

    private sealed class MonospaceMeasurer : ITextMeasurer
    {
        public double MeasureWidthMm(string text, string fontFamily, double fontSizePt, bool bold, bool italic) =>
            text.Length * UnitConverter.PointsToMm(fontSizePt) * 0.6;

        public double LineHeightMm(string fontFamily, double fontSizePt) =>
            UnitConverter.PointsToMm(fontSizePt) * 1.2;
    }

    private sealed class NullImages : IImageSource
    {
        public IDecodedImage? Get(string assetId) => null;
    }
}
