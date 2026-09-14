using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LabelMaker.App.Controls;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Views;

/// <summary>
/// Colour chooser in the manner of image editors: a saturation / brightness square with a
/// hue strip, red / green / blue / opacity and hue / saturation / brightness channels each
/// with a gradient bar, a typed value and − / + buttons, hex entry, an eyedropper that
/// reads any pixel on screen, the new colour against the old one, recent colours and the
/// app palette. Home-grown rather than the WinForms ColorDialog so it follows the app's
/// theme and handles opacity.
/// </summary>
public partial class ColorPickerWindow : Window
{
    private const double MaxRecent = 12;

    private readonly ColorRgba _old;
    private readonly IList<string>? _recent;
    private readonly List<ChannelRow> _rows = new();

    // The colour is held as HSV so hue survives passing through grey or black.
    private double _h;
    private double _s;
    private double _v;
    private byte _a = 255;
    private bool _updating;
    private bool _draggingSv;
    private bool _draggingHue;

    private Rectangle _hueFill = null!;
    private Ellipse _svMarker = null!;
    private Border _hueMarker = null!;

    public ColorPickerWindow(ColorRgba initial, string title, IList<string>? recent = null)
    {
        InitializeComponent();
        Title = title;
        _old = initial;
        _recent = recent;

        Brush checker = BuildCheckerBrush();
        CurrentChecker.Background = checker;
        OldChecker.Background = checker;
        OldSwatch.Background = new SolidColorBrush(ToWpf(initial));

        BuildSvArea();
        BuildHueStrip();
        BuildChannels();
        BuildRecent();

        DependencyPropertyDescriptor.FromProperty(ColorPalette.HexProperty, typeof(ColorPalette))
            .AddValueChanged(Palette, (_, _) =>
            {
                if (!_updating && ColorRgba.TryParse(Palette.Hex, out ColorRgba c))
                {
                    SetRgb(c);
                }
            });

        Scale100.IsChecked = true;
        SetRgb(initial);
        Result = initial;
        Loaded += (_, _) => Refresh();
    }

    public ColorRgba Result { get; private set; }

    private ColorRgba Current => ColorSpace.FromHsv(_h, _s, _v, _a);

    private bool Use255 => Scale255.IsChecked == true;

    // ---- state ---------------------------------------------------------------

    private void SetRgb(ColorRgba c)
    {
        (double h, double s, double v) = ColorSpace.ToHsv(c);
        if (s > 1e-6 && v > 1e-6)
        {
            _h = h;
        }

        _s = s;
        _v = v;
        _a = c.A;
        Refresh();
    }

    private void SetHsv(double h, double s, double v)
    {
        _h = ((h % 360) + 360) % 360;
        _s = Math.Clamp(s, 0, 1);
        _v = Math.Clamp(v, 0, 1);
        Refresh();
    }

    private void Refresh()
    {
        _updating = true;
        try
        {
            ColorRgba c = Current;
            Result = c;
            CurrentSwatch.Background = new SolidColorBrush(ToWpf(c));
            _hueFill.Fill = new SolidColorBrush(ToWpf(ColorSpace.FromHsv(_h, 1, 1)));

            double w = SvArea.ActualWidth, h = SvArea.ActualHeight;
            if (w > 0 && h > 0)
            {
                Canvas.SetLeft(_svMarker, (_s * w) - 7);
                Canvas.SetTop(_svMarker, ((1 - _v) * h) - 7);
            }

            if (HueStrip.ActualHeight > 0)
            {
                Canvas.SetTop(_hueMarker, (_h / 360 * HueStrip.ActualHeight) - 3);
                _hueMarker.Width = HueStrip.ActualWidth;
            }

            foreach (ChannelRow row in _rows)
            {
                row.Refresh();
            }

            if (!HexBox.IsKeyboardFocusWithin)
            {
                HexBox.Text = c.ToHex();
            }

            Palette.Hex = c.ToHex();
        }
        finally
        {
            _updating = false;
        }
    }

    // ---- saturation / brightness square and hue strip ---------------------------

