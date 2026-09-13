using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LabelMaker.App.Printing;
using LabelMaker.App.Services;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Views;

/// <summary>
/// Full-sheet print preview. Pages come from the very same
/// <see cref="DocumentPaginator"/> the printer is handed, so what is on screen is
/// what comes out of the tray — including calibration offsets, scale percentage,
/// label outlines and the start-position / skip mask.
/// </summary>
public partial class PrintPreviewWindow : Window
{
    private readonly DocumentSession _session;
    private readonly WpfPrintServer _printServer;
    private readonly IShellServices _shell;

    private DocumentPaginator? _paginator;
    private int _count;
    private int _index;
    private double _zoom = 1.0;
    private bool _fitMode = true;
    private bool _building;

    // Sheets are rendered only when shown (the page itself, or a thumbnail scrolled
    // into view), and only the most recent few are kept. Building every sheet up
    // front made a 715-sheet job take ten seconds to open.
    private const int CachedPages = 12;
    private readonly Dictionary<int, DocumentPage> _pageCache = new();
    private readonly LinkedList<int> _pageOrder = new();

    /// <summary>A sheet from the paginator, rendered on first use and kept in a small recent cache.</summary>
    private DocumentPage Page(int index)
    {
        if (_pageCache.TryGetValue(index, out DocumentPage? cached))
        {
            _pageOrder.Remove(index);
            _pageOrder.AddFirst(index);
            return cached;
        }

        DocumentPage page = _paginator!.GetPage(index);
        _pageCache[index] = page;
        _pageOrder.AddFirst(index);
        while (_pageOrder.Count > CachedPages)
        {
            _pageCache.Remove(_pageOrder.Last!.Value);
            _pageOrder.RemoveLast();
        }

        return page;
    }

    public PrintPreviewWindow(DocumentSession session, WpfPrintServer printServer, IShellServices shell)
    {
        _session = session;
        _printServer = printServer;
        _shell = shell;
        InitializeComponent();

        ShowOutlines.IsChecked = session.Project.PrintSettings.PrintLabelOutlines;
        Loaded += (_, _) => Build();
        SizeChanged += (_, _) => { if (_fitMode) { ApplyZoom(); } };
    }

    // ---- building ---------------------------------------------------------

    private void Build()
    {
        if (_building)
        {
            return;
        }

        _building = true;
        try
        {
            PrintSettings settings = _session.Project.PrintSettings;
            settings.PrintLabelOutlines = ShowOutlines.IsChecked == true;

            // Variable-data mode with nothing imported produces zero sheets, which
            // would show an empty preview for a document that prints perfectly well.
            if (settings.Content == PrintContent.VariableData && (_session.Data?.RowCount ?? 0) == 0)
            {
                settings.Content = PrintContent.SheetFill;
            }

            string? printer = settings.PrinterName ?? _printServer.DefaultPrinter();

            _paginator = settings.Content == PrintContent.Pages
                ? _printServer.BuildPagesPaginator(_session.Project, settings, printer)
                : _printServer.BuildPaginator(_session.Project, _session.Data, settings, printer);

            _pageCache.Clear();
            _pageOrder.Clear();
            _count = _paginator.PageCount;

            _index = Math.Clamp(_index, 0, Math.Max(0, _count - 1));
            BuildThumbnails();
            ShowPage();

            LabelTemplate? t = _session.Project.Template;
            LeftStatus.Text = t is null
                ? "No sheet template selected."
                : $"{t.Brand} {t.ProductCode} · {t.Grid.Columns} × {t.Grid.Rows} per sheet · " +
                  $"{t.Grid.LabelWidthMm:0.#} × {t.Grid.LabelHeightMm:0.#} mm on {t.EffectivePage.Name}";

            RightStatus.Text = $"{printer ?? "No printer"} · {DescribeContent(settings)} · {settings.ScalePercent:0.#}% scale";
        }
        catch (Exception ex)
        {
            _count = 0;
            _pageCache.Clear();
            _pageOrder.Clear();
            Thumbs.ItemsSource = null;
            PageHost.Children.Clear();
            LeftStatus.Text = "Preview unavailable";
            RightStatus.Text = ex.Message;
            PageText.Text = "— / —";
        }
        finally
        {
            _building = false;
        }
    }

