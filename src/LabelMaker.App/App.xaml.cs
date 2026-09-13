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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Environment = new AppEnvironment();
        ThemeManager.Apply(Environment.Settings.DarkTheme ? AppTheme.Dark : AppTheme.Light);

        // A class handler catches every Window, derived types included -- an implicit
        // style would not, which is the same trap that left the dialogs light earlier.
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ThemeManager.ApplyTitleBar((Window)sender)));

        DispatcherUnhandledException += OnUnhandledException;

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
                "Label Maker — Recovery",
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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Environment?.SaveSettings();
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
            "Label Maker", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
