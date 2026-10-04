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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Environment = new AppEnvironment();
        Localization.Localizer.Start(Localization.Localizer.Resolve(Environment.Settings.Language));
        ThemeManager.Apply(Environment.Settings.DarkTheme ? AppTheme.Dark : AppTheme.Light);
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
    }

    private bool _restartOnExit;

    /// <summary>
    /// Closes LabelAxis and starts it again (after a change of language). The main window asks
    /// about unsaved work first as usual; if that is cancelled, nothing happens.
    /// </summary>
    internal static void Restart()
    {
        if (Current is not App app || app.MainWindow is not { } main)
        {
            return;
        }

        app._restartOnExit = true;
        main.Close();
        if (main.IsVisible)
        {
            app._restartOnExit = false;
            return;
        }

        app.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Environment?.SaveSettings();
        if (_restartOnExit && System.Environment.ProcessPath is { } exe)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Could not start again; the user can open LabelAxis themselves.
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
