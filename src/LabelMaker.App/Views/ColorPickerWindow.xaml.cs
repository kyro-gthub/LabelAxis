using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using LabelMaker.App.Controls;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Views;

/// <summary>
/// Colour chooser: a colour field (saturation across, brightness down), hue and opacity
/// sliders, a split preview of the new and the original colour, hex entry with an
/// eyedropper, number fields in RGB or HSB, and the app palette beside the colours used
/// recently. Home-grown rather than the WinForms ColorDialog so it follows the app's
/// theme and handles opacity.
/// </summary>
public partial class ColorPickerWindow : Window
{
    private const int MaxRecent = 20;

    private readonly ColorRgba _old;
    private readonly IList<string>? _recent;
    private readonly List<SpinField> _fields = new();

    // The colour is held as HSV so the hue survives passing through grey or black.
    private double _h;
    private double _s;
    private double _v;
    private byte _a = 255;
    private bool _updating;
    private bool _draggingField;

    private Rectangle _hueLayer = null!;
    private Ellipse _fieldMarker = null!;
    private PillSlider _hueSlider = null!;
    private PillSlider _alphaSlider = null!;

    public ColorPickerWindow(ColorRgba initial, string title, IList<string>? recent = null)
    {
        InitializeComponent();
        Title = title;
        _old = initial;
        _recent = recent;

        PreviewChecker.Fill = CheckerBrush(8);
        OldHalf.Fill = new SolidColorBrush(ToWpf(initial));

        BuildColourField();
        BuildSliders();
        ModeRgb.IsChecked = true;
        BuildFields();
        BuildRecent();

        DependencyPropertyDescriptor.FromProperty(ColorPalette.HexProperty, typeof(ColorPalette))
            .AddValueChanged(Palette, (_, _) =>
            {
                if (!_updating && ColorRgba.TryParse(Palette.Hex, out ColorRgba c))
                {
                    SetRgb(c);
                }
            });

        SetRgb(initial);
        Result = initial;
        Loaded += (_, _) => Refresh();
    }

    public ColorRgba Result { get; private set; }

    private ColorRgba Current => ColorSpace.FromHsv(_h, _s, _v, _a);

    private bool Hsb => ModeHsb.IsChecked == true;

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
        _h = Math.Clamp(h, 0, 359.999);
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
            NewHalf.Fill = new SolidColorBrush(ToWpf(c));
            _hueLayer.Fill = new SolidColorBrush(ToWpf(ColorSpace.FromHsv(_h, 1, 1)));

            double w = SvArea.ActualWidth, h = SvArea.ActualHeight;
            if (w > 0 && h > 0)
            {
                Canvas.SetLeft(_fieldMarker, (_s * w) - 9);
                Canvas.SetTop(_fieldMarker, ((1 - _v) * h) - 9);
                _fieldMarker.Fill = new SolidColorBrush(ToWpf(c with { A = 255 }));
            }

            _hueSlider.Set(_h / 360, ColorSpace.FromHsv(_h, 1, 1));
            _alphaSlider.Background(new[] { c with { A = 0 }, c with { A = 255 } });
            _alphaSlider.Set(_a / 255.0, c);

            foreach (SpinField field in _fields)
            {
                field.Refresh();
            }

            if (!HexBox.IsKeyboardFocusWithin)
            {
                HexBox.Text = c.ToHex().TrimStart('#');
            }

