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

namespace LabelMaker.App.Printing;

/// <summary>
/// A thermal print job laid out and ready to render: one printed "page" per row of labels
/// on the roll. A project designed on an A4 sheet prints one label per row at label size.
/// </summary>
public sealed class ThermalJobPlan
{
    private readonly DocumentPaginator _paginator;

    public ThermalJobPlan(DocumentPaginator paginator, LabelTemplate roll, PrintJobSummary summary)
    {
        _paginator = paginator;
        Roll = roll;
        Summary = summary;
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
            return new ThermalJobPlan(new MultiPageSheetPaginator(rows, roll, renderer, media, identity, 100, outline), roll, summary);
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
        return new ThermalJobPlan(
            new LabelSheetPaginator(printed, roll, project.MasterLabel, renderer, media, identity, 100, outline),
            roll,
            PrintJobSummary.FromSheets(allRows, printed, first));
    }

    /// <summary>
    /// Renders, encodes and sends every row, reporting rows sent. Rendering happens on the
    /// calling (UI) thread between awaits, so the window stays responsive.
    /// </summary>
    public async Task<int> SendAsync(ThermalPrintSettings settings, IThermalSink sink, int copies, IProgress<int>? progress, CancellationToken cancellation)
    {
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
}
