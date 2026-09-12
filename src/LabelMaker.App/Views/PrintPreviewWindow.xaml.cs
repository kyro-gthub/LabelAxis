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
    private readonly List<DocumentPage> _pages = new();
    private int _index;
    private double _zoom = 1.0;
    private bool _fitMode = true;
    private bool _building;

    private const int MaxThumbnails = 60;

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

            string? printer = settings.PrinterName ?? _printServer.DefaultPrinter();

            _paginator = settings.Content == PrintContent.Pages
                ? _printServer.BuildPagesPaginator(_session.Project, settings, printer)
                : _printServer.BuildPaginator(_session.Project, _session.Data, settings, printer);

            _pages.Clear();
            int count = _paginator.PageCount;
            for (int i = 0; i < count; i++)
            {
                _pages.Add(_paginator.GetPage(i));
            }

            _index = Math.Clamp(_index, 0, Math.Max(0, _pages.Count - 1));
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
            _pages.Clear();
            Thumbs.Items.Clear();
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
        Thumbs.Items.Clear();

        for (int i = 0; i < _pages.Count && i < MaxThumbnails; i++)
        {
            DocumentPage page = _pages[i];
            double w = 132;
            double h = page.Size.Width > 0 ? w * page.Size.Height / page.Size.Width : w * 1.414;

            var panel = new StackPanel { Margin = new Thickness(2, 4, 2, 4) };
            panel.Children.Add(new Border
            {
                Width = w,
                Height = h,
                Background = new VisualBrush(page.Visual) { Stretch = Stretch.Uniform },
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)FindResource("Br.BorderStrong"),
                SnapsToDevicePixels = true,
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"Sheet {i + 1}",
                FontSize = 11.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = (Brush)FindResource("Br.TextMuted"),
            });

            Thumbs.Items.Add(new ListBoxItem { Content = panel, Padding = new Thickness(4) });
        }

        if (_pages.Count > MaxThumbnails)
        {
            Thumbs.Items.Add(new ListBoxItem
            {
                Content = new TextBlock
                {
                    Text = $"+{_pages.Count - MaxThumbnails} more sheets",
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)FindResource("Br.TextMuted"),
                },
                IsEnabled = false,
                Padding = new Thickness(8),
            });
        }

        if (_index < Thumbs.Items.Count)
        {
            Thumbs.SelectedIndex = _index;
        }

        Thumbs.SelectionChanged += OnThumbSelected;
    }

    private void ShowPage()
    {
        PageHost.Children.Clear();
        if (_pages.Count == 0)
        {
            PageText.Text = "— / —";
            return;
        }

        DocumentPage page = _pages[_index];
        PageText.Text = $"Sheet {_index + 1} of {_pages.Count}";

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
        if (_pages.Count == 0)
        {
            return;
        }

        Size size = _pages[_index].Size;
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

    private void OnLast(object sender, RoutedEventArgs e) => GoTo(_pages.Count - 1);

    private void GoTo(int index)
    {
        if (_pages.Count == 0)
        {
            return;
        }

        _index = Math.Clamp(index, 0, _pages.Count - 1);
        ShowPage();
    }

    private void OnThumbSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Thumbs.SelectedIndex >= 0 && Thumbs.SelectedIndex < _pages.Count)
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

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_printServer.Print(_session.Project, _session.Data, _session.Project.PrintSettings))
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Printing failed:\n\n" + ex.Message, "Label Maker",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
