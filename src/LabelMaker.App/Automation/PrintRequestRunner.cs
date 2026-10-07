using System.IO;
using System.Printing;
using System.Windows.Threading;
using LabelMaker.App.Printing;
using LabelMaker.App.Rendering;
using LabelMaker.App.Services;
using LabelMaker.Core.Automation;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Data;
using LabelMaker.Core.Designs;
using LabelMaker.Core.Model;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Printing.Thermal;
using LabelMaker.Core.Serialization;
using LabelMaker.Export;
using LabelMaker.Export.Pdf;

namespace LabelMaker.App.Automation;

/// <summary>
/// Carries out print requests from other programs: opens the design or project, fills one
/// label per record, and prints it on the printer asked for (a Windows printer, or the
/// design's thermal printer settings) — or makes a PDF of it. Runs one request at a time, on
/// the window thread (WPF printing needs it), and writes each job to Print history.
/// </summary>
public sealed class PrintRequestRunner : IPrintRequestHandler
{
    private readonly AppEnvironment _env;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    public PrintRequestRunner(AppEnvironment env, Dispatcher dispatcher)
    {
        _env = env;
        _dispatcher = dispatcher;
    }

    /// <summary>A request finished: where it came from, what it asked for, and how it went.</summary>
    public event EventHandler<AutomationLogEntry>? Finished;

    public string? DefaultDesign => _env.Settings.Automation.DefaultDesign;

