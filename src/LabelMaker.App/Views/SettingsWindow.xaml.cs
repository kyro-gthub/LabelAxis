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
        (s.DarkTheme ? ThemeDarkBox : ThemeLightBox).IsChecked = true;
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
        bool wasDark = s.DarkTheme;
        bool nowDark = ThemeDarkBox.IsChecked == true;

        _vm.ApplyTheme(nowDark);
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
        if (nowDark != wasDark)
        {
            _vm.StatusText = nowDark ? "Dark theme on" : "Light theme on";
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
