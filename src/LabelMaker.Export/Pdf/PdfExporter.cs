using LabelMaker.Core.Bulk;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace LabelMaker.Export.Pdf;

/// <summary>
/// Renders a project to a vector PDF (spec §17): whole Formtec sheets, individual
/// labels, or one page per record. Text, shapes and barcodes stay vector; the
/// page is the true media size so physical dimensions are preserved.
/// </summary>
public sealed class PdfExporter
{
    private readonly LabelRenderer _renderer = new();

    public byte[] Export(ExportRequest request, AssetStore assets) =>
        Export(request, assets, null, CancellationToken.None);

    /// <summary>
    /// Exports with progress (pages written so far) and cancellation checked between
    /// pages, so a job of thousands of sheets can run in the background.
    /// </summary>
    public byte[] Export(ExportRequest request, AssetStore assets, IProgress<int>? progress, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        WindowsFontResolver.Register();

        LabelTemplate template = request.Project.Template
            ?? throw new InvalidOperationException("The project has no template selected.");

        using var document = new PdfDocument();
        document.Info.Title = request.Title;
        document.Info.Creator = "Label Maker 1.0";

        (byte[] Bytes, int Width, int Height)? Resolver(string id)
        {
            ProjectAsset? a = assets.Get(id);
            if (a is null)
            {
                return null;
            }

            (int w, int h) = ImageDimensions.Probe(a.Bytes);
            return (a.Bytes, w, h);
        }

        switch (request.Scope)
        {
            case ExportScope.Sheet:
                ExportSheets(document, request, template, Resolver, progress, cancellation);
                break;
            case ExportScope.IndividualLabels:
            case ExportScope.PerRecord:
                ExportPerLabel(document, request, template, Resolver, progress, cancellation);
                break;
        }

        if (document.PageCount == 0)
        {
            document.AddPage();
        }

        using var ms = new MemoryStream();
        document.Save(ms);
        return ms.ToArray();
    }

    private void ExportSheets(
        PdfDocument document, ExportRequest request, LabelTemplate template,
        Func<string, (byte[] Bytes, int Width, int Height)?> resolver,
        IProgress<int>? progress, CancellationToken cancellation)
    {
        var plan = new BulkRenderPlan
        {
            Template = template,
            MasterLabel = request.Project.MasterLabel,
            Data = request.Data ?? Core.Data.DataSet.Empty,
            Sequences = request.Project.Sequences,
            Variables = request.Project.Variables,
            StartPosition = request.StartPosition,
            SkipPositions = request.SkipPositions ?? new HashSet<int>(),
            RecordSelection = request.RecordSelection,
            FillSheetWhenNoData = request.FillSheetWhenNoData,
        };

        PageSize media = template.EffectivePage;

        int number = 0;
        foreach (PlannedSheet sheet in SheetLayoutEngine.PlanSheets(plan))
        {
            cancellation.ThrowIfCancellationRequested();
            number++;
            if (number < (request.FirstSheet ?? 1))
            {
                continue;
            }

            if (request.LastSheet is { } last && number > last)
            {
                break;
            }

            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(media.WidthMm);
            page.Height = XUnit.FromMillimeter(media.HeightMm);

            using XGraphics gfx = XGraphics.FromPdfPage(page);
            var surface = new PdfDrawingSurface(gfx, resolver);

            foreach (PlacedLabel label in sheet.Labels)
            {
                RenderLabelAt(surface, request, template, label.PageRectMm, label.ResolveContext);
            }

            progress?.Report(document.PageCount);
        }
    }

    private void ExportPerLabel(
        PdfDocument document, ExportRequest request, LabelTemplate template,
        Func<string, (byte[] Bytes, int Width, int Height)?> resolver,
        IProgress<int>? progress, CancellationToken cancellation)
    {
        var plan = new BulkRenderPlan
        {
            Template = template,
            MasterLabel = request.Project.MasterLabel,
            Data = request.Data ?? Core.Data.DataSet.Empty,
            Sequences = request.Project.Sequences,
            Variables = request.Project.Variables,
            StartPosition = 1,
            RecordSelection = request.RecordSelection,
            FillSheetWhenNoData = false,
        };

        var size = request.Project.MasterLabel.SizeMm;
        IEnumerable<PlacedLabel> labels = plan.Enumerate();
        bool any = false;

        foreach (PlacedLabel label in labels)
        {
            cancellation.ThrowIfCancellationRequested();
            any = true;
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(size.Width);
            page.Height = XUnit.FromMillimeter(size.Height);

            using XGraphics gfx = XGraphics.FromPdfPage(page);
            var surface = new PdfDrawingSurface(gfx, resolver);
            RenderLabelAt(surface, request, template, new RectMm(0, 0, size.Width, size.Height), label.ResolveContext);
            progress?.Report(document.PageCount);
        }

        if (!any)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(size.Width);
            page.Height = XUnit.FromMillimeter(size.Height);
            using XGraphics gfx = XGraphics.FromPdfPage(page);
            var surface = new PdfDrawingSurface(gfx, resolver);
            RenderLabelAt(surface, request, template, new RectMm(0, 0, size.Width, size.Height),
                new ResolveContext { Variables = request.Project.Variables });
        }
    }

    private void RenderLabelAt(
        PdfDrawingSurface surface, ExportRequest request, LabelTemplate template,
        RectMm pageRectMm, ResolveContext context)
    {
        surface.PushTransform(pageRectMm.X, pageRectMm.Y, 0, 1, 1);
        var options = new LabelRenderOptions
        {
            LabelShape = template.Grid.LabelShape switch
            {
                LabelShape.Ellipse => LabelShape.Ellipse,
                LabelShape.RoundRectangle => LabelShape.RoundRectangle,
                _ => LabelShape.Rectangle,
            },
            CornerRadiusMm = template.Grid.CornerRadiusMm,
            Outline = request.DrawCropMarks ? LabelOutlineStyle.Crop
                : request.DrawLabelOutlines ? LabelOutlineStyle.Hairline
                : LabelOutlineStyle.None,
            ClipToLabel = true,
        };
        _renderer.Render(request.Project.MasterLabel, context, surface, options);
        surface.Pop();
    }
}
