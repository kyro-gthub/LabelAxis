using System.Globalization;
using System.Windows;
using LabelMaker.App.ViewModels;
using LabelMaker.Core.Settings;

namespace LabelMaker.App.Views;

/// <summary>Advanced application settings: appearance/theme, canvas defaults, editing confirmations,
/// startup behavior, auto-save, and file history.</summary>
public partial class SettingsWindow : Window
{
    private readonly Services.AppEnvironment _env;
    private readonly MainViewModel _vm;

    public SettingsWindow(Services.AppEnvironment env, MainViewModel vm)
    {
        InitializeComponent();
        AboutTitle.Text = $"{Services.AppInfo.Name} {Services.AppInfo.Version}";
        _env = env;
        _vm = vm;

        AppSettings s = env.Settings;
        LanguageBox.ItemsSource = Localization.Localizer.Languages;
        LanguageBox.SelectedItem = Localization.Localizer.Resolve(s.Language);
        ShowThemes();
        ReduceMotionBox.IsChecked = s.ReduceMotion;
        SnapPixelsBox.Text = s.SnapPixels.ToString("0.#", CultureInfo.InvariantCulture);
        GridSizeBox.Text = s.GridSizeMm.ToString("0.##", CultureInfo.InvariantCulture);
        ConfirmDestructiveBox.IsChecked = s.ConfirmDestructiveActions;
        ReopenLastProjectBox.IsChecked = s.ReopenLastProjectOnStart;
        ShowWelcomeBox.IsChecked = s.ShowWelcomeOnStart;
        AutoSaveEnabledBox.IsChecked = s.AutoSaveEnabled;
        AutoSaveSecondsBox.Text = s.AutoSaveSeconds.ToString(CultureInfo.InvariantCulture);
        MaxRecentBox.Text = s.MaxRecentProjects.ToString(CultureInfo.InvariantCulture);

        CheckForUpdatesBox.IsChecked = s.CheckForUpdates;
        UpdateAddressBox.Text = s.UpdateAddress ?? string.Empty;
        string builtIn = Services.AppInfo.BuiltInUpdateAddress;
        string hint = builtIn.Length > 0
            ? Localization.Localizer.F("Leave it empty to use the address built into this version: {0}", builtIn)
            : Localization.Localizer.T("A web address (https://) or a shared folder where update.json and the setup programs are published.");
        if (s.LastUpdateCheckUtc is { } last)
        {
            hint += "\n" + Localization.Localizer.F("Last checked {0}.", last.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        }

        UpdateAddressHint.Text = hint;
        RefreshFonts();
    }

    private void RefreshFonts()
    {
        FontList.ItemsSource = Rendering.CustomFonts.Names
            .Select(name => new FontRow(name, string.Join(", ", Rendering.CustomFonts.FilesOf(name))))
            .ToList();
        RemoveFontButton.IsEnabled = false;
    }

    /// <summary>A row of the font list: the family and its files.</summary>
    private sealed record FontRow(string Family, string Files)
    {
        public override string ToString() => $"{Family}   ({Files})";
    }

    private void OnFontSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        RemoveFontButton.IsEnabled = FontList.SelectedItem is not null;

    // Fonts are added and removed at once, not on Save: the font lists show them straight away.
    private void ShowThemes()
    {
        ThemeList.ItemsSource = Services.ThemeManager.Themes;
        ThemeList.SelectedItem = Services.ThemeManager.Themes.FirstOrDefault(t => t.Id == Services.ThemeManager.CurrentId);
    }

    /// <summary>The user's own theme: made in its own window, shown at once, then listed here.</summary>
    private void OnCustomTheme(object sender, RoutedEventArgs e)
    {
        _vm.EditCustomTheme();
        ShowThemes();
    }

    private void OnAddFont(object sender, RoutedEventArgs e)
    {
        if (_vm.AddFontFromFile() is { } family)
        {
            RefreshFonts();
            StatusText.Text = Localization.Localizer.F("Font “{0}” added", family);
        }
    }

    private void OnRemoveFont(object sender, RoutedEventArgs e)
    {
        if (FontList.SelectedItem is not FontRow row ||
            MessageBox.Show(this, Localization.Localizer.F("Remove the font “{0}” from LabelAxis? Labels that use it are drawn in another font until it is added again.", row.Family),
                "LabelAxis", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        Rendering.CustomFonts.Remove(row.Family);
        _vm.RedrawCanvas();
        RefreshFonts();
        StatusText.Text = Localization.Localizer.F("Font “{0}” removed", row.Family);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // Checked first, so a bad address changes nothing.
        string address = UpdateAddressBox.Text.Trim();
        if (address.Length > 0)
        {
            try
            {
                LabelMaker.Core.Updates.UpdateSource.ToFeedUri(address);
            }
            catch (Exception ex) when (ex is FormatException or UriFormatException or ArgumentException or NotSupportedException or System.IO.PathTooLongException)
            {
                StatusText.Text = Localization.Localizer.T("The update address is not usable:") + " " + ex.Message;
                UpdateAddressBox.Focus();
                return;
            }
        }

        AppSettings s = _env.Settings;
        string wasTheme = Services.ThemeManager.CurrentId;
        string nowTheme = (ThemeList.SelectedItem as Services.ThemeInfo)?.Id ?? wasTheme;

        if (nowTheme != wasTheme)
        {
            _vm.ApplyTheme(nowTheme);
        }
        _vm.ReduceMotion = ReduceMotionBox.IsChecked == true;
        s.SnapPixels = Math.Clamp(ParseDouble(SnapPixelsBox.Text, s.SnapPixels), 1, 50);
        s.GridSizeMm = Math.Clamp(ParseDouble(GridSizeBox.Text, s.GridSizeMm), 0.1, 50);
        s.ConfirmDestructiveActions = ConfirmDestructiveBox.IsChecked == true;
        s.ReopenLastProjectOnStart = ReopenLastProjectBox.IsChecked == true;
        s.ShowWelcomeOnStart = ShowWelcomeBox.IsChecked == true;
        s.AutoSaveEnabled = AutoSaveEnabledBox.IsChecked == true;
        s.AutoSaveSeconds = Math.Max(30, ParseInt(AutoSaveSecondsBox.Text, s.AutoSaveSeconds));
        s.MaxRecentProjects = Math.Clamp(ParseInt(MaxRecentBox.Text, s.MaxRecentProjects), 1, 50);

        s.CheckForUpdates = CheckForUpdatesBox.IsChecked == true;
        if (!string.Equals(s.UpdateAddress ?? string.Empty, address, StringComparison.Ordinal))
        {
            // A new place may hold a version skipped at the old one.
            s.SkippedUpdateVersion = null;
            s.LastUpdateCheckUtc = null;
        }

        s.UpdateAddress = address.Length > 0 ? address : null;

        if (LanguageBox.SelectedItem is Localization.AppLanguage language)
        {
            LanguageChanged = language.Code != Localization.Localizer.Current.Code;
            s.Language = language.Code;
        }

        _env.SaveSettings();
        _vm.NotifyAutoSaveSettingsChanged();

        // The palette is swapped in place (see ThemeManager), so the new theme shows at once.
        if (nowTheme != wasTheme)
        {
            _vm.StatusText = Localization.Localizer.F("Theme: {0}", _vm.ThemeName);
        }

        DialogResult = true;
    }

    /// <summary>True when Save chose a language other than the one on screen (it shows after a restart).</summary>
    public bool LanguageChanged { get; private set; }

    private static double ParseDouble(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
}
