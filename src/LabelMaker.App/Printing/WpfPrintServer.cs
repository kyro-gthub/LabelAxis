using System.IO;
using System.Printing;
using PrintOrientation = System.Printing.PageOrientation;
using System.Windows.Controls;
using System.Windows.Documents;
using LabelMaker.App.Rendering;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Data;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Printing;

/// <summary>Discovers printers, resolves media geometry, and prints label sheets 1:1.</summary>
public sealed class WpfPrintServer
{
    private readonly SheetVisualRenderer _renderer;
    private readonly CalibrationStore _calibration;

    public WpfPrintServer(SheetVisualRenderer renderer, CalibrationStore calibration)
    {
        _renderer = renderer;
        _calibration = calibration;
    }

    public IReadOnlyList<string> InstalledPrinters()
    {
        try
        {
            using var server = new LocalPrintServer();
            return server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections })
                .Select(q => q.FullName)
                .OrderBy(n => n)
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    public string? DefaultPrinter()
    {
        try
        {
            return LocalPrintServer.GetDefaultPrintQueue()?.FullName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private readonly Dictionary<(string Printer, double W, double H), SheetMedia> _mediaCache = new();

    /// <summary>
    /// Physical media geometry for a printer, including its unprintable border.
    /// Asking the driver for capabilities is slow (tens to hundreds of ms, more for
    /// network printers) and the answer does not change during a session, so it is
    /// cached — the print dialog and preview used to query it on every keystroke.
    /// </summary>
    public SheetMedia GetMedia(string? printerName, double pageWidthMm, double pageHeightMm)
    {
        var key = (printerName ?? string.Empty, Math.Round(pageWidthMm, 2), Math.Round(pageHeightMm, 2));
        if (_mediaCache.TryGetValue(key, out SheetMedia? cached))
        {
            return cached;
        }

        SheetMedia media = QueryMedia(printerName, pageWidthMm, pageHeightMm);
        _mediaCache[key] = media;
        return media;
    }

    private static SheetMedia QueryMedia(string? printerName, double pageWidthMm, double pageHeightMm)
    {
        try
        {
            using var server = new LocalPrintServer();
            PrintQueue queue = printerName is null
                ? LocalPrintServer.GetDefaultPrintQueue()
                : server.GetPrintQueue(printerName);

            PrintCapabilities caps = queue.GetPrintCapabilities();
            PageImageableArea? area = caps.PageImageableArea;
            if (area is not null)
            {
                return new SheetMedia
                {
                    PageWidthMm = pageWidthMm,
                    PageHeightMm = pageHeightMm,
                    ImageableOriginXMm = UnitConverter.DiuToMm(area.OriginWidth),
                    ImageableOriginYMm = UnitConverter.DiuToMm(area.OriginHeight),
                    ImageableWidthMm = UnitConverter.DiuToMm(area.ExtentWidth),
                    ImageableHeightMm = UnitConverter.DiuToMm(area.ExtentHeight),
                };
            }
        }
        catch (Exception)
        {
            // fall through to full-bleed assumption
        }

        return SheetMedia.FullBleed(pageWidthMm, pageHeightMm);
    }

    public LabelSheetPaginator BuildPaginator(
        ProjectModel project, DataSet? data, PrintSettings settings, string? printerName)
    {
        LabelTemplate template = project.Template
            ?? throw new InvalidOperationException("No template selected.");
        PageSize page = template.EffectivePage;

        BulkRenderPlan plan = SheetLayoutEngine.CreatePlan(project, data, settings);
        var sheets = SheetLayoutEngine.PlanSheets(plan).ToList();

        SheetMedia media = GetMedia(printerName, page.WidthMm, page.HeightMm);
        PrinterCalibration calibration = printerName is null
            ? PrinterCalibration.Identity(string.Empty)
            : _calibration.Get(printerName);

        LabelOutlineStyle outline = settings.PrintCropMarks ? LabelOutlineStyle.Crop
            : settings.PrintLabelOutlines ? LabelOutlineStyle.Hairline
            : LabelOutlineStyle.None;

        return new LabelSheetPaginator(sheets, template, project.MasterLabel, _renderer, media, calibration,
            settings.ScalePercent, outline);
    }

    /// <summary>
    /// Builds a paginator that prints the project's standalone pages several-to-a-sheet
    /// (spec §15 "print multiple pages in one sheet"), instead of repeating the master label.
    /// </summary>
    public MultiPageSheetPaginator BuildPagesPaginator(ProjectModel project, PrintSettings settings, string? printerName)
    {
        LabelTemplate template = project.Template
            ?? throw new InvalidOperationException("No template selected.");
        PageSize page = template.EffectivePage;

        IReadOnlyList<Core.Model.LabelDocument> documents = settings.SelectedRecords.Count > 0
            ? settings.SelectedRecords
                .Where(i => i >= 0 && i < project.Pages.Count)
                .Select(i => project.Pages[i].Document)
                .ToList()
            : project.Pages.Select(p => p.Document).ToList();

        if (documents.Count == 0)
        {
            throw new InvalidOperationException("The project has no standalone pages to print. Add pages first (Page > Add page).");
        }

        var plan = new MultiDocumentRenderPlan
        {
            Template = template,
            Documents = documents,
            StartPosition = settings.StartPosition,
            SkipPositions = new HashSet<int>(settings.SkipPositions),
        };
        var sheets = MultiDocumentSheetLayout.PlanSheets(plan).ToList();

        SheetMedia media = GetMedia(printerName, page.WidthMm, page.HeightMm);
        PrinterCalibration calibration = printerName is null
            ? PrinterCalibration.Identity(string.Empty)
            : _calibration.Get(printerName);

        LabelOutlineStyle outline = settings.PrintCropMarks ? LabelOutlineStyle.Crop
            : settings.PrintLabelOutlines ? LabelOutlineStyle.Hairline
            : LabelOutlineStyle.None;

        return new MultiPageSheetPaginator(sheets, template, _renderer, media, calibration, settings.ScalePercent, outline);
    }

    /// <summary>
    /// Shows the Windows print dialog and returns the job ready to run, or null if
    /// the user cancelled. The job is run by <see cref="PrintJob.RunAsync"/> so the
    /// app stays responsive (and cancellable) while a long job spools.
    /// </summary>
    public PrintJobRequest? PrepareWithSystemDialog(ProjectModel project, DataSet? data, PrintSettings settings)
    {
        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        return Prepare(project, data, settings, dialog.PrintQueue, dialog.PrintTicket);
    }

    /// <summary>A job for an already-chosen printer, skipping the system dialog.</summary>
    public PrintJobRequest PrepareDirect(ProjectModel project, DataSet? data, PrintSettings settings, PrintQueue queue) =>
        Prepare(project, data, settings, queue, queue.DefaultPrintTicket);

    private PrintJobRequest Prepare(ProjectModel project, DataSet? data, PrintSettings settings, PrintQueue queue, PrintTicket ticket)
    {
        ticket.PageScalingFactor = 100;           // never "fit to page" (spec §16)
        ticket.PageOrientation = settings.Landscape ? PrintOrientation.Landscape : PrintOrientation.Portrait;
        if (settings.Copies > 1)
        {
            ticket.CopyCount = settings.Copies;
        }

        settings.PrinterName = queue.FullName;
        DocumentPaginator paginator = settings.Content == PrintContent.Pages
            ? BuildPagesPaginator(project, settings, queue.FullName)
            : BuildPaginator(project, data, settings, queue.FullName);

        return new PrintJobRequest(queue, ticket, paginator, $"Label Maker — {project.Metadata.Title}", Math.Max(1, settings.Copies));
    }
}
