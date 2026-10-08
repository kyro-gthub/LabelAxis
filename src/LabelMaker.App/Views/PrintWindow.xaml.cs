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
/// The app's own print dialog. Printing used to hand straight over to the Windows
/// dialog, which looks nothing like the rest of the product and hides the things
/// that actually matter for labels (which sheet, how many sheets, what scale).
/// The driver's own dialog is still one button away for duplex/tray options.
/// The paper and page choices are in PrintWindow.Layout.cs, the buttons in PrintWindow.Send.cs.
/// </summary>
public partial class PrintWindow : Window
{
    private readonly DocumentSession _session;
    private readonly WpfPrintServer _printServer;
    private readonly IShellServices _shell;
    private readonly PrintHistoryStore _history;
    private bool _loading = true;

    // Typing in Copies or Scale rebuilds the job summary and thumbnail; wait until
    // the typing pauses instead of doing that on every keystroke.
    private readonly System.Windows.Threading.DispatcherTimer _refreshDelay =
        new() { Interval = TimeSpan.FromMilliseconds(300) };

    private readonly LabelMaker.Core.Settings.AppSettings? _settings;
    private readonly Action? _saveSettings;

    public PrintWindow(DocumentSession session, WpfPrintServer printServer, IShellServices shell, PrintHistoryStore history,
        LabelMaker.Core.Settings.AppSettings? settings = null, Action? saveSettings = null)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        _session = session;
        _printServer = printServer;
        _shell = shell;
        _history = history;
        InitializeComponent();

        LabelTemplate? t = session.Project.Template;
        HeaderSubtitle.Text = t is null
            ? "No sheet template selected"
            : $"{t.Brand} {t.ProductCode} on {t.EffectivePage.Name} · {SheetMeasurements.From(t).Describe()}";
        HeaderSubtitle.ToolTip = HeaderSubtitle.Text;

        LoadPrinters();

        PrintSettings s = session.Project.PrintSettings;
        CopiesBox.Text = Math.Max(1, s.Copies).ToString(CultureInfo.InvariantCulture);
        ScaleBox.Text = s.ScalePercent.ToString("0.#", CultureInfo.InvariantCulture);
        LandscapeBox.IsChecked = s.Landscape;
        LoadLayout(s);
        FirstSheetBox.Text = s.FirstSheet?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        LastSheetBox.Text = s.LastSheet?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        _refreshDelay.Tick += (_, _) => { _refreshDelay.Stop(); Refresh(); };
        Closed += (_, _) =>
        {
            _refreshDelay.Stop();

            // Copies set per page are kept with the pages, so the project needs saving.
            if (_pageCopiesEdited)
            {
                _session.MarkDirty();
            }
        };

