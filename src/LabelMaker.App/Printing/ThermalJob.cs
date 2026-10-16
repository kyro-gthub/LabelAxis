using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LabelMaker.App.Rendering;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Data;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Printing.Thermal;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;

namespace LabelMaker.App.Printing;

/// <summary>One label of a row: its design, its record and where it sits in the row (millimetres).</summary>
public sealed record ThermalJobLabel(LabelDocument Document, ResolveContext Context, RectMm Slot);

/// <summary>
/// A thermal print job laid out and ready to render: one printed "page" per row of labels
/// on the roll. A project designed on an A4 sheet prints one label per row at label size.
/// </summary>
public sealed class ThermalJobPlan
{
    private readonly DocumentPaginator _paginator;
    private readonly IReadOnlyList<IReadOnlyList<ThermalJobLabel>> _rows;
    private readonly SheetVisualRenderer _renderer;
    private readonly LabelOutlineStyle _outline;

    public ThermalJobPlan(
        DocumentPaginator paginator,
        LabelTemplate roll,
        PrintJobSummary summary,
        IReadOnlyList<IReadOnlyList<ThermalJobLabel>> rows,
        SheetVisualRenderer renderer,
        LabelOutlineStyle outline)
    {
        _paginator = paginator;
        Roll = roll;
        Summary = summary;
        _rows = rows;
        _renderer = renderer;
        _outline = outline;
    }

    /// <summary>The roll the job prints on (the project's own roll, or one label across at label size).</summary>
    public LabelTemplate Roll { get; }

    public PrintJobSummary Summary { get; }

    /// <summary>Rows (printer "labels") in the job.</summary>
    public int Count => _paginator.PageCount;

    /// <summary>
    /// Renders row <paramref name="index"/> at the print head's resolution and turns it into
    /// dots. Must run on the UI thread (WPF rendering); the conversion is quick.
    /// </summary>
    public MonochromeImage Rasterize(int index, ThermalPrintSettings settings)
    {
        DocumentPage page = _paginator.GetPage(index);
        PageSize size = Roll.EffectivePage;
        int width = Math.Max(1, settings.Dots(size.WidthMm));
        int height = Math.Max(1, settings.Dots(size.HeightMm));

        // Bars and boxes land on whole dots; grey edges would print unevenly.
        RenderOptions.SetEdgeMode(page.Visual, EdgeMode.Aliased);

        var paper = new DrawingVisual();
        using (DrawingContext dc = paper.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(page.Size));
        }

        var bitmap = new RenderTargetBitmap(width, height, settings.Dpi, settings.Dpi, PixelFormats.Pbgra32);
        bitmap.Render(paper);
        bitmap.Render(page.Visual);