    private void BuildSvArea()
    {
        _hueFill = new Rectangle();
        var whiteToClear = new Rectangle
        {
            Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(1, 0)),
        };
        var clearToBlack = new Rectangle
        {
            Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, new Point(0, 0), new Point(0, 1)),
        };
        _svMarker = new Ellipse { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
        _svMarker.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.9 };

        foreach (Rectangle layer in new[] { _hueFill, whiteToClear, clearToBlack })
        {
            layer.IsHitTestVisible = false;
            SvArea.Children.Add(layer);
        }

        SvArea.Children.Add(_svMarker);
        SvArea.SizeChanged += (_, _) =>
        {
            foreach (Rectangle layer in new[] { _hueFill, whiteToClear, clearToBlack })
            {
                layer.Width = SvArea.ActualWidth;
                layer.Height = SvArea.ActualHeight;
            }

            Refresh();
        };
    }

    private void BuildHueStrip()
    {
        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        for (int i = 0; i <= 6; i++)
        {
            rainbow.GradientStops.Add(new GradientStop(ToWpf(ColorSpace.FromHsv(i * 60, 1, 1)), i / 6.0));
        }

        var fill = new Rectangle { Fill = rainbow, IsHitTestVisible = false };
        _hueMarker = new Border
        {
            Height = 6,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.5),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.9 },
        };
        HueStrip.Children.Add(fill);
        HueStrip.Children.Add(_hueMarker);
        HueStrip.SizeChanged += (_, _) =>
        {
            fill.Width = HueStrip.ActualWidth;
            fill.Height = HueStrip.ActualHeight;
            Refresh();
        };
    }

    private void OnSvDown(object sender, MouseButtonEventArgs e)
    {
        _draggingSv = true;
        SvArea.CaptureMouse();
        PickSv(e.GetPosition(SvArea));
    }

    private void OnSvMove(object sender, MouseEventArgs e)
    {
        if (_draggingSv)
        {
            PickSv(e.GetPosition(SvArea));
        }
    }

    private void PickSv(Point p) =>
        SetHsv(_h, p.X / Math.Max(1, SvArea.ActualWidth), 1 - (p.Y / Math.Max(1, SvArea.ActualHeight)));

    private void OnHueDown(object sender, MouseButtonEventArgs e)
    {
        _draggingHue = true;
        HueStrip.CaptureMouse();
        PickHue(e.GetPosition(HueStrip));
    }

    private void OnHueMove(object sender, MouseEventArgs e)
    {
        if (_draggingHue)
        {
            PickHue(e.GetPosition(HueStrip));
        }
    }

    private void PickHue(Point p) =>
        SetHsv(Math.Clamp(p.Y / Math.Max(1, HueStrip.ActualHeight), 0, 0.9999) * 360, _s, _v);

    private void OnAreaUp(object sender, MouseButtonEventArgs e)
    {
        _draggingSv = _draggingHue = false;
        Mouse.Capture(null);
    }

    // ---- channels ------------------------------------------------------------------

    private void BuildChannels()
    {
        double RgbMax() => Use255 ? 255 : 100;
        double ToDisplay(byte b) => Use255 ? b : b / 2.55;
        byte FromDisplay(double d) => (byte)Math.Clamp(Math.Round(Use255 ? d : d * 2.55), 0, 255);

        ColorRgba C() => Current;

        _rows.Add(new ChannelRow(this, "R", "Red", RgbMax, () => ToDisplay(C().R), d => SetRgb(C() with { R = FromDisplay(d) }),
            () => new[] { C() with { R = 0, A = 255 }, C() with { R = 255, A = 255 } }));
        _rows.Add(new ChannelRow(this, "G", "Green", RgbMax, () => ToDisplay(C().G), d => SetRgb(C() with { G = FromDisplay(d) }),
            () => new[] { C() with { G = 0, A = 255 }, C() with { G = 255, A = 255 } }));
        _rows.Add(new ChannelRow(this, "B", "Blue", RgbMax, () => ToDisplay(C().B), d => SetRgb(C() with { B = FromDisplay(d) }),
            () => new[] { C() with { B = 0, A = 255 }, C() with { B = 255, A = 255 } }));
        _rows.Add(new ChannelRow(this, "A", "Opacity", RgbMax, () => ToDisplay(_a), d => { _a = FromDisplay(d); Refresh(); },
            () => new[] { C() with { A = 0 }, C() with { A = 255 } }, checkered: true) { GapBefore = false });

        _rows.Add(new ChannelRow(this, "H", "Hue (degrees)", () => 360, () => _h, d => SetHsv(Math.Clamp(d, 0, 359.99), _s, _v),
            () => Enumerable.Range(0, 7).Select(i => ColorSpace.FromHsv(i * 60, Math.Max(_s, 0.15), Math.Max(_v, 0.35))).ToArray()) { GapBefore = true });
        _rows.Add(new ChannelRow(this, "S", "Saturation (%)", () => 100, () => _s * 100, d => SetHsv(_h, d / 100, _v),
            () => new[] { ColorSpace.FromHsv(_h, 0, _v), ColorSpace.FromHsv(_h, 1, _v) }));
        _rows.Add(new ChannelRow(this, "V", "Brightness (%)", () => 100, () => _v * 100, d => SetHsv(_h, _s, d / 100),
            () => new[] { ColorSpace.FromHsv(_h, _s, 0), ColorSpace.FromHsv(_h, _s, 1) }));

        Channels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        Channels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Channels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        Channels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < _rows.Count; i++)
        {
            Channels.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _rows[i].AddTo(Channels, i);
        }
    }

    private void OnScaleChanged(object sender, RoutedEventArgs e) => Refresh();

    /// <summary>One channel: its letter, a gradient bar to drag along, the typed value and − / + buttons.</summary>
    private sealed class ChannelRow
    {
        private readonly ColorPickerWindow _owner;
        private readonly string _letter;
        private readonly string _name;
        private readonly Func<double> _max;
        private readonly Func<double> _get;
        private readonly Action<double> _set;
        private readonly Func<ColorRgba[]> _stops;
        private readonly bool _checkered;

        private readonly Border _bar = new() { Height = 18, Margin = new Thickness(0, 9, 0, 9), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border _gradient = new() { CornerRadius = new CornerRadius(4) };
        private readonly Canvas _markerLayer = new() { IsHitTestVisible = false };
        private readonly Path _marker = new()
        {
            Data = Geometry.Parse("M 0,0 L 5,-7 L -5,-7 Z M 0,18 L 5,25 L -5,25 Z"),
            Fill = Brushes.White,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
        };

        private readonly TextBox _box = new() { Margin = new Thickness(10, 0, 6, 0), Padding = new Thickness(4, 5, 6, 5), VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Right };
        private bool _dragging;

        public ChannelRow(ColorPickerWindow owner, string letter, string name, Func<double> max, Func<double> get, Action<double> set,
            Func<ColorRgba[]> stops, bool checkered = false)
        {
            _owner = owner;
            _letter = letter;
            _name = name;
            _max = max;
            _get = get;
            _set = set;
            _stops = stops;
            _checkered = checkered;
        }

        public bool GapBefore { get; init; }

        public void AddTo(Grid grid, int row)
        {
            var label = new TextBlock { Text = _letter, VerticalAlignment = VerticalAlignment.Center, ToolTip = _name };
            label.SetResourceReference(TextBlock.FontFamilyProperty, "Font.UiSemiBold");

            if (_checkered)
            {
                _bar.Background = BuildCheckerBrush();
            }

            var inner = new Grid();
            inner.Children.Add(_gradient);
            inner.Children.Add(_markerLayer);
            _markerLayer.Children.Add(_marker);
            _bar.Child = inner;
            _bar.SetResourceReference(Border.BorderBrushProperty, "Br.BorderStrong");
            _bar.BorderThickness = new Thickness(1);
            _bar.ToolTip = _name;
            _bar.MouseLeftButtonDown += (_, e) => { _dragging = true; _bar.CaptureMouse(); Pick(e.GetPosition(_bar)); };
            _bar.MouseMove += (_, e) => { if (_dragging) { Pick(e.GetPosition(_bar)); } };
            _bar.MouseLeftButtonUp += (_, _) => { _dragging = false; _bar.ReleaseMouseCapture(); };
            _bar.SizeChanged += (_, _) => Refresh();

            _box.SetResourceReference(Control.StyleProperty, "NumberBox");
            _box.SetResourceReference(Control.FontFamilyProperty, "Font.Ui");
            _box.LostFocus += (_, _) => Commit();
            _box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Commit();
                    e.Handled = true;
                }
                else if (e.Key is Key.Up or Key.Down)
                {
                    Step(e.Key == Key.Up ? 1 : -1);
                    e.Handled = true;
                }
            };

            var minus = StepButton("M 0,4 L 8,4", -1);
            var plus = StepButton("M 0,4 L 8,4 M 4,0 L 4,8", 1);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(minus);
            buttons.Children.Add(plus);

            var margin = new Thickness(0, GapBefore ? 12 : 0, 0, 0);
            foreach ((UIElement element, int column) in new (UIElement, int)[] { (label, 0), (_bar, 1), (_box, 2), (buttons, 3) })
            {
                if (element is FrameworkElement fe)
                {
                    fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + margin.Top, fe.Margin.Right, fe.Margin.Bottom);
                }

                Grid.SetRow(element, row);
                Grid.SetColumn(element, column);
                grid.Children.Add(element);
            }
        }

        private RepeatButton StepButton(string glyph, int direction)
        {
            var button = new RepeatButton
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(direction < 0 ? 0 : 4, 0, 0, 0),
                ToolTip = direction < 0 ? "Decrease" : "Increase",
                Content = new Path { Data = Geometry.Parse(glyph), StrokeThickness = 1.6 },
            };
            ((Path)button.Content).SetResourceReference(Shape.StrokeProperty, "Br.Text2");
            button.SetResourceReference(Control.StyleProperty, "SliderStepButton");
            button.Click += (_, _) => Step(direction);
            return button;
        }

        private void Step(int direction) => _set(Math.Clamp(Math.Round(_get()) + direction, 0, _max()));

        private void Pick(Point p) => _set(Math.Clamp(p.X / Math.Max(1, _bar.ActualWidth), 0, 1) * _max());

        private void Commit()
        {
            if (_owner._updating)
            {
                return;
            }

            if (double.TryParse(_box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
                double.TryParse(_box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                _set(Math.Clamp(value, 0, _max()));
            }
            else
            {
                Refresh();
            }
        }

        public void Refresh()
        {
            ColorRgba[] stops = _stops();
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            for (int i = 0; i < stops.Length; i++)
            {
                brush.GradientStops.Add(new GradientStop(ToWpf(stops[i]), stops.Length == 1 ? 0 : i / (double)(stops.Length - 1)));
            }

            _gradient.Background = brush;

            double value = _get();
            double width = Math.Max(0, _bar.ActualWidth - 2);
            Canvas.SetLeft(_marker, Math.Clamp(value / Math.Max(1e-9, _max()), 0, 1) * width);
            Canvas.SetTop(_marker, 0);

            if (!_box.IsKeyboardFocusWithin)
            {
                _box.Text = _max() >= 255 || _letter == "H" ? Math.Round(value).ToString(CultureInfo.CurrentCulture) : value.ToString("0.#", CultureInfo.CurrentCulture);
            }
        }
    }

    // ---- hex, recent colours, buttons ---------------------------------------------

    private void OnHexCommitted(object sender, RoutedEventArgs e)
    {
        if (ColorRgba.TryParse(HexBox.Text, out ColorRgba c))
        {
            SetRgb(c);
        }
        else
        {
            HexBox.Text = Current.ToHex();
        }
    }

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ColorRgba.TryParse(HexBox.Text, out ColorRgba c))
        {
            SetRgb(c);
            HexBox.SelectAll();
            e.Handled = true;
        }
    }

    private void BuildRecent()
    {
        RecentRow.Items.Clear();
        foreach (string hex in _recent ?? (IList<string>)Array.Empty<string>())
        {
            if (!ColorRgba.TryParse(hex, out ColorRgba c))
            {
                continue;
            }

            var inner = new Border { CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(ToWpf(c)) };
            var cell = new Border
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 0, 5, 5),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                Background = BuildCheckerBrush(),
                Child = inner,
                Cursor = Cursors.Hand,
                ToolTip = c.ToHex(),
            };
            cell.SetResourceReference(Border.BorderBrushProperty, "Br.BorderStrong");
            cell.MouseLeftButtonUp += (_, _) => SetRgb(c);
            RecentRow.Items.Add(cell);
        }
    }

    private void Remember(ColorRgba c)
    {
        if (_recent is null)
        {
            return;
        }

        string hex = c.ToHex();
        for (int i = _recent.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_recent[i], hex, StringComparison.OrdinalIgnoreCase))
            {
                _recent.RemoveAt(i);
            }
        }

        _recent.Insert(0, hex);
        while (_recent.Count > MaxRecent)
        {
            _recent.RemoveAt(_recent.Count - 1);
        }
    }

    private void OnAddRecent(object sender, RoutedEventArgs e)
    {
        Remember(Current);
        BuildRecent();
    }

    private void OnOldClicked(object sender, MouseButtonEventArgs e) => SetRgb(_old);

    private void OnReset(object sender, RoutedEventArgs e) => SetRgb(_old);

    private void OnTransparent(object sender, RoutedEventArgs e)
    {
        Result = ColorRgba.Transparent;
        DialogResult = true;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (HexBox.IsKeyboardFocusWithin && ColorRgba.TryParse(HexBox.Text, out ColorRgba typed))
        {
            SetRgb(typed);
        }

        Result = Current;
        Remember(Result);
        DialogResult = true;
    }

    private static Color ToWpf(ColorRgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private static Brush BuildCheckerBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
        var dark = new SolidColorBrush(Color.FromRgb(0xCC, 0xD2, 0xDC));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 6, 6))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(6, 6, 6, 6))));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 12, 12),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    // ---- eyedropper -------------------------------------------------------

    private void OnPickFromScreen(object sender, RoutedEventArgs e)
    {
        Mouse.OverrideCursor = Cursors.Cross;
        CaptureMouse();
        PreviewMouseLeftButtonDown += PickOnce;
    }

    private void PickOnce(object sender, MouseButtonEventArgs e)
    {
        PreviewMouseLeftButtonDown -= PickOnce;
        ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        e.Handled = true;

        if (!GetCursorPos(out NativePoint p))
        {
            return;
        }

        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            uint bgr = GetPixel(dc, p.X, p.Y);
            SetRgb(new ColorRgba((byte)(bgr & 0xFF), (byte)((bgr >> 8) & 0xFF), (byte)((bgr >> 16) & 0xFF), _a));
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr dc, int x, int y);
}