    public IReadOnlyList<string> DesignNames()
    {
        try
        {
            return _env.MyDesigns.List().Select(d => d.Name).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public IReadOnlyList<string> PrinterNames() => Server().InstalledPrinters();

    public async Task<PrintRequestResult> RunAsync(PrintRequest request, string source, CancellationToken cancellation)
    {
        await _oneAtATime.WaitAsync(cancellation).ConfigureAwait(false);
        PrintRequestResult result;
        try
        {
            result = await _dispatcher.InvokeAsync(() => RunOnWindowThread(request, cancellation)).Task.Unwrap().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new PrintRequestResult(false, "The request could not be carried out: " + ex.Message, 0, request.Reference);
        }
        finally
        {
            _oneAtATime.Release();
        }

        Finished?.Invoke(this, new AutomationLogEntry(DateTime.Now, source, request.Describe(), result.Ok, result.Message));
        return result;
    }

    private WpfPrintServer Server() =>
        new(new SheetVisualRenderer(new WpfTextMeasurer(), new WpfImageSource(new AssetStore())), _env.Calibration);

    private async Task<PrintRequestResult> RunOnWindowThread(PrintRequest request, CancellationToken cancellation)
    {
        ProjectDocument document;
        try
        {
            document = Open(request);
        }
        catch (PrintRequestException ex)
        {
            return new PrintRequestResult(false, ex.Message, 0, request.Reference);
        }

        CustomFonts.InstallFrom(document, ViewModels.MainViewModel.IsInstalledInWindows);
        ProjectModel project = document.Project;
        DataSet data = request.ToDataSet();
        int labels = data.RowCount;
        PrintSettings settings = project.PrintSettings.Clone();
        settings.Content = PrintContent.VariableData;
        settings.Copies = 1;
        settings.StartPosition = request.StartPosition ?? 1;
        settings.SkipPositions.Clear();
        settings.SelectedPositions.Clear();
        settings.SelectedRecords.Clear();
        settings.FirstRecord = settings.LastRecord = null;
        settings.FirstSheet = settings.LastSheet = null;

        var session = new DocumentSession(document, new WpfImageSource(document.Assets));
        var renderer = new SheetVisualRenderer(new WpfTextMeasurer(), new WpfImageSource(document.Assets));
        string title = project.Metadata.Title;

        if (request.Output == PrintRequestOutput.Pdf)
        {
            byte[] pdf = await Task.Run(() => Pdf(project, data, settings, document.Assets), cancellation).ConfigureAwait(true);
            return new PrintRequestResult(true, $"Made a PDF of {labels} label{(labels == 1 ? "" : "s")} of “{title}”.", labels, request.Reference) { Pdf = pdf };
        }

        string? printer = FirstNonEmpty(request.Printer, settings.PrinterName, _env.Settings.Automation.DefaultPrinter);
        if (string.Equals(printer, "thermal", StringComparison.OrdinalIgnoreCase))
        {
            ThermalJobPlan plan = ThermalJobPlan.Build(project, data, settings, renderer);
            ThermalPrintSettings thermal = settings.Thermal.Clone();
            if (thermal.Destination == ThermalDestination.File)
            {
                return new PrintRequestResult(false, $"“{title}” is set to save thermal printer commands to a file. Choose a printer for it in Thermal print first.", 0, request.Reference);
            }

            PrintLog log = PrintLog.Start(_env.PrintHistory, session, settings, plan.Summary, PrintHistoryKind.Printer, "thermal", null);
            try
            {
                using IThermalSink sink = ThermalSinks.Open(thermal, null, $"LabelAxis — {title}");
                await plan.SendAsync(thermal, sink, 1, null, cancellation).ConfigureAwait(true);
                log.Completed();
                return new PrintRequestResult(true, $"Printed {labels} label{(labels == 1 ? "" : "s")} of “{title}” on {sink.Description}.", labels, request.Reference);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or TimeoutException)
            {
                log.Failed(0, ex);
                return new PrintRequestResult(false, "The thermal printer did not take the job: " + ex.Message, 0, request.Reference);
            }
        }

        WpfPrintServer server = Server();
        printer ??= server.DefaultPrinter();
        if (printer is null)
        {
            return new PrintRequestResult(false, "No printer was named and Windows has no default printer.", 0, request.Reference);
        }

        PrintQueue queue;
        try
        {
            using var local = new LocalPrintServer();
            queue = local.GetPrintQueue(printer);
        }
        catch (Exception ex) when (ex is PrintQueueException or PrintServerException or ArgumentException or InvalidOperationException)
        {
            string known = string.Join(", ", server.InstalledPrinters());
            return new PrintRequestResult(false, $"There is no printer called “{printer}”. The printers are: {known}.", 0, request.Reference);
        }

        PrintJobRequest job = server.PrepareDirect(project, data, settings, queue);
        PrintLog printLog = PrintLog.Start(_env.PrintHistory, session, settings, job.Summary, PrintHistoryKind.Printer, queue.FullName, null);
        try
        {
            PrintJobOutcome outcome = await PrintJob.RunAsync(job, null, cancellation).ConfigureAwait(true);
            if (outcome == PrintJobOutcome.Cancelled)
            {
                printLog.Cancelled(0);
                return new PrintRequestResult(false, "The job was cancelled.", 0, request.Reference);
            }

            printLog.Completed();
            return new PrintRequestResult(true,
                $"Printed {labels} label{(labels == 1 ? "" : "s")} of “{title}” on {queue.FullName} ({job.Sheets} sheet{(job.Sheets == 1 ? "" : "s")}).",
                labels, request.Reference);
        }
        catch (Exception ex) when (ex is PrintQueueException or PrintSystemException or InvalidOperationException or IOException)
        {
            printLog.Failed(0, ex);
            return new PrintRequestResult(false, "The printer did not take the job: " + ex.Message, 0, request.Reference);
        }
    }

    /// <summary>The project the request names: one of the user's designs, or a project file.</summary>
    private ProjectDocument Open(PrintRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Design))
        {
            LabelDesign design = _env.MyDesigns.Find(request.Design)
                ?? throw new PrintRequestException($"There is no design called “{request.Design}”. Your designs are: {string.Join(", ", DesignNames())}.");
            return MyDesigns.Open(design);
        }

        string path = request.Project!;
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            throw new PrintRequestException($"The project file “{path}” was not found (give its full path).");
        }

        try
        {
            return ProjectFile.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            throw new PrintRequestException($"The project file “{path}” could not be opened: {ex.Message}", ex);
        }
    }

    private static byte[] Pdf(ProjectModel project, DataSet data, PrintSettings settings, AssetStore assets)
    {
        BulkRenderPlan plan = SheetLayoutEngine.CreatePlan(project, data, settings);
        var export = new ExportRequest
        {
            Project = project,
            Data = plan.Data,
            Template = plan.Template,
            Scope = project.Template?.IsRoll == true ? ExportScope.IndividualLabels : ExportScope.Sheet,
            Title = project.Metadata.Title,
            StartPosition = plan.StartPosition,
            SkipPositions = plan.SkipPositions,
            RecordSelection = plan.RecordSelection,
            FillSheetWhenNoData = false,
        };
        return new PdfExporter().Export(export, assets);
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}

/// <summary>One line of the print request log.</summary>
public sealed record AutomationLogEntry(DateTime When, string Source, string What, bool Ok, string Message)
{
    public string Time => When.ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);

    // Shown in the interface language; the programs that sent the request get the English.
    public string ShownSource => Localization.Localizer.T(Source);

    public string ShownMessage => Localization.Localizer.T(Message);
}