        var gray = new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0);
        var pixels = new byte[width * height];
        gray.CopyPixels(pixels, width, 0);
        return MonochromeImage.FromGray(pixels, width, height, settings.Dither, settings.Threshold);
    }

    /// <summary>
    /// Lays out the job the print settings describe (records, sheet fill or standalone pages,
    /// record range, sheet range) on the project's roll.
    /// </summary>
    public static ThermalJobPlan Build(ProjectModel project, DataSet? data, PrintSettings settings, SheetVisualRenderer renderer)
    {
        LabelTemplate roll = project.Template is { IsRoll: true } own
            ? own
            : LabelTemplate.CreateRoll("thermal-single", "One label across", project.MasterLabel.SizeMm.Width, project.MasterLabel.SizeMm.Height);
        PageSize page = roll.EffectivePage;
        var media = SheetMedia.FullBleed(page.WidthMm, page.HeightMm);
        PrinterCalibration identity = PrinterCalibration.Identity(string.Empty);
        LabelOutlineStyle outline = settings.PrintLabelOutlines ? LabelOutlineStyle.Hairline : LabelOutlineStyle.None;

        if (settings.Content == PrintContent.Pages)
        {
            IReadOnlyList<LabelDocument> documents = PrintablePages.For(project, settings);
            var all = MultiDocumentSheetLayout.PlanSheets(new MultiDocumentRenderPlan
            {
                Template = roll,
                Documents = documents,
                StartPosition = 1,
                SkipPositions = new HashSet<int>(),
            }).ToList();
            (IReadOnlyList<PlannedMultiSheet> rows, int firstRow) = settings.ApplySheetRange<PlannedMultiSheet>(all);
            var summary = new PrintJobSummary
            {
                Sheets = rows.Count,
                TotalSheets = all.Count,
                Labels = rows.Sum(r => r.Documents.Count),
                FirstSheet = firstRow,
            };
            IReadOnlyList<IReadOnlyList<ThermalJobLabel>> pageRows = rows
                .Select(r => (IReadOnlyList<ThermalJobLabel>)r.Documents.Select(d => new ThermalJobLabel(d.Document, d.ResolveContext, d.Slot.RectMm)).ToList())
                .ToList();
            return new ThermalJobPlan(new MultiPageSheetPaginator(rows, roll, renderer, media, identity, 100, outline), roll, summary, pageRows, renderer, outline);
        }

        BulkRenderPlan basis = SheetLayoutEngine.CreatePlan(project, data, settings, roll);

        // On a roll every label is used in order; sheet start slots and skipped slots do not apply.
        var plan = new BulkRenderPlan
        {
            Template = roll,
            MasterLabel = project.MasterLabel,
            Data = basis.Data,
            Sequences = basis.Sequences,
            Variables = basis.Variables,
            StartPosition = 1,
            SkipPositions = new HashSet<int>(),
            RecordSelection = basis.RecordSelection,
            FillSheetWhenNoData = basis.FillSheetWhenNoData,
        };
        var allRows = SheetLayoutEngine.PlanSheets(plan).ToList();
        (IReadOnlyList<PlannedSheet> printed, int first) = settings.ApplySheetRange<PlannedSheet>(allRows);
        IReadOnlyList<IReadOnlyList<ThermalJobLabel>> labelRows = printed
            .Select(r => (IReadOnlyList<ThermalJobLabel>)r.Labels.Select(l => new ThermalJobLabel(project.MasterLabel, l.ResolveContext, l.PageRectMm)).ToList())
            .ToList();
        return new ThermalJobPlan(
            new LabelSheetPaginator(printed, roll, project.MasterLabel, renderer, media, identity, 100, outline),
            roll,
            PrintJobSummary.FromSheets(allRows, printed, first),
            labelRows,
            renderer,
            outline);
    }

    /// <summary>For the first design in the job: how many objects the printer draws itself, and how many go as pictures with every label.</summary>
    public (int Drawn, int Pictures) NativeSummary()
    {
        if (_rows.Count == 0 || _rows[0].Count == 0)
        {
            return (0, 0);
        }

        NativeDesign design = NativeDesign.For(_rows[0][0].Document);
        return (design.Drawn.Count, design.Pictures.Count);
    }

    /// <summary>
    /// Renders, encodes and sends every row, reporting rows sent. Rendering happens on the
    /// calling (UI) thread between awaits, so the window stays responsive.
    /// </summary>
    public async Task<int> SendAsync(ThermalPrintSettings settings, IThermalSink sink, int copies, IProgress<int>? progress, CancellationToken cancellation)
    {
        if (settings.Mode == ThermalSendMode.PrinterNative)
        {
            return await SendNativeAsync(settings, sink, copies, progress, cancellation);
        }

        IThermalEncoder encoder = ThermalEncoders.For(settings.Language);
        RollMedia rollMedia = Roll.Roll ?? new RollMedia();
        double lengthMm = Roll.EffectivePage.HeightMm;
        int sent = 0;

        for (int i = 0; i < Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            MonochromeImage image = Rasterize(i, settings);
            byte[] bytes = await Task.Run(() => encoder.EncodeLabel(image, settings, rollMedia, lengthMm, copies), cancellation);
            await sink.WriteAsync(bytes, cancellation);
            sent++;
            progress?.Report(sent);

            // Let the window paint and the Cancel button respond between labels.
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        }

        await sink.CompleteAsync(cancellation);
        return sent;
    }

    /// <summary>
    /// Printer-native mode: each design's fixed parts are drawn once and stored on the printer;
    /// every row then carries only its changing text and barcodes as printer commands, plus a
    /// small picture for anything the printer cannot draw itself.
    /// </summary>
    private async Task<int> SendNativeAsync(ThermalPrintSettings settings, IThermalSink sink, int copies, IProgress<int>? progress, CancellationToken cancellation)
    {
        INativeThermalEncoder encoder = NativeThermalEncoders.For(settings.Language);
        RollMedia rollMedia = Roll.Roll ?? new RollMedia();
        PageSize page = Roll.EffectivePage;
        double lengthMm = page.HeightMm;
        int rowWidth = Math.Max(1, settings.Dots(page.WidthMm));
        int rowHeight = Math.Max(1, settings.Dots(page.HeightMm));

        var designs = new List<NativeDesign>();
        var backgrounds = new List<MonochromeImage?>();
        var index = new Dictionary<LabelDocument, int>(ReferenceEqualityComparer.Instance);
        foreach (ThermalJobLabel label in _rows.SelectMany(r => r))
        {
            if (!index.ContainsKey(label.Document))
            {
                index[label.Document] = designs.Count;
                NativeDesign design = NativeDesign.For(label.Document);
                designs.Add(design);
                backgrounds.Add(Background(design, label.Context, settings));
            }
        }

        await sink.WriteAsync(encoder.BeginJob(backgrounds, settings, rollMedia, rowWidth, lengthMm), cancellation);
        int sent = 0;
        foreach (IReadOnlyList<ThermalJobLabel> labels in _rows)
        {
            cancellation.ThrowIfCancellationRequested();
            var row = new NativeRow { WidthDots = rowWidth, HeightDots = rowHeight };
            foreach (ThermalJobLabel label in labels)
            {
                int d = index[label.Document];
                NativeLabelBuilder.Result built = NativeLabelBuilder.Build(
                    designs[d], label.Context, settings, _renderer.Measurer, encoder,
                    settings.Dots(label.Slot.X), settings.Dots(label.Slot.Y), backgrounds[d] is null ? -1 : d);
                AddPictures(built, label, settings);
                row.Labels.Add(built.Label);
            }

            byte[] bytes = encoder.EncodeRow(row, backgrounds, settings, rollMedia, lengthMm, copies);
            await sink.WriteAsync(bytes, cancellation);
            sent++;
            progress?.Report(sent);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        }

        await sink.WriteAsync(encoder.EndJob(backgrounds), cancellation);
        await sink.CompleteAsync(cancellation);
        return sent;
    }

    /// <summary>
    /// The design's fixed parts as print-head dots, with the areas of the changing objects sent as
    /// pictures left blank (so light-on-dark fields print exactly); null when nothing is fixed.
    /// </summary>
    private MonochromeImage? Background(NativeDesign design, ResolveContext context, ThermalPrintSettings settings)
    {
        MonochromeImage image = RasterizeLabel(design.BackgroundDocument(), context, settings);
        foreach (LabelObject obj in design.Pictures)
        {
            DotRect area = NativeLabelBuilder.Area(obj, settings);
            image.Clear(area.X, area.Y, area.Width, area.Height);
        }

        return image.Coverage > 0 ? image : null;
    }

    /// <summary>Cuts what the printer cannot draw out of the label as drawn on screen; drawn first, so printer text and barcodes stay on top.</summary>
    private void AddPictures(NativeLabelBuilder.Result built, ThermalJobLabel label, ThermalPrintSettings settings)
    {
        if (built.NeedPictures.Count == 0)
        {
            return;
        }

        MonochromeImage full = RasterizeLabel(label.Document, label.Context, settings);
        int at = 0;
        foreach ((LabelObject _, DotRect area) in built.NeedPictures)
        {
            if (full.Crop(area.X, area.Y, area.Width, area.Height) is { } part && part.Coverage > 0)
            {
                built.Label.Elements.Insert(at++, new NativePicture(Math.Max(0, area.X), Math.Max(0, area.Y), part));
            }
        }
    }

    /// <summary>One label drawn as it is on screen, at the print head's resolution.</summary>
    private MonochromeImage RasterizeLabel(LabelDocument document, ResolveContext context, ThermalPrintSettings settings)
    {
        int width = Math.Max(1, settings.Dots(document.SizeMm.Width));
        int height = Math.Max(1, settings.Dots(document.SizeMm.Height));
        DrawingVisual visual = _renderer.RenderSingleLabel(document, context, _renderer.OptionsFor(Roll, _outline), ColorRgba.White);
        RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);

        var bitmap = new RenderTargetBitmap(width, height, settings.Dpi, settings.Dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var gray = new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0);
        var pixels = new byte[width * height];
        gray.CopyPixels(pixels, width, 0);
        return MonochromeImage.FromGray(pixels, width, height, settings.Dither, settings.Threshold);
    }
}
