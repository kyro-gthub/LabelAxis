namespace LabelMaker.Core.Settings;

/// <summary>
/// Standard per-user locations for application data (spec §44: user settings in
/// the appropriate Windows user-data directory, no admin needed). Overridable for
/// tests.
/// </summary>
public sealed class AppPaths
{
    public const string AppFolderName = "LabelAxis";

    /// <summary>The folder name used before the app was renamed to LabelAxis.</summary>
    public const string LegacyFolderName = "LabelMaker";

    public AppPaths(string? roamingRoot = null, string? localRoot = null)
    {
        string roaming = roamingRoot
                         ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = localRoot
                       ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        CarryOverLegacyFolder(roaming);
        if (!string.Equals(local, roaming, StringComparison.OrdinalIgnoreCase))
        {
            CarryOverLegacyFolder(local);
        }

        RoamingDirectory = Path.Combine(roaming, AppFolderName);
        LocalDirectory = Path.Combine(local, AppFolderName);
        UserTemplatesDirectory = Path.Combine(RoamingDirectory, "templates");
        UserLabelTemplatesDirectory = Path.Combine(RoamingDirectory, "label-templates");
        SettingsFilePath = Path.Combine(RoamingDirectory, "settings.json");
        CalibrationFilePath = Path.Combine(RoamingDirectory, "calibration.json");
        PrintHistoryFilePath = Path.Combine(RoamingDirectory, "print-history.json");
        RecoveryDirectory = Path.Combine(LocalDirectory, "recovery");
        LogDirectory = Path.Combine(LocalDirectory, "logs");
        CacheDirectory = Path.Combine(LocalDirectory, "cache");
        FontsDirectory = Path.Combine(RoamingDirectory, "fonts");
    }

    public string RoamingDirectory { get; }
    public string LocalDirectory { get; }

    /// <summary>User-added Formtec/label-sheet templates (spec §33).</summary>
    public string UserTemplatesDirectory { get; }

    /// <summary>User-saved reusable label designs (spec §19).</summary>
    public string UserLabelTemplatesDirectory { get; }

    public string SettingsFilePath { get; }
    public string CalibrationFilePath { get; }

    /// <summary>The print log (what was printed, when, where) behind Print history and reprint.</summary>
    public string PrintHistoryFilePath { get; }
    public string RecoveryDirectory { get; }
    public string LogDirectory { get; }
    public string CacheDirectory { get; }

    /// <summary>Font files added to LabelAxis (Settings › Fonts, or "Add font…" by the font list), one folder per family.</summary>
    public string FontsDirectory { get; }

    /// <summary>
    /// Settings, templates, calibration and recovery snapshots saved under the old
    /// "LabelMaker" folder are copied to the "LabelAxis" folder the first time the renamed
    /// app starts, so nothing is lost. The old folder is left in place.
    /// </summary>
    private static void CarryOverLegacyFolder(string root)
    {
        try
        {
            string legacy = Path.Combine(root, LegacyFolderName);
            string current = Path.Combine(root, AppFolderName);
            if (Directory.Exists(current) || !Directory.Exists(legacy))
            {
                return;
            }

            foreach (string dir in Directory.EnumerateDirectories(legacy, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(current, Path.GetRelativePath(legacy, dir)));
            }

            Directory.CreateDirectory(current);
            foreach (string file in Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, Path.Combine(current, Path.GetRelativePath(legacy, file)), overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the app still starts with fresh settings.
        }
    }

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