    private static string DescribeContent(PrintSettings settings) => settings.Content switch
    {
        PrintContent.VariableData => "One label per record",
        PrintContent.Pages => "Project pages",
        _ => "Sheet fill",
    };

    private void BuildThumbnails()
    {
        Thumbs.SelectionChanged -= OnThumbSelected;

        // The list is virtualised, so a thumbnail's sheet is only rendered when its
        // row is scrolled into view — a 700-sheet job opens as fast as a 1-sheet one.
        double aspect = _paginator is { } p && p.PageSize.Width > 0 ? p.PageSize.Height / p.PageSize.Width : 1.414;
        Thumbs.ItemsSource = Enumerable.Range(0, _count).Select(i => new SheetThumb(i, aspect, Page)).ToList();
        Thumbs.SelectedIndex = _count > 0 ? _index : -1;

        Thumbs.SelectionChanged += OnThumbSelected;
    }

    /// <summary>One row in the sheet list. <see cref="Preview"/> renders its sheet the first time the row is shown.</summary>
    private sealed class SheetThumb
    {
        private readonly Func<int, DocumentPage> _page;
        private Brush? _preview;

        public SheetThumb(int index, double aspect, Func<int, DocumentPage> page)
        {
            Index = index;
            _page = page;
            ThumbHeight = 132 * aspect;
        }

        public int Index { get; }

        public string Label => $"Sheet {Index + 1}";

        public double ThumbHeight { get; }

        public Brush Preview => _preview ??= new VisualBrush(_page(Index).Visual) { Stretch = Stretch.Uniform };
    }
    private void ShowPage()
    {
        PageHost.Children.Clear();
        if (_count == 0)
        {
            PageText.Text = "— / —";
            PageHost.Children.Add(new TextBlock
            {
                Text = "Nothing to print with the current settings.\nCheck “What to print” in Print settings.",
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(60),
                Foreground = (Brush)FindResource("Br.TextMuted"),
            });
            return;
        }

        DocumentPage page = Page(_index);
        PageText.Text = $"Sheet {_index + 1:N0} of {_count:N0}";

        var paper = new Rectangle
        {
            Width = page.Size.Width,
            Height = page.Size.Height,
            Fill = new VisualBrush(page.Visual) { Stretch = Stretch.Fill },
        };
        PageHost.Children.Add(paper);

        if (ShowMargins.IsChecked == true)
        {
            PageHost.Children.Add(BuildMarginOverlay(page.Size));
        }

        if (_index < Thumbs.Items.Count && Thumbs.SelectedIndex != _index)
        {
            Thumbs.SelectionChanged -= OnThumbSelected;
            Thumbs.SelectedIndex = _index;
            Thumbs.SelectionChanged += OnThumbSelected;
        }

        ApplyZoom();
    }

    /// <summary>Dashed outline of the area the chosen printer can actually reach.</summary>
    private UIElement BuildMarginOverlay(Size pageSize)
    {
        var canvas = new Canvas { Width = pageSize.Width, Height = pageSize.Height, IsHitTestVisible = false };

        PrintSettings settings = _session.Project.PrintSettings;
        PageSize page = _session.Project.Template?.EffectivePage ?? PageSize.A4;
        SheetMedia media = _printServer.GetMedia(
            settings.PrinterName ?? _printServer.DefaultPrinter(), page.WidthMm, page.HeightMm);

        var rect = new Rectangle
        {
            Width = Math.Max(0, UnitConverter.MmToDiu(media.ImageableWidthMm)),
            Height = Math.Max(0, UnitConverter.MmToDiu(media.ImageableHeightMm)),
            Stroke = new SolidColorBrush(Color.FromArgb(0x88, 0xE0, 0x55, 0x5F)),
            StrokeThickness = 0.8,
            StrokeDashArray = new DoubleCollection { 4, 3 },
        };

        Canvas.SetLeft(rect, UnitConverter.MmToDiu(media.ImageableOriginXMm));
        Canvas.SetTop(rect, UnitConverter.MmToDiu(media.ImageableOriginYMm));
        canvas.Children.Add(rect);
        return canvas;
    }

