using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LabelMaker.App.Localization;
using LabelMaker.App.Services;
using LabelMaker.Core.Model;
using LabelMaker.Core.Settings;

namespace LabelMaker.App.Views;

/// <summary>
/// Makes or changes the user's own theme. Every change is shown at once on the whole app;
/// Cancel puts back the theme that was in use, Save keeps the new one.
/// </summary>
public partial class CustomThemeWindow : Window
{
    private readonly CustomThemeSettings _theme;
    private readonly CustomThemeSettings? _before;
    private readonly string _beforeId;
    private readonly IList<string>? _recent;
    private readonly DispatcherTimer _preview;
    private readonly List<(string Label, Func<string?> Get, Action<string?> Set, bool Optional, Border Swatch, TextBlock Hex)> _rows = new();
    private bool _ready;

    public CustomThemeWindow(IList<string>? recentColours)
    {
        InitializeComponent();
        _recent = recentColours;
        _before = ThemeManager.Custom?.Clone();
        _beforeId = ThemeManager.CurrentId;
        _theme = _before?.Clone() ?? FromCurrent();
        RemoveButton.Visibility = _before is null ? Visibility.Collapsed : Visibility.Visible;

        NameBox.Text = _theme.Name;
        BaseBox.ItemsSource = ThemeManager.Themes.Where(t => t.Id != ThemeManager.CustomId).ToList();
        BaseBox.SelectedItem = ((IEnumerable<ThemeInfo>)BaseBox.ItemsSource).FirstOrDefault(t => t.Id == _theme.BasedOn);
        AddRow("Main colour", () => _theme.Accent, v => _theme.Accent = v!, optional: false);
        AddRow("Second colour", () => _theme.AccentSecondary, v => _theme.AccentSecondary = v!, optional: false);
        AddRow("Third colour", () => _theme.AccentTertiary, v => _theme.AccentTertiary = v!, optional: false);
        AddRow("Background", () => _theme.Background, v => _theme.Background = v, optional: true);
        GlowSlider.Value = Math.Round(_theme.Glow * 100);

        // Shown a moment after the last change, so dragging the slider stays smooth.
        _preview = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _preview.Tick += (_, _) =>
        {
            _preview.Stop();
            Preview();
        };
        NameBox.TextChanged += (_, _) => Changed();
        _ready = true;
        Loaded += (_, _) => Preview();
        Closed += (_, _) =>
        {
            if (DialogResult != true)
            {
                // Cancelled: the theme as it was.
                ThemeManager.SetCustom(_before);
                ThemeManager.Apply(_beforeId);
            }
        };
    }

    /// <summary>The theme made, when the window closes with Save; null when it was removed.</summary>
    public CustomThemeSettings? Result { get; private set; }

    /// <summary>A first theme like the one in use, to change from.</summary>
    private static CustomThemeSettings FromCurrent()
    {
        ThemeInfo current = ThemeManager.Themes.FirstOrDefault(t => t.Id == ThemeManager.CurrentId) ?? ThemeManager.Themes[0];
        static string Hex(Color c) => new ColorRgba(c.R, c.G, c.B).ToHex();
        return new CustomThemeSettings
        {
            Name = Localizer.T("My theme"),
            BasedOn = current.Id,
            Accent = Hex(current.Accent),
            AccentSecondary = Hex(current.AccentSecondary),
            AccentTertiary = Hex(current.AccentTertiary),
        };
    }

    private void AddRow(string label, Func<string?> get, Action<string?> set, bool optional)
    {
        var swatch = new Border { Width = 34, Height = 24, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0) };
        swatch.SetResourceReference(Border.BorderBrushProperty, "Br.BorderStrong");
        var hex = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas"), FontSize = 12 };
        Localizer.SetOff(hex, true);
        var change = new Button { Content = Localizer.T("Change…"), Padding = new Thickness(10, 3, 10, 3) };
        change.SetResourceReference(StyleProperty, "GhostButton");
        var line = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var name = new TextBlock { Text = Localizer.T(label), Width = 118, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(name, Dock.Left);
        DockPanel.SetDock(swatch, Dock.Left);
        DockPanel.SetDock(change, Dock.Right);
        line.Children.Add(name);
        line.Children.Add(swatch);
        line.Children.Add(change);
        if (optional)
        {
            var reset = new Button { Content = Localizer.T("As the theme"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
            reset.SetResourceReference(StyleProperty, "GhostButton");
            reset.Click += (_, _) =>
            {
                set(null);
                Changed();
            };
            DockPanel.SetDock(reset, Dock.Right);
            line.Children.Add(reset);
        }

        line.Children.Add(hex);
        change.Click += (_, _) =>
        {
            ColorRgba.TryParse(get() ?? ThemeBackground(), out ColorRgba now);
            var picker = new ColorPickerWindow(now, Localizer.T(label), _recent) { Owner = this };
            if (picker.ShowDialog() == true)
            {
                ColorRgba chosen = picker.Result;
                set(new ColorRgba(chosen.R, chosen.G, chosen.B).ToHex()); // themes are solid colours
                Changed();
            }
        };

        _rows.Add((label, get, set, optional, swatch, hex));
        ColourRows.Children.Add(line);
    }

    private string ThemeBackground()
    {
        ThemeInfo? start = ThemeManager.Themes.FirstOrDefault(t => t.Id == _theme.BasedOn);
        Color c = start?.Background ?? Colors.White;
        return new ColorRgba(c.R, c.G, c.B).ToHex();
    }

    private void OnBase(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && BaseBox.SelectedItem is ThemeInfo start)
        {
            _theme.BasedOn = start.Id;
            Changed();
        }
    }

    private void OnGlow(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        GlowText.Text = Math.Round(GlowSlider.Value).ToString(CultureInfo.CurrentCulture) + "%";
        if (_ready)
        {
            _theme.Glow = GlowSlider.Value / 100;
            Changed();
        }
    }

    private void Changed()
    {
        _theme.Name = NameBox.Text;
        RefreshRows();
        _preview.Stop();
        _preview.Start();
    }

    private void RefreshRows()
    {
        foreach ((string _, Func<string?> get, _, bool optional, Border swatch, TextBlock hex) in _rows)
        {
            string? value = get();
            string shown = value ?? ThemeBackground();
            ColorRgba.TryParse(shown, out ColorRgba c);
            swatch.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            hex.Text = value is null && optional ? Localizer.F("{0} (the theme's own)", shown) : shown;
        }

        // The main colour has to stand out from the background for buttons and selections to show.
        ColorRgba.TryParse(_theme.Accent, out ColorRgba accent);
        ColorRgba.TryParse(_theme.Background ?? ThemeBackground(), out ColorRgba bg);
        double contrast = ThemeManager.Contrast(Color.FromRgb(accent.R, accent.G, accent.B), Color.FromRgb(bg.R, bg.G, bg.B));
        WarningText.Text = contrast < 2.2
            ? Localizer.T("The main colour is hard to see on this background: choose a darker or a lighter one.")
            : string.Empty;
    }

    private void Preview()
    {
        RefreshRows();
        ThemeManager.SetCustom(_theme);
        ThemeManager.Apply(ThemeManager.CustomId);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _theme.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? Localizer.T("My theme") : NameBox.Text.Trim();
        Result = _theme.Clone();
        Removed = false;
        DialogResult = true;
    }

    /// <summary>True when the window closed because the theme was removed.</summary>
    public bool Removed { get; private set; }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        Result = null;
        Removed = true;
        DialogResult = true;
    }
}
