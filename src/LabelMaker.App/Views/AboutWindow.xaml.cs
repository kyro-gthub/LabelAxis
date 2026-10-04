using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using LabelMaker.App.Services;

namespace LabelMaker.App.Views;

/// <summary>Help › About: the product, its version, where this copy keeps its data, and credits.</summary>
public partial class AboutWindow : Window
{
    private readonly AppEnvironment _env;

    public AboutWindow(AppEnvironment env)
    {
        _env = env;
        InitializeComponent();
        TaglineText.Text = AppInfo.Tagline;
        VersionText.Text = $"Version {AppInfo.Version} · {AppInfo.Architecture}";
        DescriptionText.Text = AppInfo.Description;
        EditionText.Text = env.IsPortable
            ? "Portable — settings, templates and calibration are kept next to the program"
            : "Installed";
        DataText.Text = env.Paths.RoamingDirectory;
        SystemText.Text = $"{RuntimeInformation.OSDescription} · {RuntimeInformation.FrameworkDescription}";
        CopyrightText.Text = AppInfo.Copyright + " Built with open-source software and fonts; see Licences & credits.";
    }

    private void OnOpenData(object sender, RoutedEventArgs e) => OpenFolder(_env.Paths.RoamingDirectory);

    private void OnLicences(object sender, RoutedEventArgs e)
    {
        string notices = Path.Combine(AppContext.BaseDirectory, "licenses", "THIRD-PARTY-NOTICES.txt");
        if (File.Exists(notices))
        {
            Open(notices);
        }
        else
        {
            OpenFolder(Path.Combine(AppContext.BaseDirectory, "licenses"));
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(AppInfo.SupportDetails(_env));
            CopyButton.Content = "Copied ✓";
        }
        catch (COMException)
        {
            // Another program has the clipboard open; the button simply stays as it was.
        }
    }

    private void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Open(path);
        }
    }

    private void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "LabelAxis", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