    // ---- zoom -------------------------------------------------------------

    private void ApplyZoom()
    {
        if (_count == 0)
        {
            return;
        }

        Size size = Page(_index).Size;
        if (_fitMode)
        {
            double availableW = Math.Max(50, Scroller.ActualWidth - 90);
            double availableH = Math.Max(50, Scroller.ActualHeight - 90);
            _zoom = Math.Min(availableW / size.Width, availableH / size.Height);
            _zoom = Math.Clamp(_zoom, 0.05, 8);
        }

        PageHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        ZoomText.Text = $"{Math.Round(_zoom * 100)}%";
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.2);

    private void OnZoomOut(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.2);

    /// <summary>
    /// Wheel zooms towards the pointer, like the designer canvas. Shift+wheel and
    /// Ctrl+wheel keep the ScrollViewer's normal scrolling, so a zoomed-in sheet can
    /// still be panned without reaching for the scrollbars.
    /// </summary>
    private void OnScrollerWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers is ModifierKeys.Shift or ModifierKeys.Control || _count == 0)
        {
            return;
        }

        e.Handled = true;

        // Where the pointer sits in un-zoomed content coordinates, before the change.
        Point pointer = e.GetPosition(Scroller);
        double contentX = (Scroller.HorizontalOffset + pointer.X) / _zoom;
        double contentY = (Scroller.VerticalOffset + pointer.Y) / _zoom;

        SetZoom(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15));

        // Put that same content point back under the pointer. Layout has to run
        // first or the ScrollViewer clamps the offsets against the old extent.
        Scroller.UpdateLayout();
        Scroller.ScrollToHorizontalOffset((contentX * _zoom) - pointer.X);
        Scroller.ScrollToVerticalOffset((contentY * _zoom) - pointer.Y);
    }

    private void SetZoom(double value)
    {
        _fitMode = false;
        _zoom = Math.Clamp(value, 0.05, 8);
        ApplyZoom();
    }

    private void OnFit(object sender, RoutedEventArgs e)
    {
        _fitMode = true;
        ApplyZoom();
    }

    // ---- navigation -------------------------------------------------------

    private void OnFirst(object sender, RoutedEventArgs e) => GoTo(0);

    private void OnPrev(object sender, RoutedEventArgs e) => GoTo(_index - 1);

    private void OnNext(object sender, RoutedEventArgs e) => GoTo(_index + 1);

    private void OnLast(object sender, RoutedEventArgs e) => GoTo(_count - 1);

    private void GoTo(int index)
    {
        if (_count == 0)
        {
            return;
        }

        _index = Math.Clamp(index, 0, _count - 1);
        ShowPage();
    }

    private void OnThumbSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Thumbs.SelectedIndex >= 0 && Thumbs.SelectedIndex < _count)
        {
            GoTo(Thumbs.SelectedIndex);
        }
    }

    // ---- actions ----------------------------------------------------------

    private void OnOptionChanged(object sender, RoutedEventArgs e) => Build();

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        if (_shell.ShowPrintSettings())
        {
            Build();
        }
    }

    /// <summary>Opens the app's own print dialog, which spools with progress, and closes the preview if it printed.</summary>
    private void OnPrint(object sender, RoutedEventArgs e)
    {
        if (_shell.ShowPrintDialog())
        {
            Close();
        }
    }
}