            Palette.Hex = c.ToHex();
        }
        finally
        {
            _updating = false;
        }
    }

    // ---- colour field ------------------------------------------------------------

    private void BuildColourField()
    {
        _hueLayer = new Rectangle();
        var whiteToClear = new Rectangle
        {
            Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(1, 0)),
        };
        var clearToBlack = new Rectangle
        {
            Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, new Point(0, 0), new Point(0, 1)),
        };
        _fieldMarker = new Ellipse
        {
            Width = 18,
            Height = 18,
            Stroke = Brushes.White,
            StrokeThickness = 3,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.6 },
        };

        Rectangle[] layers = { _hueLayer, whiteToClear, clearToBlack };
        foreach (Rectangle layer in layers)
        {
            layer.IsHitTestVisible = false;
            SvArea.Children.Add(layer);
        }

        SvArea.Children.Add(_fieldMarker);
        SvArea.SizeChanged += (_, _) =>
        {
            foreach (Rectangle layer in layers)
            {
                layer.Width = SvArea.ActualWidth;
                layer.Height = SvArea.ActualHeight;
            }

            // Round the field's corners to match its frame.
            SvArea.Clip = new RectangleGeometry(new Rect(0, 0, SvArea.ActualWidth, SvArea.ActualHeight), 11, 11);
            Refresh();
        };
    }

    private void OnSvDown(object sender, MouseButtonEventArgs e)
    {
        _draggingField = true;
        SvArea.CaptureMouse();
        PickField(e.GetPosition(SvArea));
    }

    private void OnSvMove(object sender, MouseEventArgs e)
    {
        if (_draggingField)
        {
            PickField(e.GetPosition(SvArea));
        }
    }

    private void PickField(Point p) =>
        SetHsv(_h, p.X / Math.Max(1, SvArea.ActualWidth), 1 - (p.Y / Math.Max(1, SvArea.ActualHeight)));

    private void OnAreaUp(object sender, MouseButtonEventArgs e)
    {
        _draggingField = false;
        SvArea.ReleaseMouseCapture();
    }

    // ---- hue and opacity sliders ----------------------------------------------------

    private void BuildSliders()
    {
        _hueSlider = new PillSlider(HueHost, checkered: false, f => SetHsv(f * 359.999, _s, _v));
        _hueSlider.Background(Enumerable.Range(0, 7).Select(i => ColorSpace.FromHsv(i * 60, 1, 1)).ToArray());
        _alphaSlider = new PillSlider(AlphaHost, checkered: true, f =>
        {
            _a = (byte)Math.Round(Math.Clamp(f, 0, 1) * 255);
            Refresh();
        });
    }

    /// <summary>A rounded gradient track with a round thumb that shows the colour at that point.</summary>
    private sealed class PillSlider
    {
        private readonly Grid _host;
        private readonly Border _track = new() { CornerRadius = new CornerRadius(8) };
        private readonly Canvas _layer = new() { ClipToBounds = false };
        private readonly Ellipse _thumb = new()
        {
            Width = 20,
            Height = 20,
            Stroke = Brushes.White,
            StrokeThickness = 3,
            Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 0, Opacity = 0.55 },
            IsHitTestVisible = false,
        };

        private readonly Action<double> _changed;
        private double _fraction;
        private bool _dragging;

        public PillSlider(Grid host, bool checkered, Action<double> changed)
        {
            _host = host;
            _changed = changed;
            var frame = new Border { CornerRadius = new CornerRadius(8), Background = checkered ? CheckerBrush(8) : null, Child = _track };
            host.Children.Add(frame);
            host.Children.Add(_layer);
            _layer.Children.Add(_thumb);
            host.Background = Brushes.Transparent;
            host.Cursor = Cursors.Hand;
            host.MouseLeftButtonDown += (_, e) => { _dragging = true; host.CaptureMouse(); Pick(e.GetPosition(host)); };
            host.MouseMove += (_, e) => { if (_dragging) { Pick(e.GetPosition(host)); } };
            host.MouseLeftButtonUp += (_, _) => { _dragging = false; host.ReleaseMouseCapture(); };
            host.SizeChanged += (_, _) => Place();
        }

        public void Background(ColorRgba[] stops)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            for (int i = 0; i < stops.Length; i++)
            {
                brush.GradientStops.Add(new GradientStop(ToWpf(stops[i]), i / (double)Math.Max(1, stops.Length - 1)));
            }

            _track.Background = brush;
        }

        public void Set(double fraction, ColorRgba colour)
        {
            _fraction = Math.Clamp(fraction, 0, 1);
            _thumb.Fill = new SolidColorBrush(ToWpf(colour with { A = 255 }));
            Place();
        }

        private void Place()
        {
            // The thumb stays within the track's rounded ends.
            double usable = Math.Max(0, _host.ActualWidth - 16);
            Canvas.SetLeft(_thumb, 8 + (_fraction * usable) - 10);
            Canvas.SetTop(_thumb, (_host.ActualHeight / 2) - 10);
        }

        private void Pick(Point p) => _changed(Math.Clamp((p.X - 8) / Math.Max(1, _host.ActualWidth - 16), 0, 1));
    }

    // ---- number fields ------------------------------------------------------------

    private void BuildFields()
    {
        ColorRgba C() => Current;

        _fields.Add(new SpinField(this, () => Hsb ? "Hue °" : "Red", () => Hsb ? 360 : 255,
            () => Hsb ? _h : C().R, v => { if (Hsb) { SetHsv(v, _s, _v); } else { SetRgb(C() with { R = (byte)v }); } }));
        _fields.Add(new SpinField(this, () => Hsb ? "Sat. %" : "Green", () => Hsb ? 100 : 255,
            () => Hsb ? _s * 100 : C().G, v => { if (Hsb) { SetHsv(_h, v / 100, _v); } else { SetRgb(C() with { G = (byte)v }); } }));
        _fields.Add(new SpinField(this, () => Hsb ? "Bright. %" : "Blue", () => Hsb ? 100 : 255,
            () => Hsb ? _v * 100 : C().B, v => { if (Hsb) { SetHsv(_h, _s, v / 100); } else { SetRgb(C() with { B = (byte)v }); } }));
        _fields.Add(new SpinField(this, () => "Opacity %", () => 100,
            () => _a / 2.55, v => { _a = (byte)Math.Clamp(Math.Round(v * 2.55), 0, 255); Refresh(); }));

        for (int i = 0; i < _fields.Count; i++)
        {
            Fields.Children.Add(_fields[i].Build(i == _fields.Count - 1));
        }
    }

    private void OnModeChanged(object sender, RoutedEventArgs e) => Refresh();

    /// <summary>A number with small up / down arrows and its name underneath. Wheel and arrow keys step it; Shift steps by 10.</summary>
    private sealed class SpinField
    {
        private readonly ColorPickerWindow _owner;
        private readonly Func<string> _caption;
        private readonly Func<double> _max;
        private readonly Func<double> _get;
        private readonly Action<double> _set;
        private readonly TextBox _box = new()
        {
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };

        private readonly TextBlock _label = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };

        public SpinField(ColorPickerWindow owner, Func<string> caption, Func<double> max, Func<double> get, Action<double> set)
        {
            _owner = owner;
            _caption = caption;
            _max = max;
            _get = get;
            _set = set;
        }

        public UIElement Build(bool last)
        {
            _box.SetResourceReference(Control.StyleProperty, "PlainTextBox");
            _label.SetResourceReference(TextBlock.ForegroundProperty, "Br.TextMuted");

            var arrows = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) };
            arrows.Children.Add(Arrow("M 0,4 L 4,0 L 8,4", 1));
            arrows.Children.Add(Arrow("M 0,0 L 4,4 L 8,0", -1));

            var inner = new DockPanel { Height = 34 };
            DockPanel.SetDock(arrows, Dock.Right);
            inner.Children.Add(arrows);
            inner.Children.Add(_box);

            var frame = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Child = inner,
            };
            frame.SetResourceReference(Border.BackgroundProperty, "Br.Surface");
            frame.SetResourceReference(Border.BorderBrushProperty, "Br.Border");
            _box.GotKeyboardFocus += (_, _) =>
            {
                frame.SetResourceReference(Border.BorderBrushProperty, "Br.Accent");
                _box.SelectAll();
            };
            _box.LostKeyboardFocus += (_, _) =>
            {
                frame.SetResourceReference(Border.BorderBrushProperty, "Br.Border");
                Commit();
            };
            _box.PreviewKeyDown += (_, e) =>
            {
                double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
                switch (e.Key)
                {
                    case Key.Enter:
                        Commit();
                        _box.SelectAll();
                        e.Handled = true;
                        break;
                    case Key.Up:
                        Step(step);
                        e.Handled = true;
                        break;
                    case Key.Down:
                        Step(-step);
                        e.Handled = true;
                        break;
                }
            };
            // Preview: the text box's scroll viewer marks the wheel handled before it bubbles up.
            frame.PreviewMouseWheel += (_, e) =>
            {
                Step((e.Delta > 0 ? 1 : -1) * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1));
                e.Handled = true;
            };

            var column = new StackPanel { Margin = new Thickness(0, 0, last ? 0 : 8, 0) };
            column.Children.Add(frame);
            column.Children.Add(_label);
            return column;
        }

        private RepeatButton Arrow(string glyph, int direction)
        {
            var path = new Path { Data = Geometry.Parse(glyph), StrokeThickness = 1.4, Margin = new Thickness(0, 2, 0, 2) };
            path.SetResourceReference(Shape.StrokeProperty, "Br.TextMuted");
            var button = new RepeatButton
            {
                Width = 16,
                Height = 14,
                Focusable = false,
                Cursor = Cursors.Hand,
                Delay = 350,
                Interval = 70,
                Template = ArrowTemplate(),
                Content = path,
            };
            button.Click += (_, _) => Step(direction);
            return button;
        }

        private static ControlTemplate ArrowTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            return new ControlTemplate(typeof(RepeatButton)) { VisualTree = border };
        }

        /// <summary>
        /// Arrows, wheel and arrow keys. The box is refreshed even while it has focus: it used to
        /// keep showing the old number, and leaving the box then typed that number back in.
        /// </summary>
        private void Step(double amount)
        {
            _set(Math.Clamp(Math.Round(_get()) + amount, 0, _max()));
            _box.Text = Math.Round(_get()).ToString(CultureInfo.CurrentCulture);
            if (_box.IsKeyboardFocusWithin)
            {
                _box.SelectAll();
            }
        }

        private void Commit()
        {
            if (_owner._updating)
            {
                return;
            }

            if (double.TryParse(_box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
                double.TryParse(_box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                _set(Math.Clamp(Math.Round(value), 0, _max()));
            }
            else
            {
                Refresh();
            }
        }

        public void Refresh()
        {
            _label.Text = _caption();
            if (!_box.IsKeyboardFocusWithin)
            {
                _box.Text = Math.Round(_get()).ToString(CultureInfo.CurrentCulture);
            }
        }
    }

    // ---- hex, swatches, buttons ------------------------------------------------------

    private void OnHexCommitted(object sender, RoutedEventArgs e)
    {
        if (ColorRgba.TryParse(HexBox.Text, out ColorRgba c))
        {
            SetRgb(c);
        }
        else
        {
            HexBox.Text = Current.ToHex().TrimStart('#');
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

    private void OnSwatchTab(object sender, RoutedEventArgs e)
    {
        bool recent = TabRecent.IsChecked == true;
        Palette.Visibility = recent ? Visibility.Collapsed : Visibility.Visible;
        RecentPanel.Visibility = recent ? Visibility.Visible : Visibility.Collapsed;
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

            var dot = new Grid { Width = 26, Height = 26, Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, ToolTip = c.ToHex() };
            dot.Children.Add(new Ellipse { Fill = CheckerBrush(6) });
            var fill = new Ellipse { Fill = new SolidColorBrush(ToWpf(c)), StrokeThickness = 1 };
            fill.SetResourceReference(Shape.StrokeProperty, "Br.BorderStrong");
            dot.Children.Add(fill);
            dot.MouseLeftButtonUp += (_, _) => SetRgb(c);
            RecentRow.Items.Add(dot);
        }

        RecentEmpty.Visibility = RecentRow.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        TabRecent.IsChecked = true;
        OnSwatchTab(sender, e);
    }

    private void OnOldClicked(object sender, MouseButtonEventArgs e) => SetRgb(_old);

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

    private static Brush CheckerBrush(double cell)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, cell * 2, cell * 2))));
        var dark = new SolidColorBrush(Color.FromRgb(0xCC, 0xD2, 0xDC));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, cell, cell))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(cell, cell, cell, cell))));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, cell * 2, cell * 2),
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
