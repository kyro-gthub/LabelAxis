using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LabelMaker.App.Services;
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

    public CalibrationWizardWindow(AppEnvironment env, ProjectModel project)
    {
        InitializeComponent();
        _env = env;
        _project = project;

        var printServer = new Printing.WpfPrintServer(new Rendering.SheetVisualRenderer(new Rendering.WpfTextMeasurer(),
            new Rendering.WpfImageSource(new Core.Serialization.AssetStore())), env.Calibration);
        var printers = printServer.InstalledPrinters().ToList();
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

    private void OnPrintTarget(object sender, RoutedEventArgs e)
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        // The target must print on the same paper, at 100%, as the label sheets it calibrates.
        PageSize page = _project.Template?.EffectivePage ?? PageSize.A4;
        dlg.PrintTicket = Printing.WpfPrintServer.TicketFor(dlg.PrintQueue, dlg.PrintTicket, page.WidthMm, page.HeightMm, landscape: false);

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

        dlg.PrintVisual(visual, "LabelAxis calibration target");
        StatusText.Text = "Target printed. Measure the crosshair centre from the sheet's top-left and enter the difference below.";
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
        catch (Exception ex)
        {
            StatusText.Text = "Could not save: " + ex.Message;
        }
    }

    private static double Parse(string s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
}
