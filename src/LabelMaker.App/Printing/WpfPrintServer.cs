using System.IO;
using System.Printing;
using PrintOrientation = System.Printing.PageOrientation;
using System.Windows.Controls;
using System.Windows.Documents;
using LabelMaker.App.Rendering;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Data;
using LabelMaker.Core.Diagnostics;
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
        catch (Exception fault)
        {
            Faults.Survived(fault);
            return Array.Empty<string>();
        }
    }

    public string? DefaultPrinter()
    {
        try
        {
            return LocalPrintServer.GetDefaultPrintQueue()?.FullName;
        }
        catch (Exception fault)
        {
            Faults.Survived(fault);
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

    /// <summary>
    /// A ticket for <paramref name="queue"/> asking for paper of the sheet's size, at 100% and
    /// in the given orientation, validated by the driver. Without the paper size the driver
    /// uses its default paper (often Letter), and an A4 sheet can come out shifted, scaled
    /// or cropped.
    /// </summary>
    public static PrintTicket TicketFor(PrintQueue queue, PrintTicket? baseTicket, double pageWidthMm, double pageHeightMm, bool landscape)
    {
        PrintTicket ticket = (baseTicket ?? queue.DefaultPrintTicket).Clone();
        double shortMm = Math.Min(pageWidthMm, pageHeightMm);
        double longMm = Math.Max(pageWidthMm, pageHeightMm);
        PageMediaSizeName name =
            Near(shortMm, 210) && Near(longMm, 297) ? PageMediaSizeName.ISOA4 :
            Near(shortMm, 148) && Near(longMm, 210) ? PageMediaSizeName.ISOA5 :
            Near(shortMm, 105) && Near(longMm, 148) ? PageMediaSizeName.ISOA6 :
            Near(shortMm, 215.9) && Near(longMm, 279.4) ? PageMediaSizeName.NorthAmericaLetter :
            PageMediaSizeName.Unknown;
        ticket.PageMediaSize = name == PageMediaSizeName.Unknown
            ? new PageMediaSize(UnitConverter.MmToDiu(shortMm), UnitConverter.MmToDiu(longMm))
            : new PageMediaSize(name, UnitConverter.MmToDiu(shortMm), UnitConverter.MmToDiu(longMm));
        ticket.PageScalingFactor = 100;           // never "fit to page" (spec §16)
        ticket.PageOrientation = landscape || pageWidthMm > pageHeightMm + 0.01 ? PrintOrientation.Landscape : PrintOrientation.Portrait;

        try
        {
            return queue.MergeAndValidatePrintTicket(queue.DefaultPrintTicket, ticket).ValidatedPrintTicket;
        }
        catch (Exception fault)
        {
            Faults.Survived(fault);
            return ticket;               // some drivers refuse validation; the request still stands
        }

        static bool Near(double a, double b) => Math.Abs(a - b) < 1.0;
    }

    private static SheetMedia QueryMedia(string? printerName, double pageWidthMm, double pageHeightMm)
    {
        try
        {
            using var server = new LocalPrintServer();
            PrintQueue queue = printerName is null
                ? LocalPrintServer.GetDefaultPrintQueue()
                : server.GetPrintQueue(printerName);

            // Ask about this sheet's paper, not the driver's default paper.
            PrintCapabilities caps = queue.GetPrintCapabilities(TicketFor(queue, null, pageWidthMm, pageHeightMm, landscape: false));
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
        catch (Exception fault)
        {
            Faults.Survived(fault);
            // fall through to full-bleed assumption
        }

        return SheetMedia.FullBleed(pageWidthMm, pageHeightMm);
    }

    /// <summary>
    /// The mapping a print job uses for <paramref name="printerName"/> (its calibration and the
    /// scale setting), so on-screen measurements match where labels actually print.
    /// </summary>
    public PrintCoordinateMapper MapperFor(string? printerName, PrintSettings settings, PageSize page)
    {
        SheetMedia media = GetMedia(printerName, page.WidthMm, page.HeightMm);
        PrinterCalibration calibration = printerName is null
            ? PrinterCalibration.Identity(string.Empty)
            : _calibration.Get(printerName);
        return new PrintCoordinateMapper(media, calibration, settings.ScalePercent);
    }

    /// <summary>True when the printer has a saved calibration that moves or scales the print.</summary>
    public bool IsCalibrated(string? printerName) => printerName is not null && !_calibration.Get(printerName).IsIdentity;

    public LabelSheetPaginator BuildPaginator(
        ProjectModel project, DataSet? data, PrintSettings settings, string? printerName)
    {
        LabelTemplate template = PrintLayoutResolver.Resolve(project, settings);
        PageSize page = template.EffectivePage;

        BulkRenderPlan plan = SheetLayoutEngine.CreatePlan(project, data, settings, template);
        var all = SheetLayoutEngine.PlanSheets(plan).ToList();
        (IReadOnlyList<PlannedSheet> sheets, int firstSheet) = settings.ApplySheetRange<PlannedSheet>(all);

        SheetMedia media = GetMedia(printerName, page.WidthMm, page.HeightMm);
        PrinterCalibration calibration = printerName is null
            ? PrinterCalibration.Identity(string.Empty)
            : _calibration.Get(printerName);

        LabelOutlineStyle outline = settings.PrintCropMarks ? LabelOutlineStyle.Crop
            : settings.PrintLabelOutlines ? LabelOutlineStyle.Hairline
            : LabelOutlineStyle.None;

        return new LabelSheetPaginator(sheets, template, project.MasterLabel, _renderer, media, calibration,
            settings.ScalePercent, outline)
        {
            Summary = PrintJobSummary.FromSheets(all, sheets, firstSheet),
        };
    }

    /// <summary>
    /// Builds a paginator that prints the project's standalone pages several-to-a-sheet
    /// (spec §15 "print multiple pages in one sheet"), instead of repeating the master label.
    /// </summary>
    public MultiPageSheetPaginator BuildPagesPaginator(ProjectModel project, PrintSettings settings, string? printerName)
    {
        LabelTemplate template = PrintLayoutResolver.Resolve(project, settings);
        PageSize page = template.EffectivePage;

        IReadOnlyList<Core.Model.LabelDocument> documents = PrintablePages.For(project, settings);

        if (documents.Count == 0)
        {
            throw new InvalidOperationException("There are no pages to print. Put something on the master label or add pages in the Pages panel.");
        }

        var plan = new MultiDocumentRenderPlan
        {
            Template = template,
            Documents = documents,
            StartPosition = settings.StartPosition,
            SkipPositions = new HashSet<int>(settings.SkipPositions),
        };
        var all = MultiDocumentSheetLayout.PlanSheets(plan).ToList();
        (IReadOnlyList<PlannedMultiSheet> sheets, int firstSheet) = settings.ApplySheetRange<PlannedMultiSheet>(all);

        SheetMedia media = GetMedia(printerName, page.WidthMm, page.HeightMm);
        PrinterCalibration calibration = printerName is null
            ? PrinterCalibration.Identity(string.Empty)
            : _calibration.Get(printerName);

        LabelOutlineStyle outline = settings.PrintCropMarks ? LabelOutlineStyle.Crop
            : settings.PrintLabelOutlines ? LabelOutlineStyle.Hairline
            : LabelOutlineStyle.None;

        return new MultiPageSheetPaginator(sheets, template, _renderer, media, calibration, settings.ScalePercent, outline)
        {
            Summary = new PrintJobSummary
            {
                Sheets = sheets.Count,
                TotalSheets = all.Count,
                Labels = sheets.Sum(s => s.Documents.Count),
                FirstSheet = firstSheet,
            },
        };
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

    private PrintJobRequest Prepare(ProjectModel project, DataSet? data, PrintSettings settings, PrintQueue queue, PrintTicket baseTicket)
    {
        PageSize page = project.Template is null ? PageSize.A4 : PrintLayoutResolver.Resolve(project, settings).EffectivePage;
        PrintTicket ticket = TicketFor(queue, baseTicket, page.WidthMm, page.HeightMm, settings.Landscape);
        if (settings.Copies > 1)
        {
            ticket.CopyCount = settings.Copies;
        }

        settings.PrinterName = queue.FullName;
        (DocumentPaginator paginator, PrintJobSummary summary) = BuildJob(project, data, settings, queue.FullName);
        return new PrintJobRequest(queue, ticket, paginator, $"LabelAxis — {project.Metadata.Title}", Math.Max(1, settings.Copies), summary);
    }

    /// <summary>The paginator for whatever the settings print (records, sheet fill or standalone pages), with its summary.</summary>
    public (DocumentPaginator Paginator, PrintJobSummary Summary) BuildJob(
        ProjectModel project, DataSet? data, PrintSettings settings, string? printerName)
    {
        if (settings.Content == PrintContent.Pages)
        {
            MultiPageSheetPaginator pages = BuildPagesPaginator(project, settings, printerName);
            return (pages, pages.Summary);
        }

        LabelSheetPaginator labels = BuildPaginator(project, data, settings, printerName);
        return (labels, labels.Summary);
    }
}
