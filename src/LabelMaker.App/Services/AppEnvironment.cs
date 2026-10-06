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
    /// LabelAxis.exe keeps settings, user templates, calibration and recovery
    /// snapshots in a "Data" folder beside the executable instead of %APPDATA%,
    /// so the whole thing can live on a USB stick and leave no trace behind.
    /// </summary>
    public const string PortableMarkerFileName = "portable.txt";

    /// <param name="dataDirectory">
    /// A folder to keep settings, templates and the rest in instead of the usual places
    /// (<c>LabelAxis.exe --data-dir &lt;folder&gt;</c>), so the automated click-through tests
    /// start from a clean, known state and never touch the user's own settings.
    /// </param>
    public AppEnvironment(string? dataDirectory = null)
    {
        string? portableRoot = dataDirectory is null ? FindPortableRoot() : null;
        IsPortable = portableRoot is not null;

        string? root = dataDirectory ?? portableRoot;
        Paths = root is null ? new AppPaths() : new AppPaths(root, root);
        Paths.EnsureCreated();

        Settings = AppSettings.Load(Paths.SettingsFilePath);
        Templates = TemplateStore.CreateWithBuiltIns();
        Templates.LoadUserDirectory(Paths.UserTemplatesDirectory);
        Calibration = CalibrationStore.Load(Paths.CalibrationFilePath);
        PrintHistory = PrintHistoryStore.Load(Paths.PrintHistoryFilePath);
        AutoSave = new AutoSaveService(Paths.RecoveryDirectory)
        {
            Interval = TimeSpan.FromSeconds(Math.Max(30, Settings.AutoSaveSeconds)),
        };
        MyDesigns = new LabelMaker.Core.Designs.MyDesigns(Paths.DesignsDirectory);
    }

    /// <summary>The user's own designs (Save page as my design…), listed first in the design gallery.</summary>
    public LabelMaker.Core.Designs.MyDesigns MyDesigns { get; }

    /// <summary>The watch folder and web link for print requests from other programs, while the main window is open.</summary>
    public Automation.AutomationHost? Automation { get; set; }

    /// <summary>True when the app is keeping its data next to the executable.</summary>
    public bool IsPortable { get; }

    public AppPaths Paths { get; }

    public AppSettings Settings { get; }

    public TemplateStore Templates { get; }

    public CalibrationStore Calibration { get; }

    public PrintHistoryStore PrintHistory { get; }

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
