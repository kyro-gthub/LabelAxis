using System.Collections.ObjectModel;
using System.IO;
using LabelMaker.App.Controls;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Printing;
using LabelMaker.App.Rendering;
using LabelMaker.App.Services;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Data;
using LabelMaker.Core.Data.Import;
using LabelMaker.Core.Diagnostics;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Preflight;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;
using LabelMaker.Export;
using LabelMaker.Export.Pdf;
using LabelMaker.Export.Svg;

namespace LabelMaker.App.ViewModels;

/// <summary>Printing, print preview, history, export and the checks before a print.</summary>
public sealed partial class MainViewModel
{
    // ---------- print / export ----------

    private void Print()
    {
        try
        {
            var settings = _session.Project.PrintSettings;
            if (!_activeTab.HasConfiguredPrintSettings)
            {
                settings.Content = _session.Data is { RowCount: > 0 } ? PrintContent.VariableData : PrintContent.SheetFill;
            }

            AimPrintAtActivePage();

            // A roll goes to the thermal dialog (printer commands, dot-exact preview);
            // it can still hand over to the Windows driver from there.
            if (_session.Project.Template is { IsRoll: true })
            {
                if (_shell.ShowThermalPrint())
                {
                    StatusText = "Sent to the label printer";
                    _env.SaveSettings();
                }

                return;
            }

            // The app's own print dialog, not the Windows one; it still offers the
            // driver dialog for duplex/tray options the printer alone knows about.
            if (_shell.ShowPrintDialog())
            {
                StatusText = "Sent to printer";
                _env.SaveSettings();
            }
        }
        catch (Exception ex) when (Faults.IsReportable(ex))
        {
            _shell.Warn("Printing failed:\n\n" + ex.Message);
        }
    }

    /// <summary>
    /// Printing from a numbered page prints that page: the Print dialog and the preview open
    /// on it, and the dialog can still switch to every page or a chosen few. From the master
    /// label, printing goes back to what the job was set to before.
    /// </summary>
    /// <summary>The help page about what is selected (F1 in the main window), or the first steps.</summary>
    public string HelpTopicForSelection() => _session.Selection.FirstOrDefault() switch
    {
        TextObject t when Bidi.HasRightToLeft(t.Content.PlainText) => "arabic",
        TextObject => "text",
        BarcodeObject => "barcodes",
        ImageObject => "images",
        ShapeObject => "shapes",
        TableObject => "designing",
        _ => HelpGuide.StartTopic,
    };

    /// <summary>Gives the objects on other pages the place, size and look they have on this page.</summary>
    private void ApplyLayoutToOtherPages()
    {
        if (_session.Project.Pages.Count == 0)
        {
            _shell.Info("This project has only one page. Add or duplicate a page first, then it can be given this page's format.");
            return;
        }

        if (_shell.ShowApplyLayout())
        {
            RefreshCanvas();
            StatusText = "Page format applied to other pages";
        }
    }

    /// <summary>Takes the selected objects, and their matches on other pages, off every page.</summary>
    private void RemoveFromAllPages()
    {
        if (_session.Selection.Count == 0)
        {
            return;
        }

        if (_session.Project.Pages.Count == 0)
        {
            DeleteSelection(); // one page: the same as Delete
            return;
        }

        int removed = _shell.ShowRemoveFromPages();
        if (removed > 0)
        {
            RebuildObjectList();
            RefreshCanvas();
            StatusText = $"Removed {removed} object(s) from the pages.";
        }
    }

    /// <summary>Gives the selected text box's formatting to text boxes on other pages.</summary>
    private void ApplyTextStyleToOtherPages()
    {
        if (_session.Selection.OfType<TextObject>().FirstOrDefault() is not { } source)
        {
            return;
        }

        if (_session.Project.Pages.Count == 0)
        {
            _shell.Info("This project has only one page. Add or duplicate a page first, then its text boxes can take this formatting.");
            return;
        }

        if (_shell.ShowApplyTextStyle(source))
        {
            RefreshCanvas();
            StatusText = "Formatting applied to other pages";
        }
    }

