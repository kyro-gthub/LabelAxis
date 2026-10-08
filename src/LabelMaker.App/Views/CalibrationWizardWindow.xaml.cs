using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LabelMaker.App.Services;
using LabelMaker.Core.Diagnostics;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Views;

public partial class CalibrationWizardWindow : Window
{
    private const double K = UnitConverter.DiuPerInch / UnitConverter.MmPerInch;

    private readonly AppEnvironment _env;
    private readonly ProjectModel _project;
    private readonly Dictionary<string, Core.Printing.Ipp.IppPrinterInfo> _network;

    public CalibrationWizardWindow(AppEnvironment env, ProjectModel project)
    {
        InitializeComponent();
        _env = env;
        _project = project;

        var printServer = new Printing.WpfPrintServer(new Rendering.SheetVisualRenderer(new Rendering.WpfTextMeasurer(),
            new Rendering.WpfImageSource(new Core.Serialization.AssetStore())), env.Calibration);
        // Wi-Fi printers added in the Print window are calibrated here too, under the name they
        // print with, so their jobs pick the correction up like any other printer's.
        _network = env.Settings.NetworkPrinters.GroupBy(Printing.IppPrintJob.DisplayName).ToDictionary(g => g.Key, g => g.First());
        var printers = printServer.InstalledPrinters().Concat(_network.Keys).ToList();
        PrinterBox.ItemsSource = printers;
        PrinterBox.SelectedItem = printServer.DefaultPrinter() ?? printers.FirstOrDefault();
        PrinterBox.SelectionChanged += (_, _) => LoadForSelectedPrinter();
        LoadForSelectedPrinter();
    }

    private void LoadForSelectedPrinter()
    {
        if (PrinterBox.SelectedItem is not string printer)
        {
            return;
        }

        PrinterCalibration c = _env.Calibration.Get(printer);
        OffsetXBox.Text = c.OffsetXMm.ToString("0.###", CultureInfo.InvariantCulture);
        OffsetYBox.Text = c.OffsetYMm.ToString("0.###", CultureInfo.InvariantCulture);
        ScaleXBox.Text = c.ScaleX.ToString("0.0000", CultureInfo.InvariantCulture);
        ScaleYBox.Text = c.ScaleY.ToString("0.0000", CultureInfo.InvariantCulture);
        StatusText.Text = c.IsIdentity ? "No calibration stored for this printer yet." : $"Last updated {c.UpdatedUtc.ToLocalTime():g}.";
    }

    /// <summary>Prints the target on the printer chosen above, on the paper the label sheets use, at 100%.</summary>
    private async void OnPrintTarget(object sender, RoutedEventArgs e)
    {
        if (PrinterBox.SelectedItem is not string printer)
        {
            StatusText.Text = "Select a printer first.";
            return;
        }

        PageSize page = _project.Template?.EffectivePage ?? PageSize.A4;
        DrawingVisual visual = Target();
        try
        {
            if (_network.TryGetValue(printer, out Core.Printing.Ipp.IppPrinterInfo? wifi))
            {
                StatusText.Text = $"Sending the target to {printer}…";
                var paginator = new Printing.SinglePagePaginator(visual, new Size(page.WidthMm * K, page.HeightMm * K));
                await Printing.IppPrintJob.SendAsync(wifi, paginator, 1, "LabelAxis calibration target", new Progress<int>(), CancellationToken.None);
            }
            else
            {
                using var server = new System.Printing.LocalPrintServer();
                System.Printing.PrintQueue queue = server.GetPrintQueue(printer);
                var dlg = new PrintDialog { PrintQueue = queue };
                dlg.PrintTicket = Printing.WpfPrintServer.TicketFor(queue, null, page.WidthMm, page.HeightMm, landscape: false);
                dlg.PrintVisual(visual, "LabelAxis calibration target");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Printing.PrintQueueException or System.Net.Http.HttpRequestException)
        {
            StatusText.Text = "Could not print the target: " + ex.Message;
            return;
        }

        StatusText.Text = "Target printed. Measure the crosshair centre from the sheet's top-left and enter the difference below.";
    }

    /// <summary>A crosshair 20 mm in from the sheet's top-left corner, with millimetre ticks.</summary>
    private static DrawingVisual Target()
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var pen = new Pen(Brushes.Black, 0.3 * K);
            double t = 20 * K; // 20 mm crosshair centre
            dc.DrawLine(pen, new Point(t - (10 * K), t), new Point(t + (10 * K), t));
            dc.DrawLine(pen, new Point(t, t - (10 * K)), new Point(t, t + (10 * K)));
            dc.DrawEllipse(null, pen, new Point(t, t), 3 * K, 3 * K);

            var label = new FormattedText("LabelAxis calibration — crosshair centre = 20.0 mm from sheet top-left",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 3.5 * K, Brushes.Black, 1.0);
            dc.DrawText(label, new Point(20 * K, 40 * K));

            // ruler ticks along X and Y from the crosshair
            for (int mm = 0; mm <= 40; mm += 5)
            {
                double len = mm % 10 == 0 ? 3 * K : 1.5 * K;
                dc.DrawLine(new Pen(Brushes.Black, 0.2 * K), new Point((20 + mm) * K, t), new Point((20 + mm) * K, t - len));
                dc.DrawLine(new Pen(Brushes.Black, 0.2 * K), new Point(t, (20 + mm) * K), new Point(t - len, (20 + mm) * K));
            }
        }

        return visual;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (PrinterBox.SelectedItem is not string printer)
        {
            StatusText.Text = "Select a printer first.";
            return;
        }

        var calibration = new PrinterCalibration
        {
            PrinterName = printer,
            OffsetXMm = Parse(OffsetXBox.Text, 0),
            OffsetYMm = Parse(OffsetYBox.Text, 0),
            ScaleX = Parse(ScaleXBox.Text, 1),
            ScaleY = Parse(ScaleYBox.Text, 1),
        };

        _env.Calibration.Set(calibration);
        try
        {
            _env.Calibration.Save(_env.Paths.CalibrationFilePath);
            StatusText.Text = $"Saved calibration for {printer}.";
        }
        catch (Exception ex) when (Faults.IsReportable(ex))
        {
            StatusText.Text = "Could not save: " + ex.Message;
        }
    }

    private static double Parse(string s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
}
