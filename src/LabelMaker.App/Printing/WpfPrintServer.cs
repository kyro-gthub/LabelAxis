using System.IO;
using System.Printing;
using PrintOrientation = System.Printing.PageOrientation;
using System.Windows.Controls;
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

    /// <summary>Physical media geometry for a printer, including its unprintable border.</summary>
    public SheetMedia GetMedia(string? printerName, double pageWidthMm, double pageHeightMm)
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

    /// <summary>Show the system print dialog and print. Returns false if the user cancelled.</summary>
    public bool Print(ProjectModel project, DataSet? data, PrintSettings settings)
    {
        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        PrintTicket ticket = dialog.PrintTicket;
        ticket.PageScalingFactor = 100;           // never "fit to page" (spec §16)
        ticket.PageOrientation = settings.Landscape ? PrintOrientation.Landscape : PrintOrientation.Portrait;
        if (settings.Copies > 1)
        {
            ticket.CopyCount = settings.Copies;
        }

        string printerName = dialog.PrintQueue.FullName;
        settings.PrinterName = printerName;

        LabelSheetPaginator paginator = BuildPaginator(project, data, settings, printerName);
        dialog.PrintDocument(paginator, $"Label Maker — {project.Metadata.Title}");
        return true;
    }
}
