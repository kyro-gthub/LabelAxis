namespace LabelMaker.Core.Settings;

/// <summary>
/// Standard per-user locations for application data (spec §44: user settings in
/// the appropriate Windows user-data directory, no admin needed). Overridable for
/// tests.
/// </summary>
public sealed class AppPaths
{
    public const string AppFolderName = "LabelMaker";

    public AppPaths(string? roamingRoot = null, string? localRoot = null)
    {
        string roaming = roamingRoot
                         ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = localRoot
                       ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        RoamingDirectory = Path.Combine(roaming, AppFolderName);
        LocalDirectory = Path.Combine(local, AppFolderName);
        UserTemplatesDirectory = Path.Combine(RoamingDirectory, "templates");
        UserLabelTemplatesDirectory = Path.Combine(RoamingDirectory, "label-templates");
        SettingsFilePath = Path.Combine(RoamingDirectory, "settings.json");
        CalibrationFilePath = Path.Combine(RoamingDirectory, "calibration.json");
        RecoveryDirectory = Path.Combine(LocalDirectory, "recovery");
        LogDirectory = Path.Combine(LocalDirectory, "logs");
        CacheDirectory = Path.Combine(LocalDirectory, "cache");
    }

    public string RoamingDirectory { get; }
    public string LocalDirectory { get; }

    /// <summary>User-added Formtec/label-sheet templates (spec §33).</summary>
    public string UserTemplatesDirectory { get; }

    /// <summary>User-saved reusable label designs (spec §19).</summary>
    public string UserLabelTemplatesDirectory { get; }

    public string SettingsFilePath { get; }
    public string CalibrationFilePath { get; }
    public string RecoveryDirectory { get; }
    public string LogDirectory { get; }
    public string CacheDirectory { get; }

    public void EnsureCreated()
    {
        foreach (string dir in new[]
                 {
                     RoamingDirectory, LocalDirectory, UserTemplatesDirectory, UserLabelTemplatesDirectory,
                     RecoveryDirectory, LogDirectory, CacheDirectory,
                 })
        {
            Directory.CreateDirectory(dir);
        }
    }
}
