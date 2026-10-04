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

    // ---- paper, margins and what to print ----------------------------------

    private void LoadLayout(PrintSettings s)
    {
        PrintLayout layout = s.Layout ??= new PrintLayout();
        LabelTemplate? t = _session.Project.Template;
        PaperBox.Items.Add(new PaperChoice(PrintLayout.TemplatePaper,
            t is null ? "Sheet template paper" : $"Sheet template paper ({t.Page.Name})"));
        foreach (PageSize p in PrintLayoutResolver.Papers)
        {
            PaperBox.Items.Add(new PaperChoice(p.Name, $"{p.Name} ({Mm(p.WidthMm)} × {Mm(p.HeightMm)} mm)"));
        }

        PaperBox.Items.Add(new PaperChoice(PrintLayout.CustomPaper, "Custom size…"));
        PaperBox.SelectedItem = PaperBox.Items.Cast<PaperChoice>().FirstOrDefault(c => string.Equals(c.Key, layout.PaperSize, StringComparison.OrdinalIgnoreCase))
                                ?? PaperBox.Items[0];

        PaperWidthBox.Text = Mm(layout.PaperWidthMm);
        PaperHeightBox.Text = Mm(layout.PaperHeightMm);
        (layout.CustomMargins ? CustomMarginsBox : TemplateMarginsBox).IsChecked = true;
        if (!layout.CustomMargins && t is not null)
        {
            // Start from the template's own spacing, so switching to custom margins changes nothing until edited.
            SheetMeasurements m = SheetMeasurements.From(t);
            layout.MarginTopMm = Math.Round(m.MarginTopMm, 2);
            layout.MarginLeftMm = Math.Round(m.MarginLeftMm, 2);
            layout.MarginRightMm = Math.Max(0, Math.Round(m.MarginRightMm, 2));
            layout.MarginBottomMm = Math.Max(0, Math.Round(m.MarginBottomMm, 2));
            layout.HGapMm = Math.Round(m.HGapMm, 2);
            layout.VGapMm = Math.Round(m.VGapMm, 2);
            layout.CenterOnPaper = false;
        }

        MarginTopBox.Text = Mm(layout.MarginTopMm);
        MarginBottomBox.Text = Mm(layout.MarginBottomMm);
        MarginLeftBox.Text = Mm(layout.MarginLeftMm);
        MarginRightBox.Text = Mm(layout.MarginRightMm);
        HGapBox.Text = Mm(layout.HGapMm);
        VGapBox.Text = Mm(layout.VGapMm);
        CenterBox.IsChecked = layout.CenterOnPaper;

        CopiesPerPageBox.Text = Math.Max(1, s.CopiesPerPage).ToString(CultureInfo.InvariantCulture);
        ContentRecordsBox.IsEnabled = (_session.Data?.RowCount ?? 0) > 0;
        (s.Content switch
        {
            PrintContent.Pages => ContentPagesBox,
            PrintContent.VariableData when ContentRecordsBox.IsEnabled => ContentRecordsBox,
            _ => ContentFillBox,
        }).IsChecked = true;

        LoadPageChoice(s);
    }

    /// <summary>All pages, the page that was on screen, or a ticked list of pages.</summary>
    private void LoadPageChoice(PrintSettings s)
    {
        ProjectModel project = _session.Project;
        int count = project.Pages.Count + 1;
        int current = Math.Clamp(s.CurrentPage, 0, count - 1);

        // Nothing ticked yet: start from the page on screen.
        var ticked = new HashSet<int>(s.ChosenPages.Where(i => i >= 0 && i < count));
        if (ticked.Count == 0)
        {
            ticked.Add(current);
        }

        for (int i = 0; i < count; i++)
        {
            LabelDocument page = i == 0 ? project.MasterLabel : project.Pages[i - 1].Document;
            var box = new CheckBox
            {
                Content = PageCaption(i),
                Tag = i,
                IsChecked = ticked.Contains(i),
                VerticalAlignment = VerticalAlignment.Center,
            };
            box.Click += (_, _) =>
            {
                PagesChosenBox.IsChecked = true;
                Refresh();
            };

            // How many of this page to print; remembered with the page.
            var copies = new TextBox
            {
                Style = (Style)FindResource("NumberBox"),
                Width = 52,
                Text = PrintablePages.CopiesOf(page, s).ToString(CultureInfo.InvariantCulture),
                ToolTip = "How many of this page to print, one after another",
            };
            copies.TextChanged += (_, _) => OnPageCopiesChanged(page, copies);

            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), LastChildFill = true };
            var unit = new TextBlock
            {
                Text = "copies",
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11.5,
                Foreground = (Brush)FindResource("Br.TextMuted"),
            };
            DockPanel.SetDock(unit, Dock.Right);
            DockPanel.SetDock(copies, Dock.Right);
            row.Children.Add(unit);
            row.Children.Add(copies);
            row.Children.Add(box);
            PageList.Children.Add(row);
            _pageRows.Add((box, copies, page));
        }

        PagesCurrentBox.Content = $"Current page ({PageCaption(current)})";
        (s.PagesToPrint switch
        {
            PagesToPrint.Current => PagesCurrentBox,
            PagesToPrint.Chosen => PagesChosenBox,
            _ => PagesAllBox,
        }).IsChecked = true;
    }

    /// <summary>A page as the Pages panel names it: ★ for the master label, then numbered pages.</summary>
    private string PageCaption(int position) => position == 0
        ? $"★  {_session.Project.MasterLabel.Name}"
        : $"{position}.  {_session.Project.Pages[position - 1].Name}";

    private readonly List<(CheckBox Box, TextBox Copies, LabelDocument Page)> _pageRows = new();
    private bool _syncingCopies;
    private bool _pageCopiesEdited;

    private IEnumerable<CheckBox> PageBoxes => _pageRows.Select(r => r.Box);

    /// <summary>A page's own copies box: the number is kept with that page.</summary>
    private void OnPageCopiesChanged(LabelDocument page, TextBox box)
    {
        if (_syncingCopies || _loading)
        {
            return;
        }

        if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1)
        {
            n = Math.Min(n, 1000);
            int everyPage = Math.Clamp(ParseInt(CopiesPerPageBox.Text, 1), 1, 1000);
            page.PrintCopies = n == everyPage ? null : n;
            _pageCopiesEdited = true;
            RefreshSoon();
        }
    }

    /// <summary>"Copies of every page" sets them all at once, replacing per-page numbers.</summary>
    private void OnCopiesPerPageChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (int.TryParse(CopiesPerPageBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1)
        {
            n = Math.Min(n, 1000);
            _syncingCopies = true;
            foreach ((_, TextBox copies, LabelDocument page) in _pageRows)
            {
                _pageCopiesEdited |= page.PrintCopies is not null;
                page.PrintCopies = null;
                copies.Text = n.ToString(CultureInfo.InvariantCulture);
            }

            _syncingCopies = false;
        }

        RefreshSoon();
    }

    private void OnTickAllPages(object sender, RoutedEventArgs e) => TickAllPages(true);

    private void OnUntickAllPages(object sender, RoutedEventArgs e) => TickAllPages(false);

    private void TickAllPages(bool ticked)
    {
        foreach (CheckBox box in PageBoxes)
        {
            box.IsChecked = ticked;
        }

        PagesChosenBox.IsChecked = true;
        Refresh();
    }

    private void ApplyLayout(PrintSettings s)
    {
        PrintLayout layout = s.Layout ??= new PrintLayout();
        layout.PaperSize = (PaperBox.SelectedItem as PaperChoice)?.Key ?? PrintLayout.TemplatePaper;
        layout.PaperWidthMm = Math.Clamp(ParseDouble(PaperWidthBox.Text, layout.PaperWidthMm), 10, 2000);
        layout.PaperHeightMm = Math.Clamp(ParseDouble(PaperHeightBox.Text, layout.PaperHeightMm), 10, 2000);
        layout.CustomMargins = CustomMarginsBox.IsChecked == true;
        layout.MarginTopMm = Math.Clamp(ParseDouble(MarginTopBox.Text, layout.MarginTopMm), 0, 500);
        layout.MarginBottomMm = Math.Clamp(ParseDouble(MarginBottomBox.Text, layout.MarginBottomMm), 0, 500);
        layout.MarginLeftMm = Math.Clamp(ParseDouble(MarginLeftBox.Text, layout.MarginLeftMm), 0, 500);
        layout.MarginRightMm = Math.Clamp(ParseDouble(MarginRightBox.Text, layout.MarginRightMm), 0, 500);
        layout.HGapMm = Math.Clamp(ParseDouble(HGapBox.Text, layout.HGapMm), 0, 500);
        layout.VGapMm = Math.Clamp(ParseDouble(VGapBox.Text, layout.VGapMm), 0, 500);
        layout.CenterOnPaper = CenterBox.IsChecked == true;

        CustomPaperRow.Visibility = layout.PaperSize == PrintLayout.CustomPaper ? Visibility.Visible : Visibility.Collapsed;
        MarginsGrid.IsEnabled = CenterBox.IsEnabled = layout.CustomMargins;

        s.CopiesPerPage = Math.Clamp(ParseInt(CopiesPerPageBox.Text, 1), 1, 1000);
        s.Content = ContentPagesBox.IsChecked == true ? PrintContent.Pages
            : ContentRecordsBox.IsChecked == true ? PrintContent.VariableData
            : PrintContent.SheetFill;
        CopiesPerPageRow.IsEnabled = PageChoicePanel.IsEnabled = s.Content == PrintContent.Pages;
        s.PagesToPrint = PagesCurrentBox.IsChecked == true ? PagesToPrint.Current
            : PagesChosenBox.IsChecked == true ? PagesToPrint.Chosen
            : PagesToPrint.All;
        s.ChosenPages = PageBoxes.Where(b => b.IsChecked == true).Select(b => (int)b.Tag).ToList();

        try
        {
            if (_session.Project.Template is not null)
            {
                LabelTemplate t = PrintLayoutResolver.Resolve(_session.Project, s);
                LayoutText.Text = $"{t.Grid.Columns} across × {t.Grid.Rows} down = {t.Grid.Count} per sheet on {t.EffectivePage.Name} " +
                                  $"({Mm(t.EffectivePage.WidthMm)} × {Mm(t.EffectivePage.HeightMm)} mm). " + SheetMeasurements.From(t).Describe() + ".";
            }
        }
        catch (InvalidOperationException ex)
        {
            LayoutText.Text = ex.Message;
        }
    }

    private string DescribePages(PrintSettings s)
    {
        IReadOnlyList<LabelDocument> docs = PrintablePages.For(_session.Project, s);
        int pages = docs.Distinct().Count();
        int labels = docs.Count;
        string copies = labels == pages ? string.Empty
            : docs.Distinct().Select(d => PrintablePages.CopiesOf(d, s)).Distinct().Count() == 1 ? $", {labels / Math.Max(1, pages)} copies of each"
            : $", {labels} labels in all (each page's own number of copies)";
        return s.PagesToPrint switch
        {
            PagesToPrint.Current => $"Just the current page, {PageCaption(Math.Clamp(s.CurrentPage, 0, _session.Project.Pages.Count)).Replace("  ", " ")}" +
                                    (labels > 1 ? $", {labels} copies placed one after another." : "."),
            PagesToPrint.Chosen when pages == 0 => "No pages ticked yet. Tick the pages to print.",
            PagesToPrint.Chosen => $"{pages} selected page(s){copies}, placed one after another on each sheet.",
            _ => $"{pages} page(s) from the Pages panel{copies}, placed one after another on each sheet.",
        };
    }

    private void OnPaperChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    private sealed record PaperChoice(string Key, string Text)
    {
        public override string ToString() => Text;
    }

    private static string Mm(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private void RefreshSoon()
    {
        _refreshDelay.Stop();
        _refreshDelay.Start();
    }

    private void LoadPrinters()
    {
        PrinterBox.Items.Clear();
        IReadOnlyList<string> printers = _printServer.InstalledPrinters();
        foreach (string p in printers)
        {
            PrinterBox.Items.Add(p);
        }

        // Wi-Fi printers added here print directly, without being installed in Windows.
        foreach (Core.Printing.Ipp.IppPrinterInfo info in _settings?.NetworkPrinters ?? new List<Core.Printing.Ipp.IppPrinterInfo>())
        {
            PrinterBox.Items.Add(new NetworkPrinterItem(info));
        }

        string? wanted = _session.Project.PrintSettings.PrinterName ?? _printServer.DefaultPrinter();
        if (PrinterBox.Items.OfType<NetworkPrinterItem>().FirstOrDefault(n => n.Name == wanted) is { } network)
        {
            PrinterBox.SelectedItem = network;
        }
        else if (wanted is not null && printers.Contains(wanted))
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

    private string? SelectedPrinter => PrinterBox.SelectedItem switch
    {
        string name => name,
        NetworkPrinterItem network => network.Name,
        _ => null,
    };

    private Core.Printing.Ipp.IppPrinterInfo? SelectedNetworkPrinter => (PrinterBox.SelectedItem as NetworkPrinterItem)?.Info;

    /// <summary>A Wi-Fi printer in the printer list.</summary>
    private sealed record NetworkPrinterItem(Core.Printing.Ipp.IppPrinterInfo Info)
    {
        public string Name => IppPrintJob.DisplayName(Info);

        public override string ToString() => Name;
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
        catch (Exception ex)
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
        catch (Exception ex)
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
            catch (Exception ex)
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
        catch (Exception ex)
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
