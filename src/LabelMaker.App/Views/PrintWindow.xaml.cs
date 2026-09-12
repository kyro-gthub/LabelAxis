using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using LabelMaker.App.Printing;
using LabelMaker.App.Services;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Printing;

namespace LabelMaker.App.Views;

/// <summary>
/// The app's own print dialog. Printing used to hand straight over to the Windows
/// dialog, which looks nothing like the rest of the product and hides the things
/// that actually matter for labels (which sheet, how many sheets, what scale).
/// The driver's own dialog is still one button away for duplex/tray options.
/// </summary>
public partial class PrintWindow : Window
{
    private readonly DocumentSession _session;
    private readonly WpfPrintServer _printServer;
    private readonly IShellServices _shell;
    private bool _loading = true;

    public PrintWindow(DocumentSession session, WpfPrintServer printServer, IShellServices shell)
    {
        _session = session;
        _printServer = printServer;
        _shell = shell;
        InitializeComponent();

        LabelTemplate? t = session.Project.Template;
        HeaderSubtitle.Text = t is null
            ? "No sheet template selected"
            : $"{t.Brand} {t.ProductCode} · {t.Grid.Columns} × {t.Grid.Rows} per {t.EffectivePage.Name} · " +
              $"{t.Grid.LabelWidthMm:0.#} × {t.Grid.LabelHeightMm:0.#} mm";

        LoadPrinters();

        PrintSettings s = session.Project.PrintSettings;
        CopiesBox.Text = Math.Max(1, s.Copies).ToString(CultureInfo.InvariantCulture);
        ScaleBox.Text = s.ScalePercent.ToString("0.#", CultureInfo.InvariantCulture);
        LandscapeBox.IsChecked = s.Landscape;

        _loading = false;
        Refresh();
    }

    private void LoadPrinters()
    {
        IReadOnlyList<string> printers = _printServer.InstalledPrinters();
        foreach (string p in printers)
        {
            PrinterBox.Items.Add(p);
        }

        string? wanted = _session.Project.PrintSettings.PrinterName ?? _printServer.DefaultPrinter();
        if (wanted is not null && printers.Contains(wanted))
        {
            PrinterBox.SelectedItem = wanted;
        }
        else if (PrinterBox.Items.Count > 0)
        {
            PrinterBox.SelectedIndex = 0;
        }

        if (PrinterBox.Items.Count == 0)
        {
            PrinterStatus.Text = "No printers are installed on this PC.";
            PrintButton.IsEnabled = false;
        }
    }

    private string? SelectedPrinter => PrinterBox.SelectedItem as string;

    // ---- live summary -----------------------------------------------------

    private void OnPrinterChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    private void OnFieldChanged(object sender, RoutedEventArgs e) => Refresh();

    private void OnFieldChanged(object sender, TextChangedEventArgs e) => Refresh();

    /// <summary>Writes the form back into the project's settings and recalculates the job.</summary>
    private void Apply()
    {
        PrintSettings s = _session.Project.PrintSettings;
        s.PrinterName = SelectedPrinter;
        s.Copies = Math.Clamp(ParseInt(CopiesBox.Text, 1), 1, 999);
        s.ScalePercent = Math.Clamp(ParseDouble(ScaleBox.Text, 100), 10, 400);
        s.Landscape = LandscapeBox.IsChecked == true;

        // Variable data with nothing imported prints nothing at all; fall back to
        // filling the sheet, which is what the preview does too.
        if (s.Content == PrintContent.VariableData && (_session.Data?.RowCount ?? 0) == 0)
        {
            s.Content = PrintContent.SheetFill;
        }
    }

