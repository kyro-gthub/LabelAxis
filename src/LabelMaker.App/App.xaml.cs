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

        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
        {
            window.OpenProjectPath(e.Args[0]);
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