    private void AimPrintAtActivePage()
    {
        PrintSettings settings = _session.Project.PrintSettings;
        int position = _session.ActivePagePosition;
        settings.CurrentPage = position;

        if (position > 0)
        {
            settings.Content = PrintContent.Pages;
            settings.PagesToPrint = PagesToPrint.Current;
        }
        else if (settings.PagesToPrint == PagesToPrint.Current)
        {
            settings.PagesToPrint = PagesToPrint.All;
        }
    }

    private void ShowPrintHistory()
    {
        if (_shell.ShowPrintHistory() is { } reprint)
        {
            Reprint(reprint);
        }
    }

    /// <summary>
    /// Sends a job from the print history again: opens its project if another one is
    /// showing, restores the settings it printed with (and the sheet range to finish
    /// a stopped job), then shows the print dialog so the user confirms before paper moves.
    /// </summary>
    public bool Reprint(ReprintRequest request)
    {
        PrintHistoryEntry entry = request.Entry;

        if (TabForProject(entry.ProjectId) is { } openTab)
        {
            ActivateTab(openTab);
        }
        else
        {
            if (entry.ProjectPath is not { } path || !File.Exists(path))
            {
                _shell.Warn($"The project \"{entry.ProjectTitle}\" is not open and its file could not be found, so the job cannot be reprinted.");
                return false;
            }

            if (!_shell.Confirm($"This job was printed from \"{entry.ProjectTitle}\". Open that project in a new tab to reprint it?\n\n{path}"))
            {
                return false;
            }

            OpenProjectPath(path);
            if (_session.Project.Metadata.Id != entry.ProjectId)
            {
                return false;
            }
        }

        if (entry.Content == PrintContent.VariableData && entry.DataRecordCount != _session.RecordCount &&
            !_shell.Confirm($"When this job printed, {entry.DataRecordCount:N0} record(s) were loaded; now there are {_session.RecordCount:N0}. " +
                            "The same sheet numbers may hold different records.\n\nContinue to the print dialog?"))
        {
            return false;
        }

        _session.Project.PrintSettings.CopyFrom(request.BuildSettings());
        _activeTab.HasConfiguredPrintSettings = true;
        StatusText = request.RemainingOnly
            ? $"Ready to print sheets {entry.ResumeSheet:N0}–{entry.LastSheet:N0} of \"{entry.ProjectTitle}\""
            : $"Reprint of \"{entry.ProjectTitle}\" ready";

        try
        {
            if (_shell.ShowPrintDialog())
            {
                StatusText = "Sent to printer";
                _env.SaveSettings();
                return true;
            }
        }
        catch (Exception ex) when (Faults.IsReportable(ex))
        {
            _shell.Warn("Printing failed:\n\n" + ex.Message);
        }

        return false;
    }

    private void OpenPrintSettingsThenPrint()
    {
        if (_shell.ShowPrintSettings())
        {
            _activeTab.HasConfiguredPrintSettings = true;
            Print();
        }
    }