    private void Refresh()
    {
        if (_loading)
        {
            return;
        }

        Apply();
        PrintSettings s = _session.Project.PrintSettings;

        ContentText.Text = s.Content switch
        {
            PrintContent.VariableData => $"One label per record — {_session.RecordCount:N0} record(s) loaded.",
            PrintContent.Pages => $"The project's {_session.Project.Pages.Count} standalone page(s), several to a sheet.",
            _ => "The master label repeated in every slot on the sheet.",
        };

        PositionText.Text = $"Starts at slot {s.StartPosition}" +
            (s.SkipPositions.Count > 0 ? $", skipping {string.Join(", ", s.SkipPositions)}" : string.Empty) +
            (s.PrintLabelOutlines ? " · label outlines on" : string.Empty) +
            (s.PrintCropMarks ? " · crop marks on" : string.Empty);

        try
        {
            DocumentPaginator paginator = s.Content == PrintContent.Pages
                ? _printServer.BuildPagesPaginator(_session.Project, s, SelectedPrinter)
                : _printServer.BuildPaginator(_session.Project, _session.Data, s, SelectedPrinter);

            int sheets = paginator.PageCount;
            int copies = Math.Max(1, s.Copies);
            SummaryText.Text = copies > 1
                ? $"{sheets} sheet(s) × {copies} copies = {sheets * copies} pages to {SelectedPrinter ?? "no printer"}."
                : $"{sheets} sheet(s) to {SelectedPrinter ?? "no printer"}.";
            PrintButtonText.Text = sheets * copies > 1 ? $"Print {sheets * copies} pages" : "Print";

            ShowThumbnail(paginator, sheets);
            PrintButton.IsEnabled = sheets > 0 && SelectedPrinter is not null;
        }
        catch (Exception ex)
        {
            SummaryText.Text = ex.Message;
            ThumbHost.Children.Clear();
            ThumbCaption.Text = string.Empty;
            PrintButton.IsEnabled = false;
        }

        PrinterStatus.Text = SelectedPrinter is null
            ? "No printer selected."
            : DescribeMedia(SelectedPrinter);
    }

    private string DescribeMedia(string printer)
    {
        PageSize page = _session.Project.Template?.EffectivePage ?? PageSize.A4;
        SheetMedia media = _printServer.GetMedia(printer, page.WidthMm, page.HeightMm);
        return $"Printable area {media.ImageableWidthMm:0.#} × {media.ImageableHeightMm:0.#} mm " +
               $"from ({media.ImageableOriginXMm:0.#}, {media.ImageableOriginYMm:0.#}) on {page.Name}.";
    }

    private void ShowThumbnail(DocumentPaginator paginator, int sheets)
    {
        ThumbHost.Children.Clear();
        if (sheets == 0)
        {
            ThumbCaption.Text = "Nothing to print with these settings.";
            return;
        }

        DocumentPage page = paginator.GetPage(0);
        double scale = Math.Min(230 / page.Size.Width, 330 / page.Size.Height);

        var paper = new Rectangle
        {
            Width = page.Size.Width * scale,
            Height = page.Size.Height * scale,
            Fill = new VisualBrush(page.Visual) { Stretch = Stretch.Fill },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        };

        var frame = new Border
        {
            BorderBrush = (Brush)FindResource("Br.BorderStrong"),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = paper,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 3,
                Direction = 270,
                Opacity = 0.22,
                Color = Color.FromRgb(0x22, 0x30, 0x3F),
            },
        };

        ThumbHost.Children.Add(frame);
        ThumbCaption.Text = sheets > 1
            ? $"Sheet 1 of {sheets}. Open Preview to page through the rest."
            : "The only sheet in this job.";
    }

    // ---- actions ----------------------------------------------------------

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
        try
        {
            if (_printServer.Print(_session.Project, _session.Data, _session.Project.PrintSettings))
            {
                DialogResult = true;
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        Apply();
        if (SelectedPrinter is not { } printer)
        {
            return;
        }

        try
        {
            // The queue must outlive the write, so the server is disposed only after it.
            using var server = new LocalPrintServer();
            PrintQueue queue = server.GetPrintQueue(printer);
            _printServer.PrintDirect(_session.Project, _session.Data, _session.Project.PrintSettings, queue);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private void Fail(Exception ex) =>
        MessageBox.Show(this, "Printing failed:\n\n" + ex.Message, "Label Maker",
            MessageBoxButton.OK, MessageBoxImage.Warning);

    private static int ParseInt(string s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    private static double ParseDouble(string s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
}
