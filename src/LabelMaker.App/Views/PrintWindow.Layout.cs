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

/// <summary>The print window's paper, margins, pages to print and printer list.</summary>
public partial class PrintWindow
{
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

    /// <summary>A page as the Pages panel names it: numbered from 1, the first page too.</summary>
    private string PageCaption(int position) => position == 0
        ? $"1.  {_session.Project.MasterLabel.Name}"
        : $"{position + 1}.  {_session.Project.Pages[position - 1].Name}";

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
}
