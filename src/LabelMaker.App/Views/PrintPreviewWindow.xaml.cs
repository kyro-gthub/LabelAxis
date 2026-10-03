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
    private SheetMeasurements? _measurements;
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
        // Refit when the viewing area itself changes size. The window's own SizeChanged can
        // arrive before the page area has been laid out at the new size (maximize, snap),
        // which left the sheet fitted to the old, smaller window.
        Scroller.SizeChanged += (_, _) => { if (_fitMode) { ApplyZoom(); } };
        StateChanged += (_, _) => Dispatcher.BeginInvoke(() => { if (_fitMode) { ApplyZoom(); } }, System.Windows.Threading.DispatcherPriority.Loaded);
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

            // Measured before the page is shown: ShowPage draws the overlay from them.
            LabelTemplate? t = _session.Project.Template is null ? null : PrintLayoutResolver.Resolve(_session.Project, settings);
            _measurements = t is null ? null : SheetMeasurements.From(t, _printServer.MapperFor(printer, settings, t.EffectivePage));

            _index = Math.Clamp(_index, 0, Math.Max(0, _count - 1));
            BuildThumbnails();
            ShowPage();

            LeftStatus.Text = t is null || _measurements is null
                ? "No sheet template selected."
                : $"{t.Brand} {t.ProductCode} · {_measurements.Describe()} · {t.EffectivePage.Name}" +
                  (_printServer.IsCalibrated(printer) || Math.Abs(settings.ScalePercent - 100) > 0.01 ? " (as printed, with calibration and scale)" : string.Empty);
            LeftStatus.ToolTip = LeftStatus.Text;
            RightStatus.Text = $"{printer ?? "No printer"} · {DescribeContent(settings)} · {settings.ScalePercent:0.#}% scale";
        }
        catch (Exception ex)
        {
            _count = 0;
            _pageCache.Clear();
            _pageOrder.Clear();
            Thumbs.ItemsSource = null;
            PageHost.Children.Clear();
            MeasureLayer.Children.Clear();
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

        public Brush Preview => _preview ??= SheetBrush(_page(Index));
    }

    /// <summary>
    /// Paints a whole sheet. The viewbox is the paper, not the visual's content bounds
    /// (VisualBrush's default), which would stretch a part-filled sheet over the page and
    /// hide its real margins.
    /// </summary>
    internal static VisualBrush SheetBrush(DocumentPage page) => new(page.Visual)
    {
        Stretch = Stretch.Fill,
        ViewboxUnits = BrushMappingMode.Absolute,
        Viewbox = new Rect(page.Size),
    };

    private void ShowPage()
    {
        PageHost.Children.Clear();
        if (_count == 0)
        {
            PageText.Text = "— / —";
            MeasureLayer.Children.Clear();
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
            Fill = SheetBrush(page),
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
        PageSize page = _session.Project.Template is null ? PageSize.A4 : PrintLayoutResolver.Resolve(_session.Project, settings).EffectivePage;
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

    // ---- measurements -----------------------------------------------------

    private static readonly Brush MarginInk = Frozen(0xD9, 0x48, 0x0F);
    private static readonly Brush LabelInk = Frozen(0x1F, 0x6F, 0xEB);
    private static readonly Brush GapInk = Frozen(0x7A, 0x3E, 0xD6);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static readonly Brush GuideInk = Frozen(0x8A, 0x94, 0xA3);

    // Room around the sheet for the dimension chains, in screen pixels.
    private static readonly Thickness MeasureGutter = new(150, 66, 72, 36);
    private static readonly Thickness PlainGutter = new(10);

    // Distances outward from the paper edge: the two dimension-line lanes, then the tags.
    private const double Lane0 = 12;
    private const double Lane1 = 26;
    private const double TagEdge = 34;

    private bool MeasurementsShown => ShowMeasurements.IsChecked == true && _measurements is not null && _count > 0;

    private void OnMeasurementsChanged(object sender, RoutedEventArgs e) => ApplyZoom();

    /// <summary>One dimension: a stretch of the sheet along one axis, in millimetres from the paper edge.</summary>
    private sealed record Span(double StartMm, double EndMm, string Text, Brush Ink, double Lane);

    /// <summary>
    /// Dimension chains in the gutter around the sheet, the way a technical drawing is
    /// dimensioned: across the top the left margin, label width, gap, pitch and right
    /// margin; down the left the top margin, label height, gap, pitch and bottom margin.
    /// Dashed guides run to the label edges being measured, the first label is outlined,
    /// and tags slide apart so none overlap at any zoom. Preview only, never printed.
    /// </summary>
    private void DrawMeasurements()
    {
        MeasureLayer.Children.Clear();
        if (!MeasurementsShown)
        {
            return;
        }

        SheetMeasurements m = _measurements!;
        string Mm(double v) => SheetMeasurements.Mm(v) + " mm";
        RectMm first = m.First;
        RectMm last = m.Last;

        // Which labels are being measured.
        Outline(first, LabelInk, 0.10);
        if (m.SecondInRow is { } r)
        {
            Outline(r, GapInk, 0.05);
        }

        if (m.SecondInColumn is { } b)
        {
            Outline(b, GapInk, 0.05);
        }

        var across = new List<Span>
        {
            new(0, first.X, "left " + Mm(m.MarginLeftMm), MarginInk, Lane0),
            new(first.X, first.Right, "label " + Mm(m.LabelWidthMm), LabelInk, Lane0),
        };
        if (m.SecondInRow is { } right)
        {
            across.Add(m.HGapMm > 0.005
                ? new Span(first.Right, right.X, "gap " + Mm(m.HGapMm), GapInk, Lane0)
                : new Span(first.Right, first.Right, "no gap", GapInk, Lane0));
            if (m.HGapMm > 0.005)
            {
                across.Add(new Span(first.X, right.X, "pitch " + Mm(m.HPitchMm), GapInk, Lane1));
            }
        }

        across.Add(new Span(last.Right, m.PageWidthMm, "right " + Mm(m.MarginRightMm), MarginInk, Lane0));

        var down = new List<Span>
        {
            new(0, first.Y, "top " + Mm(m.MarginTopMm), MarginInk, Lane0),
            new(first.Y, first.Bottom, "label " + Mm(m.LabelHeightMm), LabelInk, Lane0),
        };
        if (m.SecondInColumn is { } below)
        {
            down.Add(m.VGapMm > 0.005
                ? new Span(first.Bottom, below.Y, "gap " + Mm(m.VGapMm), GapInk, Lane0)
                : new Span(first.Bottom, first.Bottom, "no gap", GapInk, Lane0));
            if (m.VGapMm > 0.005)
            {
                down.Add(new Span(first.Y, below.Y, "pitch " + Mm(m.VPitchMm), GapInk, Lane1));
            }
        }

        down.Add(new Span(last.Bottom, m.PageHeightMm, "bottom " + Mm(m.MarginBottomMm), MarginInk, Lane0));

        Chain(across, horizontal: true, guideToMm: first.Y, pageExtentMm: m.PageWidthMm);
        Chain(down, horizontal: false, guideToMm: first.X, pageExtentMm: m.PageHeightMm);
    }

    /// <summary>Millimetres on the sheet to screen pixels from its top-left corner.</summary>
    private double Px(double mm) => UnitConverter.MmToDiu(mm) * _zoom;

    private void Outline(RectMm rect, Brush ink, double fillOpacity)
    {
        var fill = ink.CloneCurrentValue();
        fill.Opacity = fillOpacity;
        var box = new Rectangle
        {
            Width = Math.Max(1, Px(rect.Width)),
            Height = Math.Max(1, Px(rect.Height)),
            Stroke = ink,
            StrokeThickness = 1.2,
            Fill = fill,
        };
        Canvas.SetLeft(box, Px(rect.X));
        Canvas.SetTop(box, Px(rect.Y));
        MeasureLayer.Children.Add(box);
    }

    /// <summary>
    /// One chain of dimensions along the top edge (<paramref name="horizontal"/>) or down the
    /// left edge. Coordinates are worked in (along, out): along the edge, and outward from it.
    /// </summary>
    private void Chain(List<Span> spans, bool horizontal, double guideToMm, double pageExtentMm)
    {
        Point At(double along, double outward) => horizontal ? new Point(along, -outward) : new Point(-outward, along);

        // Dashed guides from the lanes to the label edges being measured.
        foreach (double mm in spans.SelectMany(sp => new[] { sp.StartMm, sp.EndMm }).Distinct())
        {
            if (mm > 0.01 && mm < pageExtentMm - 0.01)
            {
                double p = Px(mm);
                MeasureLayer.Children.Add(new Line
                {
                    X1 = At(p, Lane1 + 4).X, Y1 = At(p, Lane1 + 4).Y,
                    X2 = At(p, -Px(guideToMm)).X, Y2 = At(p, -Px(guideToMm)).Y,
                    Stroke = GuideInk,
                    StrokeThickness = 0.8,
                    StrokeDashArray = new DoubleCollection { 3, 3 },
                });
            }
        }

        // Tags in one row above (or one column left of) the lanes, in order along the edge,
        // each pushed along just far enough to clear the one before it.
        // Each leader starts where its tag is ordered from — the middle of the line, or near
        // the far end of an outer-lane line — so leaders run in tag order and never cross.
        var tags = spans
            .Select(sp => (Span: sp, Tag: MakeTag(sp.Text, sp.Ink),
                Mid: sp.Lane > Lane0 ? Math.Max(Px(sp.StartMm), Px(sp.EndMm) - 6) : Px((sp.StartMm + sp.EndMm) / 2)))
            .OrderBy(t => t.Mid).ThenBy(t => t.Span.Lane)
            .ToList();

        double previousEnd = double.NegativeInfinity;
        foreach ((Span span, Border tag, double mid) in tags)
        {
            double a = Px(span.StartMm);
            double b = Px(span.EndMm);
            DimensionLine(At(a, span.Lane), At(b, span.Lane), span.Ink, horizontal);

            Size size = tag.DesiredSize;
            double length = horizontal ? size.Width : size.Height;
            double start = Math.Max(mid - (length / 2), previousEnd + 4);
            previousEnd = start + length;

            double left = horizontal ? start : -TagEdge - size.Width;
            double top = horizontal ? -TagEdge - size.Height : start;
            Canvas.SetLeft(tag, left);
            Canvas.SetTop(tag, top);

            // Leader from the middle of the dimension line to the tag.
            Point from = At(mid, span.Lane);
            Point to = horizontal ? new Point(start + (length / 2), -TagEdge) : new Point(-TagEdge, start + (length / 2));
            MeasureLayer.Children.Add(new Line { X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, Stroke = span.Ink, StrokeThickness = 0.8, Opacity = 0.7 });
            MeasureLayer.Children.Add(tag);
        }
    }

    /// <summary>A dimension line with end ticks and arrowheads; a zero-length one is a single tick.</summary>
    private void DimensionLine(Point a, Point b, Brush ink, bool horizontal)
    {
        double length = (b - a).Length;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        {
            if (length < 0.5)
            {
                // Butted labels: mark the shared edge, square to the chain.
                Vector tick = horizontal ? new Vector(0, 6) : new Vector(6, 0);
                g.BeginFigure(a - tick, false, false);
                g.LineTo(a + tick, true, false);
            }
            else
            {
                Vector along = (b - a) / length;
                var across = new Vector(-along.Y, along.X);
                double head = Math.Min(5, length / 3);
                g.BeginFigure(a, false, false);
                g.LineTo(b, true, false);
                foreach ((Point end, Vector inward) in new[] { (a, along), (b, -along) })
                {
                    g.BeginFigure(end - (across * 4), false, false);
                    g.LineTo(end + (across * 4), true, false);
                    g.BeginFigure(end + (inward * head) + (across * head * 0.45), true, true);
                    g.LineTo(end, true, false);
                    g.LineTo(end + (inward * head) - (across * head * 0.45), true, false);
                }
            }
        }

        geometry.Freeze();
        MeasureLayer.Children.Add(new Path { Data = geometry, Stroke = ink, Fill = ink, StrokeThickness = 1 });
    }

    /// <summary>A value tag, measured so the chain can place it.</summary>
    private Border MakeTag(string text, Brush ink)
    {
        var tag = new Border
        {
            Background = Brushes.White,
            BorderBrush = ink,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = ink,
                FontFamily = (FontFamily)FindResource("Font.UiSemiBold"),
            },
        };
        tag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return tag;
    }

    // ---- zoom -------------------------------------------------------------

    private void ApplyZoom()
    {
        if (_count == 0)
        {
            return;
        }

        Size size = Page(_index).Size;
        Thickness gutter = MeasurementsShown ? MeasureGutter : PlainGutter;
        Sheet.Margin = gutter;
        if (_fitMode)
        {
            double availableW = Math.Max(50, Scroller.ActualWidth - 70 - gutter.Left - gutter.Right);
            double availableH = Math.Max(50, Scroller.ActualHeight - 70 - gutter.Top - gutter.Bottom);
            _zoom = Math.Min(availableW / size.Width, availableH / size.Height);
            _zoom = Math.Clamp(_zoom, 0.05, 8);
        }

        PageHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        ZoomText.Text = $"{Math.Round(_zoom * 100)}%";
        DrawMeasurements();
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
        // The sheet starts after the scroller's padding and the gutter around it.
        Point pointer = e.GetPosition(Scroller);
        double originX = Scroller.Padding.Left + Sheet.Margin.Left;
        double originY = Scroller.Padding.Top + Sheet.Margin.Top;
        double contentX = (Scroller.HorizontalOffset + pointer.X - originX) / _zoom;
        double contentY = (Scroller.VerticalOffset + pointer.Y - originY) / _zoom;

        SetZoom(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15));

        // Put that same content point back under the pointer. Layout has to run
        // first or the ScrollViewer clamps the offsets against the old extent.
        Scroller.UpdateLayout();
        Scroller.ScrollToHorizontalOffset((contentX * _zoom) + originX - pointer.X);
        Scroller.ScrollToVerticalOffset((contentY * _zoom) + originY - pointer.Y);
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
