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

    // Gradient: its two colours, the one the controls below are editing, and its direction.
    private readonly IList<string>? _recentGradients;
    private readonly ColorRgba[] _stops = { ColorRgba.Black, ColorRgba.White };
    private readonly Ellipse[] _stopFills = new Ellipse[2];
    private readonly Ellipse[] _stopRings = new Ellipse[2];
    private readonly List<(RadioButton Button, double Angle)> _angleButtons = new();
    private bool _gradient;
    private int _activeStop;
    private double _angle = 90;

    private Rectangle _hueLayer = null!;
    private Ellipse _fieldMarker = null!;
    private PillSlider _hueSlider = null!;
    private PillSlider _alphaSlider = null!;

    public ColorPickerWindow(ColorRgba initial, string title, IList<string>? recent = null)
        : this(initial, title, recent, gradient: null)
    {
    }

    /// <summary>
    /// With <paramref name="gradient"/>, the window can also make a two-colour gradient: it
    /// opens on the gradient when <see cref="GradientStart.To"/> is set, and shows the ready-made
    /// and recently used gradients.
    /// </summary>
    public ColorPickerWindow(ColorRgba initial, string title, IList<string>? recent, GradientStart? gradient)
    {
        InitializeComponent();
        Title = title;
        _old = initial;
        _recent = recent;
        _recentGradients = gradient?.Recent;

        PreviewChecker.Fill = CheckerBrush(8);
        OldHalf.Fill = gradient?.To is { } oldTo
            ? GradientBrush(initial, oldTo, gradient.AngleDegrees)
            : new SolidColorBrush(ToWpf(initial));

        BuildColourField();
        BuildSliders();
        ModeRgb.IsChecked = true;
        BuildFields();
        BuildRecent();
        if (gradient is not null)
        {
            BuildGradientEditor(initial, gradient);
        }

        DependencyPropertyDescriptor.FromProperty(ColorPalette.HexProperty, typeof(ColorPalette))
            .AddValueChanged(Palette, (_, _) =>
            {
                if (!_updating && ColorRgba.TryParse(Palette.Hex, out ColorRgba c))
                {
                    SetRgb(c);
                }
            });

        SetRgb(_gradient ? _stops[0] : initial);
        Result = initial;
        Loaded += (_, _) => Refresh();
    }

    /// <summary>The chosen colour: for a gradient, its start colour.</summary>
    public ColorRgba Result { get; private set; }

    /// <summary>The gradient's end colour, or null when a single colour was chosen.</summary>
    public ColorRgba? ResultGradientTo { get; private set; }

    /// <summary>The gradient's direction, degrees clockwise from left to right.</summary>
    public double ResultAngle => _angle;

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

    /// <summary>
    /// Choosing a colour while opacity is at 0 (as it is for "no colour") would choose an
    /// invisible colour, so picking one by hue, the colour field or the number fields brings
    /// opacity back to full. Opacity itself is then free to be set lower again.
    /// </summary>
    private void MakeVisible()
    {
        if (_a == 0)
        {
            _a = 255;
        }
    }

    private void SetHsv(double h, double s, double v)
    {
        MakeVisible();
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
            if (_gradient)
            {
                _stops[_activeStop] = c;
                RefreshGradient();
                NewHalf.Fill = GradientBrush(_stops[0], _stops[1], _angle);
            }
            else
            {
                NewHalf.Fill = new SolidColorBrush(ToWpf(c));
            }

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
            () => Hsb ? _h : C().R, v => { if (Hsb) { SetHsv(v, _s, _v); } else { MakeVisible(); SetRgb(C() with { R = (byte)v }); } }));
        _fields.Add(new SpinField(this, () => Hsb ? "Sat. %" : "Green", () => Hsb ? 100 : 255,
            () => Hsb ? _s * 100 : C().G, v => { if (Hsb) { SetHsv(_h, v / 100, _v); } else { MakeVisible(); SetRgb(C() with { G = (byte)v }); } }));
        _fields.Add(new SpinField(this, () => Hsb ? "Bright. %" : "Blue", () => Hsb ? 100 : 255,
            () => Hsb ? _v * 100 : C().B, v => { if (Hsb) { SetHsv(_h, _s, v / 100); } else { MakeVisible(); SetRgb(C() with { B = (byte)v }); } }));
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
        bool recent = TabRecent.IsChecked == true, gradients = TabGradients.IsChecked == true;
        Palette.Visibility = recent || gradients ? Visibility.Collapsed : Visibility.Visible;
        RecentPanel.Visibility = recent ? Visibility.Visible : Visibility.Collapsed;
        GradientsPanel.Visibility = gradients ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildRecent()
    {
        RecentRow.Items.Clear();
        foreach (string hex in _recent ?? (IList<string>)Array.Empty<string>())
        {
            // Fully see-through entries saved before could only ever pick "nothing".
            if (!ColorRgba.TryParse(hex, out ColorRgba c) || c.A == 0)
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
        if (_recent is not null && c.A > 0)
        {
            RememberIn(_recent, c.ToHex(), MaxRecent);
        }
    }

    private static void RememberIn(IList<string> list, string entry, int max)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (string.Equals(list[i], entry, StringComparison.OrdinalIgnoreCase))
            {
                list.RemoveAt(i);
            }
        }

        list.Insert(0, entry);
        while (list.Count > max)
        {
            list.RemoveAt(list.Count - 1);
        }
    }

    private void OnAddRecent(object sender, RoutedEventArgs e)
    {
        if (_gradient)
        {
            RememberGradient();
            BuildGradients();
            TabGradients.IsChecked = true;
        }
        else
        {
            Remember(Current);
            BuildRecent();
            TabRecent.IsChecked = true;
        }

        OnSwatchTab(sender, e);
    }

    private void OnOldClicked(object sender, MouseButtonEventArgs e)
    {
        if (_oldTo is { } to && _recentGradients is not null)
        {
            ApplyGradient(_old, to, _oldAngle);
        }
        else
        {
            if (_gradient)
            {
                SetGradientMode(false);
            }

            SetRgb(_old);
        }
    }

    private void OnTransparent(object sender, RoutedEventArgs e)
    {
        Result = ColorRgba.Transparent;
        ResultGradientTo = null;
        DialogResult = true;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (HexBox.IsKeyboardFocusWithin && ColorRgba.TryParse(HexBox.Text, out ColorRgba typed))
        {
            SetRgb(typed);
        }

        if (AngleBox.IsKeyboardFocusWithin)
        {
            OnAngleCommitted(AngleBox, e);
        }

        if (_gradient)
        {
            Result = _stops[0];
            ResultGradientTo = _stops[1];
            RememberGradient();
        }
        else
        {
            Result = Current;
            ResultGradientTo = null;
            Remember(Result);
        }

        DialogResult = true;
    }

    // ---- gradients ---------------------------------------------------------------

    private const int MaxRecentGradients = 12;

    /// <summary>Ready-made gradients: start and end colour, drawn left to right.</summary>
    private static readonly (string Name, string From, string To)[] PresetGradients =
    {
        ("Ocean", "#2C6FE8", "#6FD3F7"),
        ("Violet", "#7B5CFA", "#E16BD8"),
        ("Sunset", "#FF7A59", "#FFC15E"),
        ("Mint", "#1FAF7A", "#B8F1D2"),
        ("Berry", "#D9346C", "#7A2CF0"),
        ("Gold", "#B7862F", "#F6E27A"),
        ("Fire", "#E5322D", "#F7A325"),
        ("Forest", "#17633F", "#6DBE45"),
        ("Peach", "#FFD3A5", "#FD6585"),
        ("Night", "#141E30", "#3B5A86"),
        ("Sky", "#A1C4FD", "#C2E9FB"),
        ("Lime", "#F9F047", "#0FD850"),
        ("Rose", "#F5C6CF", "#B5577D"),
        ("Steel", "#3A4556", "#9AA7B8"),
        ("Silver", "#F2F4F7", "#A9B1BF"),
        ("Fade to clear", "#000000", "#00000000"),
    };

    private ColorRgba? _oldTo;
    private double _oldAngle = 90;

    private void BuildGradientEditor(ColorRgba initial, GradientStart start)
    {
        _oldTo = start.To;
        _oldAngle = start.AngleDegrees;
        _angle = ((start.AngleDegrees % 360) + 360) % 360;
        KindBar.Visibility = Visibility.Visible;
        TabGradients.Visibility = Visibility.Visible;

        for (int i = 0; i < 2; i++)
        {
            Grid host = i == 0 ? StartStop : EndStop;
            host.Background = Brushes.Transparent;
            host.Children.Add(new Ellipse { Fill = CheckerBrush(5), Margin = new Thickness(3) });
            _stopFills[i] = new Ellipse { Margin = new Thickness(3) };
            host.Children.Add(_stopFills[i]);
            _stopRings[i] = new Ellipse { IsHitTestVisible = false };
            host.Children.Add(_stopRings[i]);
        }

        // The eight directions, as arrows pointing the way the colours run.
        string[] arrows = { "→", "↘", "↓", "↙", "←", "↖", "↑", "↗" };
        for (int i = 0; i < arrows.Length; i++)
        {
            double angle = i * 45;
            var button = new RadioButton
            {
                Content = arrows[i],
                GroupName = "angle",
                Margin = new Thickness(0, 0, 2, 0),
                Padding = new Thickness(5, 2, 5, 2),
                MinWidth = 0,
                ToolTip = string.Create(CultureInfo.CurrentCulture, $"{angle:0}°"),
                FontFamily = new FontFamily("Segoe UI Symbol"),
            };
            button.SetResourceReference(StyleProperty, "ChipToggle");
            button.Click += (_, _) => SetAngle(angle);
            _angleButtons.Add((button, angle));
            AngleButtons.Children.Add(button);
        }

        BuildGradients();
        if (start.To is { } to)
        {
            _stops[0] = initial;
            _stops[1] = to;
            SetGradientMode(true, load: false);
        }
    }

    private void OnKindChanged(object sender, RoutedEventArgs e) => SetGradientMode(KindGradient.IsChecked == true);

    /// <summary>Switches between one colour and a gradient; a new gradient starts from the current colour.</summary>
    private void SetGradientMode(bool on, bool load = true)
    {
        if (_recentGradients is null && on)
        {
            return;
        }

        bool was = _gradient;
        KindGradient.IsChecked = on;
        KindSolid.IsChecked = !on;
        GradientPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        SaveLabel.Text = Localization.Localizer.T(on ? "Save gradient" : "Save colour");

        if (on && !was && load)
        {
            ColorRgba from = Current;
            if (from.A == 0)
            {
                from = ColorRgba.Parse(PresetGradients[0].From);
            }

            _stops[0] = from;
            _stops[1] = PartnerOf(from);
            _activeStop = 0;
        }

        _gradient = on;
        if (on)
        {
            TabGradients.IsChecked = true;
            OnSwatchTab(TabGradients, new RoutedEventArgs());
            SetRgb(_stops[_activeStop]);
        }
        else
        {
            if (TabGradients.IsChecked == true)
            {
                TabPalette.IsChecked = true;
                OnSwatchTab(TabPalette, new RoutedEventArgs());
            }

            Refresh();
        }
    }

    /// <summary>A second colour that goes well with <paramref name="c"/>: a neighbouring hue, lighter or darker.</summary>
    private static ColorRgba PartnerOf(ColorRgba c)
    {
        (double h, double s, double v) = ColorSpace.ToHsv(c);
        return v > 0.6
            ? ColorSpace.FromHsv((h + 20) % 360, Math.Min(1, s + 0.15), Math.Max(0, v - 0.35), c.A)
            : ColorSpace.FromHsv((h + 340) % 360, Math.Max(0, s - 0.2), Math.Min(1, v + 0.4), c.A);
    }

    private void OnStartStop(object sender, MouseButtonEventArgs e) => EditStop(0);

    private void OnEndStop(object sender, MouseButtonEventArgs e) => EditStop(1);

    private void EditStop(int stop)
    {
        _activeStop = stop;
        SetRgb(_stops[stop]);
    }

    private void OnReverseGradient(object sender, RoutedEventArgs e) => ApplyGradient(_stops[1], _stops[0], _angle);

    private void ApplyGradient(ColorRgba from, ColorRgba to, double angle)
    {
        SetGradientMode(true, load: false);
        _stops[0] = from;
        _stops[1] = to;
        _angle = ((angle % 360) + 360) % 360;
        SetRgb(_stops[_activeStop]);
    }

    private void SetAngle(double angle)
    {
        _angle = ((angle % 360) + 360) % 360;
        Refresh();
    }

    private void OnAngleCommitted(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(AngleBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double a) ||
            double.TryParse(AngleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out a))
        {
            SetAngle(Math.Round(a));
        }
        else
        {
            AngleBox.Text = _angle.ToString("0", CultureInfo.CurrentCulture);
        }
    }

    private void OnAngleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnAngleCommitted(sender, e);
            AngleBox.SelectAll();
            e.Handled = true;
        }
    }

    private void RefreshGradient()
    {
        GradientBar.Background = GradientBrush(_stops[0], _stops[1], 0);
        GradientFrame.Background = CheckerBrush(5);
        for (int i = 0; i < 2; i++)
        {
            _stopFills[i].Fill = new SolidColorBrush(ToWpf(_stops[i]));
            _stopRings[i].SetResourceReference(Shape.StrokeProperty, i == _activeStop ? "Br.Accent" : "Br.BorderStrong");
            _stopRings[i].StrokeThickness = i == _activeStop ? 2.5 : 1;
        }

        foreach ((RadioButton button, double angle) in _angleButtons)
        {
            button.IsChecked = Math.Abs(angle - _angle) < 0.5;
        }

        if (!AngleBox.IsKeyboardFocusWithin)
        {
            AngleBox.Text = _angle.ToString("0", CultureInfo.CurrentCulture);
        }
    }

    private void BuildGradients()
    {
        GradientRow.Items.Clear();
        var entries = new List<(ColorRgba From, ColorRgba To, double? Angle, string Tip)>();
        foreach (string saved in _recentGradients ?? (IList<string>)Array.Empty<string>())
        {
            if (ParseGradient(saved) is { } g)
            {
                entries.Add((g.From, g.To, g.Angle, Localization.Localizer.T("Used recently")));
            }
        }

        foreach ((string name, string from, string to) in PresetGradients)
        {
            entries.Add((ColorRgba.Parse(from), ColorRgba.Parse(to), null, Localization.Localizer.T(name)));
        }

        foreach ((ColorRgba from, ColorRgba to, double? angle, string tip) in entries)
        {
            var chip = new Grid { Width = 50, Height = 26, Margin = new Thickness(0, 0, 6, 8), Cursor = Cursors.Hand, ToolTip = tip };
            chip.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = CheckerBrush(5) });
            var fill = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Background = GradientBrush(from, to, 0) };
            fill.SetResourceReference(Border.BorderBrushProperty, "Br.BorderStrong");
            chip.Children.Add(fill);
            // A ready-made gradient keeps the direction already chosen; a recent one brings its own.
            chip.MouseLeftButtonUp += (_, _) => ApplyGradient(from, to, angle ?? _angle);
            GradientRow.Items.Add(chip);
        }
    }

    private void RememberGradient()
    {
        if (_recentGradients is not null && !(_stops[0].A == 0 && _stops[1].A == 0))
        {
            RememberIn(_recentGradients, FormatGradient(_stops[0], _stops[1], _angle), MaxRecentGradients);
        }
    }

    /// <summary>How a gradient is kept in the settings: "#start #end angle".</summary>
    public static string FormatGradient(ColorRgba from, ColorRgba to, double angle) =>
        string.Create(CultureInfo.InvariantCulture, $"{from.ToHex()} {to.ToHex()} {angle:0}");

    public static (ColorRgba From, ColorRgba To, double Angle)? ParseGradient(string? text)
    {
        string[] parts = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3 && ColorRgba.TryParse(parts[0], out ColorRgba from) && ColorRgba.TryParse(parts[1], out ColorRgba to) &&
               double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double angle)
            ? (from, to, angle)
            : null;
    }

    /// <summary>The gradient as it is drawn on the label: along the angle, through the box's centre.</summary>
    public static Brush GradientBrush(ColorRgba from, ColorRgba to, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180.0;
        double dx = Math.Cos(radians) / 2.0, dy = Math.Sin(radians) / 2.0;
        var brush = new LinearGradientBrush(ToWpf(from), ToWpf(to), new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy));
        brush.Freeze();
        return brush;
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
            SetRgb(new ColorRgba((byte)(bgr & 0xFF), (byte)((bgr >> 8) & 0xFF), (byte)((bgr >> 16) & 0xFF), _a == 0 ? (byte)255 : _a));
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

/// <summary>
/// What the colour window starts from when the colour can also be a gradient: the end
/// colour (null for a single colour), the direction, and the list of recently used gradients.
/// </summary>
public sealed record GradientStart(ColorRgba? To, double AngleDegrees, IList<string> Recent);
