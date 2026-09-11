using System.Globalization;
using System.Windows;
using LabelMaker.App.ViewModels;
using LabelMaker.Core.Settings;

namespace LabelMaker.App.Views;

/// <summary>Advanced application settings: appearance/motion, canvas defaults, auto-save, and file history.</summary>
public partial class SettingsWindow : Window
{
    private readonly Services.AppEnvironment _env;
    private readonly MainViewModel _vm;

    public SettingsWindow(Services.AppEnvironment env, MainViewModel vm)
    {
        InitializeComponent();
        _env = env;
        _vm = vm;

        AppSettings s = env.Settings;
        ReduceMotionBox.IsChecked = s.ReduceMotion;
        SnapPixelsBox.Text = s.SnapPixels.ToString("0.#", CultureInfo.InvariantCulture);
        GridSizeBox.Text = s.GridSizeMm.ToString("0.##", CultureInfo.InvariantCulture);
        AutoSaveEnabledBox.IsChecked = s.AutoSaveEnabled;
        AutoSaveSecondsBox.Text = s.AutoSaveSeconds.ToString(CultureInfo.InvariantCulture);
        MaxRecentBox.Text = s.MaxRecentProjects.ToString(CultureInfo.InvariantCulture);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        AppSettings s = _env.Settings;

        _vm.ReduceMotion = ReduceMotionBox.IsChecked == true;
        s.SnapPixels = Math.Clamp(ParseDouble(SnapPixelsBox.Text, s.SnapPixels), 1, 50);
        s.GridSizeMm = Math.Clamp(ParseDouble(GridSizeBox.Text, s.GridSizeMm), 0.1, 50);
        s.AutoSaveEnabled = AutoSaveEnabledBox.IsChecked == true;
        s.AutoSaveSeconds = Math.Max(30, ParseInt(AutoSaveSecondsBox.Text, s.AutoSaveSeconds));
        s.MaxRecentProjects = Math.Clamp(ParseInt(MaxRecentBox.Text, s.MaxRecentProjects), 1, 50);

        _env.SaveSettings();
        _vm.NotifyAutoSaveSettingsChanged();

        DialogResult = true;
    }

    private static double ParseDouble(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
}
