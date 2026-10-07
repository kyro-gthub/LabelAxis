using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using LabelMaker.App.Services;
using LabelMaker.App.Views;
using LabelMaker.Core.Serialization;

namespace LabelMaker.App;

public partial class App : Application
{
    public AppEnvironment Environment { get; private set; } = null!;

    /// <summary>
    /// PDF export gets the fonts that come with LabelAxis, and WPF's text engine for Arabic and
    /// Hebrew, which PdfSharp cannot join up or order right to left on its own.
    /// </summary>
    internal static void RegisterFontsForExport()
    {
        foreach ((string family, bool bold, byte[] data) in Rendering.BundledFonts.Files())
        {
            LabelMaker.Export.Pdf.WindowsFontResolver.AddPrivateFont(family, bold, data);
        }

        LabelMaker.Export.Pdf.PdfDrawingSurface.ComplexTextOutliner = new Rendering.WpfTextOutliner();
    }

    private static string? _dataDirectory;

    /// <summary>The folder after <c>--data-dir</c> on the command line, if any.</summary>
    private static string? DataDirectoryArgument(string[] args)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, "--data-dir", StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? Path.GetFullPath(args[i + 1]) : null;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _dataDirectory = DataDirectoryArgument(e.Args);
        Environment = new AppEnvironment(_dataDirectory);
        Localization.Localizer.Start(Localization.Localizer.Resolve(Environment.Settings.Language));
        ThemeManager.SetCustom(Environment.Settings.CustomTheme);
        ThemeManager.Apply(ThemeManager.Resolve(Environment.Settings.Theme, Environment.Settings.DarkTheme));
        Rendering.CustomFonts.Load(Environment.Paths.FontsDirectory);
        RegisterFontsForExport();

        // A class handler catches every Window, derived types included -- an implicit
        // style would not, which is the same trap that left the dialogs light earlier.
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ThemeManager.ApplyTitleBar((Window)sender)));

        DispatcherUnhandledException += OnUnhandledException;
        HelpGuide.HookF1();

        var window = new MainWindow(Environment);
        MainWindow = window;
        window.Show();

        // Crash recovery prompt (spec §32).
        bool restoredFromRecovery = false;
        IReadOnlyList<RecoveryInfo> recoverable = Environment.AutoSave.FindRecoverable();
        if (recoverable.Count > 0)
        {
            RecoveryInfo info = recoverable[0];
            MessageBoxResult result = MessageBox.Show(
                window,
                $"An unsaved project was recovered:\n\n\"{info.Title}\"\nlast auto-saved {info.SavedUtc.ToLocalTime():g}\n\nRestore it?",
                "LabelAxis — Recovery",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                window.RestoreRecovered(info);
                restoredFromRecovery = true;
            }
            else
            {
                Environment.AutoSave.ClearFor(info.ProjectId);
            }
        }

        string[] projects = e.Args.Where(File.Exists).ToArray();
        if (projects.Length > 0)
        {
            // Each project named on the command line (or several files opened from Explorer) gets its own tab.
            foreach (string path in projects)
            {
                window.OpenProjectPath(path);
            }
        }
        else if (!restoredFromRecovery && Environment.Settings.ReopenLastProjectOnStart &&
                 Environment.Settings.RecentProjects.FirstOrDefault() is { } last && File.Exists(last.Path))
        {
            window.OpenProjectPath(last.Path);
        }
        else if (!restoredFromRecovery && Environment.Settings.ShowWelcomeOnStart)
        {
            // After the main window has drawn, so the welcome window sits over it.
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, window.ViewModel.ShowWelcome);
        }

        // The daily look for a newer version, a little after start-up so it never slows it down.
        var updateCheck = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(12) };
        updateCheck.Tick += async (_, _) =>
        {
            updateCheck.Stop();
            await window.ViewModel.CheckForUpdatesInBackgroundAsync();
        };
        updateCheck.Start();
    }

    private System.Diagnostics.ProcessStartInfo? _runOnExit;

    /// <summary>
    /// Closes LabelAxis and starts it again (after a change of language). The main window asks
    /// about unsaved work first as usual; if that is cancelled, nothing happens.
    /// </summary>
    internal static void Restart()
    {
        if (System.Environment.ProcessPath is { } exe)
        {
            // A copy started with its own data folder (the click-through tests) keeps it.
            var next = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
            if (_dataDirectory is { } data)
            {
                next.ArgumentList.Add("--data-dir");
                next.ArgumentList.Add(data);
            }

            QuitAndRun(next);
        }
    }

    /// <summary>
    /// Closes LabelAxis, then starts <paramref name="next"/> once settings are saved: LabelAxis
    /// itself again, or an update's setup program. Cancelling the save question stops it.
    /// </summary>
    internal static void QuitAndRun(System.Diagnostics.ProcessStartInfo next)
    {
        if (Current is not App app || app.MainWindow is not { } main)
        {
            return;
        }

        app._runOnExit = next;
        main.Close();
        if (main.IsVisible)
        {
            app._runOnExit = null;
            return;
        }

        app.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Environment?.SaveSettings();
        if (_runOnExit is { } next)
        {
            try
            {
                System.Diagnostics.Process.Start(next);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Could not start it (or the user declined the administrator prompt).
            }
        }

        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            string logDir = Environment.Paths.LogDirectory;
            Directory.CreateDirectory(logDir);
            File.AppendAllText(
                Path.Combine(logDir, "crash.log"),
                $"{DateTimeOffset.Now:u}\n{e.Exception}\n\n");
        }
        catch
        {
            // ignore
        }

        MessageBox.Show(
            "An unexpected error occurred and was logged. Your work-in-progress auto-save is intact.\n\n" + e.Exception.Message,
            "LabelAxis", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
