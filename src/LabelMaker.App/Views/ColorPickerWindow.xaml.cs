using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
/// theme and handles opacity. The gradient editor is in ColorPickerWindow.Gradient.cs.
/// </summary>
public partial class ColorPickerWindow : Window
{
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

        PreviewChecker.Fill = ColourBrushes.Checker(8);
        OldHalf.Fill = gradient?.To is { } oldTo
            ? ColourBrushes.Gradient(initial, oldTo, gradient.AngleDegrees)
            : new SolidColorBrush(ColourBrushes.ToWpf(initial));

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
                NewHalf.Fill = ColourBrushes.Gradient(_stops[0], _stops[1], _angle);
            }
            else
            {
                NewHalf.Fill = new SolidColorBrush(ColourBrushes.ToWpf(c));
            }

            _hueLayer.Fill = new SolidColorBrush(ColourBrushes.ToWpf(ColorSpace.FromHsv(_h, 1, 1)));

            double w = SvArea.ActualWidth, h = SvArea.ActualHeight;
            if (w > 0 && h > 0)
            {
                Canvas.SetLeft(_fieldMarker, (_s * w) - 9);
                Canvas.SetTop(_fieldMarker, ((1 - _v) * h) - 9);
                _fieldMarker.Fill = new SolidColorBrush(ColourBrushes.ToWpf(c with { A = 255 }));
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

    // ---- hue and opacity sliders, number fields ----------------------------------------

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

    private void BuildFields()
    {
        ColorRgba C() => Current;
        bool Busy() => _updating;

        _fields.Add(new SpinField(Busy, () => Hsb ? "Hue °" : "Red", () => Hsb ? 360 : 255,
            () => Hsb ? _h : C().R, v => { if (Hsb) { SetHsv(v, _s, _v); } else { MakeVisible(); SetRgb(C() with { R = (byte)v }); } }));
        _fields.Add(new SpinField(Busy, () => Hsb ? "Sat. %" : "Green", () => Hsb ? 100 : 255,
            () => Hsb ? _s * 100 : C().G, v => { if (Hsb) { SetHsv(_h, v / 100, _v); } else { MakeVisible(); SetRgb(C() with { G = (byte)v }); } }));
        _fields.Add(new SpinField(Busy, () => Hsb ? "Bright. %" : "Blue", () => Hsb ? 100 : 255,
            () => Hsb ? _v * 100 : C().B, v => { if (Hsb) { SetHsv(_h, _s, v / 100); } else { MakeVisible(); SetRgb(C() with { B = (byte)v }); } }));
        _fields.Add(new SpinField(Busy, () => "Opacity %", () => 100,
            () => _a / 2.55, v => { _a = (byte)Math.Clamp(Math.Round(v * 2.55), 0, 255); Refresh(); }));

        for (int i = 0; i < _fields.Count; i++)
        {
            Fields.Children.Add(_fields[i].Build(i == _fields.Count - 1));
        }
    }

    private void OnModeChanged(object sender, RoutedEventArgs e) => Refresh();

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
            dot.Children.Add(new Ellipse { Fill = ColourBrushes.Checker(6) });
            var fill = new Ellipse { Fill = new SolidColorBrush(ColourBrushes.ToWpf(c)), StrokeThickness = 1 };
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
            ColourMemory.Remember(_recent, c.ToHex(), ColourMemory.MaxRecentColours);
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

        if (ScreenPixel.UnderPointer() is { } picked)
        {
            SetRgb(picked with { A = _a == 0 ? (byte)255 : _a });
        }
    }
}

/// <summary>
/// What the colour window starts from when the colour can also be a gradient: the end
/// colour (null for a single colour), the direction, and the list of recently used gradients.
/// </summary>
public sealed record GradientStart(ColorRgba? To, double AngleDegrees, IList<string> Recent);