        _loading = false;
        Refresh();
    }

    // ---- live summary -----------------------------------------------------

    private void OnPrinterChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    private void OnFieldChanged(object sender, RoutedEventArgs e) => Refresh();

    private void OnFieldChanged(object sender, TextChangedEventArgs e) => RefreshSoon();

    /// <summary>Writes the form back into the project's settings and recalculates the job.</summary>
    private void Apply()
    {
        PrintSettings s = _session.Project.PrintSettings;
        s.PrinterName = SelectedPrinter;
        s.Copies = Math.Clamp(ParseInt(CopiesBox.Text, 1), 1, 999);
        s.ScalePercent = Math.Clamp(ParseDouble(ScaleBox.Text, 100), 10, 400);
        s.Landscape = LandscapeBox.IsChecked == true;
        s.FirstSheet = ParseSheet(FirstSheetBox.Text);
        s.LastSheet = ParseSheet(LastSheetBox.Text);
        ApplyLayout(s);

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
            PrintContent.VariableData when s.SelectedRecords.Count > 0 =>
                $"One label for each of {s.SelectedRecords.Count:N0} chosen record(s), out of {_session.RecordCount:N0}.",
            PrintContent.VariableData when s.FirstRecord is not null || s.LastRecord is not null =>
                $"One label per record, records {(s.FirstRecord ?? 0) + 1:N0}–{(s.LastRecord ?? _session.RecordCount - 1) + 1:N0} of {_session.RecordCount:N0}.",
            PrintContent.VariableData => $"One label per record — {_session.RecordCount:N0} record(s) loaded.",
            PrintContent.Pages => DescribePages(s),
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
            PrintJobSummary summary = paginator switch
            {
                LabelSheetPaginator l => l.Summary,
                MultiPageSheetPaginator m => m.Summary,
                _ => new PrintJobSummary { Sheets = sheets, TotalSheets = sheets },
            };
            SheetRangeHint.Text = s.HasSheetRange ? $"of {summary.TotalSheets:N0}" : $"all {summary.TotalSheets:N0}";
            SummaryText.Text = copies > 1
                ? $"{sheets} sheet(s) × {copies} copies = {sheets * copies} pages to {SelectedPrinter ?? "no printer"}."
                : $"{sheets} sheet(s) to {SelectedPrinter ?? "no printer"}.";
            PrintButtonText.Text = sheets * copies > 1 ? $"Print {sheets * copies} pages" : "Print";

            ShowThumbnail(paginator, summary);
            PrintButton.IsEnabled = sheets > 0 && SelectedPrinter is not null;
        }
        catch (Exception ex) when (Faults.IsReportable(ex))
        {
            SummaryText.Text = ex.Message;
            ThumbHost.Children.Clear();
            ThumbCaption.Text = string.Empty;
            PrintButton.IsEnabled = false;
        }

        PrinterStatus.Text = SelectedNetworkPrinter is { } wifi
            ? $"On the network at {WifiPrinterFinder.HostOf(wifi)}: {wifi.Describe()}, no driver needed." +
              (_printServer.IsCalibrated(IppPrintJob.DisplayName(wifi)) ? " Calibrated." : " Not calibrated yet (Calibration wizard).")
            : SelectedPrinter is null
                ? "No printer selected."
                : DescribeMedia(SelectedPrinter);
    }

    private string DescribeMedia(string printer)
    {
        PageSize page = _session.Project.Template is null ? PageSize.A4 : PrintLayoutResolver.Resolve(_session.Project, _session.Project.PrintSettings).EffectivePage;
        SheetMedia media = _printServer.GetMedia(printer, page.WidthMm, page.HeightMm);
        return $"Printable area {media.ImageableWidthMm:0.#} × {media.ImageableHeightMm:0.#} mm " +
               $"from ({media.ImageableOriginXMm:0.#}, {media.ImageableOriginYMm:0.#}) on {page.Name}.";
    }

    private void ShowThumbnail(DocumentPaginator paginator, PrintJobSummary summary)
    {
        int sheets = summary.Sheets;
        ThumbHost.Children.Clear();
        if (sheets == 0)
        {
            ThumbCaption.Text = "Nothing to print with these settings.";
            return;
        }

        DocumentPage page = paginator.GetPage(0);
        _thumbPageSize = page.Size;
        var paper = new Rectangle
        {
            Fill = PrintPreviewWindow.SheetBrush(page),
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

        _thumbPaper = paper;
        FitThumbnail();
        ThumbHost.Children.Add(frame);
        ThumbCaption.Text = summary.TotalSheets != sheets
            ? $"Sheet {summary.FirstSheet:N0} of {summary.TotalSheets:N0}; this job prints {sheets:N0} of them."
            : sheets > 1
                ? $"Sheet 1 of {sheets:N0}. Open Preview to page through the rest."
                : "The only sheet in this job.";
    }

    private Rectangle? _thumbPaper;
    private Size _thumbPageSize;

    private void OnThumbHostSizeChanged(object sender, SizeChangedEventArgs e) => FitThumbnail();

    /// <summary>The sheet picture as big as the space beside the form allows, keeping the paper's shape.</summary>
    private void FitThumbnail()
    {
        if (_thumbPaper is null || _thumbPageSize.Width <= 0 || _thumbPageSize.Height <= 0)
        {
            return;
        }

        // Before the first layout the host has no size yet; start from the old fixed size.
        double w = ThumbHost.ActualWidth > 20 ? ThumbHost.ActualWidth - 4 : 230;
        double h = ThumbHost.ActualHeight > 20 ? ThumbHost.ActualHeight - 4 : 330;
        double scale = Math.Min(w / _thumbPageSize.Width, h / _thumbPageSize.Height);
        _thumbPaper.Width = Math.Max(10, _thumbPageSize.Width * scale);
        _thumbPaper.Height = Math.Max(10, _thumbPageSize.Height * scale);
    }
}
