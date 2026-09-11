using System.Windows;
using System.Windows.Media;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;
using CoreLabelShape = LabelMaker.Core.Formtec.LabelShape;

namespace LabelMaker.App.Rendering;

/// <summary>
/// Renders one Formtec sheet (or a single label) to a WPF <see cref="DrawingVisual"/>
/// via the shared <see cref="LabelRenderer"/>. Used by the designer canvas, the
/// print paginator and the PNG/JPEG exporter, so screen, print and image output
/// come from one code path (spec §29).
/// </summary>
public sealed class SheetVisualRenderer
{
    private const double K = UnitConverter.DiuPerInch / UnitConverter.MmPerInch;

    private readonly LabelRenderer _renderer = new();
    private readonly ITextMeasurer _measurer;

    public SheetVisualRenderer(ITextMeasurer measurer, IImageSource images)
    {
        _measurer = measurer;
        Images = images;
    }

    /// <summary>Swapped when a new project (with a new asset store) is loaded.</summary>
    public IImageSource Images { get; set; }

    public LabelRenderOptions OptionsFor(LabelTemplate template, LabelOutlineStyle outline = LabelOutlineStyle.None) => new()
    {
        LabelShape = template.Grid.LabelShape switch
        {
            CoreLabelShape.Ellipse => CoreLabelShape.Ellipse,
            CoreLabelShape.RoundRectangle => CoreLabelShape.RoundRectangle,
            _ => CoreLabelShape.Rectangle,
        },
        CornerRadiusMm = template.Grid.CornerRadiusMm,
        Outline = outline,
        ClipToLabel = true,
    };

    /// <summary>Draw one label at the origin (designer canvas). Caller applies zoom as a layout transform.</summary>
    public DrawingVisual RenderSingleLabel(
        Core.Model.LabelDocument document, ResolveContext context, LabelRenderOptions options, ColorRgba paper)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(paper.A, paper.R, paper.G, paper.B)),
                null,
                new Rect(0, 0, document.SizeMm.Width * K, document.SizeMm.Height * K));

            var surface = new WpfDrawingSurface(dc, _measurer, Images);
            _renderer.Render(document, context, surface, options);
        }

        return visual;
    }

    /// <summary>Draw a whole sheet of placed labels in page space (print / export).</summary>
    public DrawingVisual RenderSheet(
        LabelTemplate template,
        Core.Model.LabelDocument master,
        IReadOnlyList<PlacedLabel> labels,
        LabelRenderOptions options,
        PrintCoordinateMapper? mapper = null)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var surface = new WpfDrawingSurface(dc, _measurer, Images);
            foreach (PlacedLabel label in labels)
            {
                RectMm rect = mapper?.PageToCanvas(label.PageRectMm) ?? label.PageRectMm;
                surface.PushTransform(rect.X, rect.Y, 0, 1, 1);
                _renderer.Render(master, label.ResolveContext, surface, options);
                surface.Pop();
            }
        }

        return visual;
    }

    /// <summary>
    /// Draw a sheet where every slot shows a <em>different</em> design (spec §15
    /// "print multiple pages in one sheet") — each <see cref="PlacedDocument"/>
    /// carries its own <see cref="LabelDocument"/> rather than sharing one master.
    /// </summary>
    public DrawingVisual RenderMultiSheet(
        LabelTemplate template,
        IReadOnlyList<PlacedDocument> documents,
        LabelRenderOptions options,
        PrintCoordinateMapper? mapper = null)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var surface = new WpfDrawingSurface(dc, _measurer, Images);
            foreach (PlacedDocument placed in documents)
            {
                RectMm rect = mapper?.PageToCanvas(placed.Slot.RectMm) ?? placed.Slot.RectMm;
                surface.PushTransform(rect.X, rect.Y, 0, 1, 1);
                _renderer.Render(placed.Document, placed.ResolveContext, surface, options);
                surface.Pop();
            }
        }

        return visual;
    }
}
