using System;
using System.Windows;

namespace LabelAxis.Setup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string arguments = Arguments();

        // A silent install (LabelAxis's own updater runs "/SILENT … /RELAUNCH=1") goes straight to
        // the Inno Setup installer inside, with the same switches, as it did before this window.
        if (IsSilent(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(Engine.Present ? Engine.RunAsIs(arguments) : 1);
            return;
        }

        Strings.Use(Strings.FromWindows());
        Theme.Apply(Resources, Theme.WindowsUsesDark());
        var window = new SetupWindow();
        MainWindow = window;
        window.Show();
    }

    private static bool IsSilent(string[] args) => Array.Exists(args, a =>
        a.Equals("/SILENT", StringComparison.OrdinalIgnoreCase) || a.Equals("/VERYSILENT", StringComparison.OrdinalIgnoreCase));

    /// <summary>Everything after the program's own name, exactly as it was typed.</summary>
    private static string Arguments()
    {
        string line = Environment.CommandLine.TrimStart();
        int end = line.StartsWith("\"", StringComparison.Ordinal) ? line.IndexOf('"', 1) : line.IndexOf(' ');
        return end < 0 ? string.Empty : line.Substring(end + 1).Trim();
    }
}