    private void ExportSheetPdf()
    {
        string? path = _shell.PickSaveFile("PDF|*.pdf", SafeName(_session.Project.Metadata.Title) + "-sheet", "Export sheet PDF");
        if (path is null)
        {
            return;
        }

        PrintLog? log = null;
        CountingProgress? counter = null;
        try
        {
            // The PDF follows the same print settings as a printed job (start slot,
            // skipped slots, record range, sheet range, outlines), so it is a faithful proof.
            PrintSettings settings = _session.Project.PrintSettings;
            if (settings.Content == PrintContent.VariableData && (_session.Data?.RowCount ?? 0) == 0)
            {
                settings.Content = PrintContent.SheetFill;
            }

            BulkRenderPlan plan = SheetLayoutEngine.CreatePlan(_session.Project, _session.Data, settings);
            var allSheets = SheetLayoutEngine.PlanSheets(plan).ToList();
            (IReadOnlyList<PlannedSheet> slice, int firstSheet) = settings.ApplySheetRange<PlannedSheet>(allSheets);
            PrintJobSummary summary = PrintJobSummary.FromSheets(allSheets, slice, firstSheet);
            int sheets = summary.Sheets;

            var request = new ExportRequest
            {
                Project = _session.Project,
                Data = plan.Data,
                Template = plan.Template,
                Scope = ExportScope.Sheet,
                Title = _session.Project.Metadata.Title,
                StartPosition = plan.StartPosition,
                SkipPositions = plan.SkipPositions,
                RecordSelection = plan.RecordSelection,
                FillSheetWhenNoData = plan.FillSheetWhenNoData,
                DrawLabelOutlines = settings.PrintLabelOutlines,
                DrawCropMarks = settings.PrintCropMarks,
                FirstSheet = settings.HasSheetRange ? firstSheet : null,
                LastSheet = settings.HasSheetRange ? firstSheet + Math.Max(0, sheets - 1) : null,
            };
            AssetStore assets = _session.Document.Assets;

            log = PrintLog.Start(_env.PrintHistory, _session, settings, summary, PrintHistoryKind.Pdf, null, path);

            // Rendering and writing run off the UI thread; the progress window is modal,
            // so the project cannot change underneath the export.
            _shell.RunJob($"Exporting {sheets:N0} sheet{(sheets == 1 ? "" : "s")} to PDF", "sheets rendered", sheets,
                (progress, token) => Task.Run(() =>
                {
                    byte[] pdf = new PdfExporter().Export(request, assets, counter = new CountingProgress(progress), token);
                    token.ThrowIfCancellationRequested();
                    File.WriteAllBytes(path, pdf);
                    return sheets;
                }, token));

            log.Completed();
            settings.FirstSheet = null;
            settings.LastSheet = null;
            StatusText = $"Exported {sheets:N0} sheet(s) to {Path.GetFileName(path)}";
        }
        catch (OperationCanceledException)
        {
            log?.Cancelled(counter?.Max ?? 0);
            StatusText = "PDF export cancelled";
        }
        catch (Exception ex) when (Faults.IsReportable(ex))
        {
            log?.Failed(counter?.Max ?? 0, ex);
            _shell.Warn("PDF export failed:\n\n" + ex.Message);
        }
    }

    private void ExportLabelPng()
    {
        string? path = _shell.PickSaveFile("PNG image|*.png", SafeName(_session.Project.Metadata.Title) + "-label", "Export label PNG");
        if (path is null || _session.Project.Template is not { } template)
        {
            return;
        }

        try
        {
            byte[] png = _imageExporter.RenderLabelToImage(
                _session.ActiveLabel, _session.CurrentResolveContext(), template, 300, RasterFormat.Png);
            File.WriteAllBytes(path, png);
            StatusText = $"Exported {Path.GetFileName(path)} (300 DPI)";
        }
        catch (Exception ex) when (Faults.IsReportable(ex))
        {
            _shell.Warn("PNG export failed:\n\n" + ex.Message);
        }
    }

    private void ExportLabelSvg()
    {
        string? path = _shell.PickSaveFile("SVG|*.svg", SafeName(_session.Project.Metadata.Title) + "-label", "Export label SVG");
        if (path is null)
        {
            return;
        }

        try
        {
            LabelShape shape = _session.Project.Template?.Grid.LabelShape ?? LabelShape.Rectangle;
            double radius = _session.Project.Template?.Grid.CornerRadiusMm ?? 0;
            string svg = new SvgExporter().ExportLabel(
                _session.ActiveLabel, _session.CurrentResolveContext(), _session.Document.Assets, shape, radius);
            File.WriteAllText(path, svg);
            StatusText = $"Exported {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (Faults.IsReportable(ex))
        {
            _shell.Warn("SVG export failed:\n\n" + ex.Message);
        }
    }
}
