using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using LabelMaker.App.Printing;
using LabelMaker.App.Services;
using LabelMaker.Core.Diagnostics;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Printing;

namespace LabelMaker.App.Views;

/// <summary>
/// The print window's buttons: preview, calibration, the Windows dialog, and printing to
/// Windows and Wi-Fi printers.
/// </summary>
public partial class PrintWindow
{
    private void OnAdvanced(object sender, RoutedEventArgs e)
    {
        Apply();
        _shell.ShowPrintSettings();
        Refresh();
    }

    private void OnPreview(object sender, RoutedEventArgs e)
    {
        Apply();
        _shell.ShowPrintPreview();
        Refresh();
    }

    private void OnCalibration(object sender, RoutedEventArgs e)
    {
        _shell.ShowCalibrationWizard();
        Refresh();
    }

    private void OnSystemDialog(object sender, RoutedEventArgs e)
    {
        Apply();
        if (SelectedNetworkPrinter is not null)
        {
            MessageBox.Show(this, "The Windows dialog is for printers installed in Windows. A Wi-Fi printer added here prints with the Print button.",
                "LabelAxis", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (_printServer.PrepareWithSystemDialog(_session.Project, _session.Data, _session.Project.PrintSettings) is { } job)
            {
                Send(job);
            }
        }
        catch (Exception ex) when (Faults.IsReportable(ex))
        {
            Fail(ex);
        }
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        Apply();
        if (SelectedNetworkPrinter is { } wifi)
        {
            try
            {
                SendToNetwork(wifi);
            }
            catch (Exception ex) when (Faults.IsReportable(ex))
            {
                Fail(ex);
            }

            return;
        }

        if (SelectedPrinter is not { } printer)
        {
            return;
        }

        try
        {
            // The queue must outlive the write, so the server is disposed only after it.
            using var server = new LocalPrintServer();
            PrintQueue queue = server.GetPrintQueue(printer);
            Send(_printServer.PrepareDirect(_session.Project, _session.Data, _session.Project.PrintSettings, queue));
        }
        catch (Exception ex) when (Faults.IsReportable(ex))
        {
            Fail(ex);
        }
    }

    /// <summary>
    /// Prints on a Wi-Fi printer without a driver: the same sheets the preview shows, drawn at
    /// the printer's resolution and sent over IPP, with progress and a print history entry.
    /// </summary>
    private void SendToNetwork(Core.Printing.Ipp.IppPrinterInfo printer)
    {
        PrintSettings settings = _session.Project.PrintSettings;
        string name = IppPrintJob.DisplayName(printer);
        DocumentPaginator paginator = settings.Content == PrintContent.Pages
            ? _printServer.BuildPagesPaginator(_session.Project, settings, name)
            : _printServer.BuildPaginator(_session.Project, _session.Data, settings, name);
        PrintJobSummary summary = paginator switch
        {
            LabelSheetPaginator l => l.Summary,
            MultiPageSheetPaginator m => m.Summary,
            _ => new PrintJobSummary { Sheets = paginator.PageCount, TotalSheets = paginator.PageCount },
        };

        int sheets = paginator.PageCount;
        PrintLog log = PrintLog.Start(_history, _session, settings, summary, PrintHistoryKind.Printer, name, null);
        int drawn = 0;
        try
        {
            string heading = settings.Copies > 1 ? $"Printing {sheets:N0} sheet(s) × {settings.Copies} on {name}" : $"Printing {sheets:N0} sheet(s) on {name}";
            JobProgressWindow.Run(this, heading, "sheets", sheets, async (progress, token) =>
            {
                var counting = new Progress<int>(n =>
                {
                    drawn = n;
                    ((IProgress<int>)progress).Report(n);
                });
                await IppPrintJob.SendAsync(printer, paginator, Math.Max(1, settings.Copies), $"LabelAxis — {_session.Project.Metadata.Title}", counting, token);
                return true;
            });

            log.Completed();
            settings.FirstSheet = null;
            settings.LastSheet = null;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            log.Cancelled(0);
            SummaryText.Text = "Cancelled before the job reached the printer.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Net.Http.HttpRequestException or System.IO.InvalidDataException or InvalidOperationException)
        {
            log.Failed(0, ex);
            MessageBox.Show(this, $"Printing on {name} failed after drawing {drawn} of {sheets} sheet(s):\n\n{ex.Message}\n\n" +
                                  "Check the printer is on and on the same network, or add it in Windows instead.",
                "LabelAxis", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnNetworkPrinters(object sender, RoutedEventArgs e)
    {
        if (_settings is null)
        {
            return;
        }

        var win = new NetworkPrintersWindow(_settings, _saveSettings ?? (() => { })) { Owner = this };
        win.ShowDialog();
        if (win.Changed)
        {
            string? keep = SelectedPrinter;
            LoadPrinters();
            if (PrinterBox.Items.OfType<object>().FirstOrDefault(i => i.ToString() == keep) is { } again)
            {
                PrinterBox.SelectedItem = again;
            }

            // Newly added: pick it, which is almost always why it was added.
            if (_settings.NetworkPrinters.LastOrDefault() is { } latest &&
                PrinterBox.Items.OfType<NetworkPrinterItem>().FirstOrDefault(n => n.Info.Uri == latest.Uri) is { } item &&
                keep != item.Name)
            {
                PrinterBox.SelectedItem = item;
            }

            Refresh();
        }
    }

    /// <summary>
    /// Spools with a progress window and records the job in the print history; the
    /// dialog closes only if the job was not cancelled.
    /// </summary>
    private void Send(PrintJobRequest job)
    {
        PrintSettings settings = _session.Project.PrintSettings;
        PrintLog log = PrintLog.Start(_history, _session, settings, job.Summary, PrintHistoryKind.Printer, job.Queue.FullName, null);

        int sheetsDone = 0;
        try
        {
            if (JobProgressWindow.RunPrint(this, job, out sheetsDone) == PrintJobOutcome.Completed)
            {
                log.Completed();

                // A sheet range is for one job only.
                settings.FirstSheet = null;
                settings.LastSheet = null;
                DialogResult = true;
            }
            else
            {
                log.Cancelled(sheetsDone);
                SummaryText.Text = $"Cancelled after {sheetsDone:N0} of {job.Sheets:N0} sheet(s). " +
                                   "Print history can finish the rest.";
            }
        }
        catch (Exception ex)
        {
            log.Failed(sheetsDone, ex);
            throw;
        }
    }

    /// <summary>Set when the user picked a job to reprint from the history; the caller runs it after this dialog closes.</summary>
    public ReprintRequest? Reprint { get; private set; }

    private void OnHistory(object sender, RoutedEventArgs e)
    {
        Apply();
        if (_shell.ShowPrintHistory() is { } reprint)
        {
            Reprint = reprint;
            DialogResult = false;
        }
    }

    private static int? ParseSheet(string s) =>
        int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v > 0 ? v : null;

    private void Fail(Exception ex) =>
        MessageBox.Show(this, "Printing failed:\n\n" + ex.Message, "LabelAxis",
            MessageBoxButton.OK, MessageBoxImage.Warning);

    private static int ParseInt(string s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    private static double ParseDouble(string s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
}
