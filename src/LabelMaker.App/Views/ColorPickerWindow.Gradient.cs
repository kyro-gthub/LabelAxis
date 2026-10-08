using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LabelMaker.App.Controls;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Views;

/// <summary>The colour window's gradient editor: two colour stops, a direction, and saved gradients.</summary>
public partial class ColorPickerWindow
{
    // Gradient: its two colours, the one the controls are editing, and its direction.
    private readonly IList<string>? _recentGradients;
    private readonly ColorRgba[] _stops = { ColorRgba.Black, ColorRgba.White };
    private readonly Ellipse[] _stopFills = new Ellipse[2];
    private readonly Ellipse[] _stopRings = new Ellipse[2];
    private readonly List<(RadioButton Button, double Angle)> _angleButtons = new();
    private bool _gradient;
    private int _activeStop;
    private double _angle = 90;

    private ColorRgba? _oldTo;
    private double _oldAngle = 90;

    private void BuildGradientEditor(ColorRgba initial, GradientStart start)
    {
        _oldTo = start.To;
        _oldAngle = start.AngleDegrees;
        _angle = ColourMemory.NormaliseAngle(start.AngleDegrees);
        KindBar.Visibility = Visibility.Visible;
        TabGradients.Visibility = Visibility.Visible;

        for (int i = 0; i < 2; i++)
        {
            Grid host = i == 0 ? StartStop : EndStop;
            host.Background = Brushes.Transparent;
            host.Children.Add(new Ellipse { Fill = ColourBrushes.Checker(5), Margin = new Thickness(3) });
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
                from = ColorRgba.Parse(ColourMemory.PresetGradients[0].From);
            }

            _stops[0] = from;
            _stops[1] = ColourMemory.PartnerOf(from);
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
        _angle = ColourMemory.NormaliseAngle(angle);
        SetRgb(_stops[_activeStop]);
    }

    private void SetAngle(double angle)
    {
        _angle = ColourMemory.NormaliseAngle(angle);
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
        GradientBar.Background = ColourBrushes.Gradient(_stops[0], _stops[1], 0);
        GradientFrame.Background = ColourBrushes.Checker(5);
        for (int i = 0; i < 2; i++)
        {
            _stopFills[i].Fill = new SolidColorBrush(ColourBrushes.ToWpf(_stops[i]));
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
            if (ColourMemory.ParseGradient(saved) is { } g)
            {
                entries.Add((g.From, g.To, g.Angle, Localization.Localizer.T("Used recently")));
            }
        }

        foreach ((string name, string from, string to) in ColourMemory.PresetGradients)
        {
            entries.Add((ColorRgba.Parse(from), ColorRgba.Parse(to), null, Localization.Localizer.T(name)));
        }

        foreach ((ColorRgba from, ColorRgba to, double? angle, string tip) in entries)
        {
            var chip = new Grid { Width = 50, Height = 26, Margin = new Thickness(0, 0, 6, 8), Cursor = Cursors.Hand, ToolTip = tip };
            chip.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = ColourBrushes.Checker(5) });
            var fill = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Background = ColourBrushes.Gradient(from, to, 0) };
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
            ColourMemory.Remember(_recentGradients, ColourMemory.FormatGradient(_stops[0], _stops[1], _angle), ColourMemory.MaxRecentGradients);
        }
    }
}
