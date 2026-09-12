using System.IO;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Settings;

namespace LabelMaker.App.Services;

/// <summary>Process-wide services: paths, settings, template catalogue, calibration, recovery.</summary>
public sealed class AppEnvironment
{
    /// <summary>
    /// Marker file that switches the app to portable mode. Dropping it next to
    /// LabelMaker.exe keeps settings, user templates, calibration and recovery
    /// snapshots in a "Data" folder beside the executable instead of %APPDATA%,
    /// so the whole thing can live on a USB stick and leave no trace behind.
    /// </summary>
    public const string PortableMarkerFileName = "portable.txt";

    public AppEnvironment()
    {
        string? portableRoot = FindPortableRoot();
        IsPortable = portableRoot is not null;

        Paths = portableRoot is null ? new AppPaths() : new AppPaths(portableRoot, portableRoot);
        Paths.EnsureCreated();

        Settings = AppSettings.Load(Paths.SettingsFilePath);
        Templates = TemplateStore.CreateWithBuiltIns();
        Templates.LoadUserDirectory(Paths.UserTemplatesDirectory);
        Calibration = CalibrationStore.Load(Paths.CalibrationFilePath);
        AutoSave = new AutoSaveService(Paths.RecoveryDirectory)
        {
            Interval = TimeSpan.FromSeconds(Math.Max(30, Settings.AutoSaveSeconds)),
        };
    }

    /// <summary>True when the app is keeping its data next to the executable.</summary>
    public bool IsPortable { get; }

    public AppPaths Paths { get; }

    public AppSettings Settings { get; }

    public TemplateStore Templates { get; }

    public CalibrationStore Calibration { get; }

    public AutoSaveService AutoSave { get; }

    /// <summary>
    /// Returns the folder portable data should live in, or null for a normal install.
    /// A read-only location (Program Files, a locked-down share) silently falls back
    /// to %APPDATA% rather than failing to start.
    /// </summary>
    private static string? FindPortableRoot()
    {
        try
        {
            string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (exeDir is null || !File.Exists(Path.Combine(exeDir, PortableMarkerFileName)))
            {
                return null;
            }

            string data = Path.Combine(exeDir, "Data");
            Directory.CreateDirectory(data);

            // Prove it is writable before committing to it.
            string probe = Path.Combine(data, ".write-test");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return data;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void SaveSettings()
    {
        try
        {
            Settings.Save(Paths.SettingsFilePath);
        }
        catch (IOException)
        {
            // non-fatal
        }
    }
}
