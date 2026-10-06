using System.Text.Json;
using System.Text.Json.Serialization;
using LabelMaker.Core.Units;

namespace LabelMaker.Core.Settings;

public sealed record RecentProject(string Path, string Title, DateTimeOffset LastOpenedUtc);

/// <summary>Persisted application preferences. Plain data, atomic JSON save.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // System.Text.Json matches property names case-sensitively by default, so a
        // hand-edited settings.json with "darkTheme" instead of "DarkTheme" would drop
        // the value silently. This file is meant to be editable, so accept either.
        PropertyNameCaseInsensitive = true,
    };

    public Unit DisplayUnit { get; set; } = Unit.Mm;

    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public bool SnapToObjects { get; set; } = true;
    public double GridSizeMm { get; set; } = 1.0;

    public bool ShowRulers { get; set; } = true;
    public bool ShowGuides { get; set; } = true;
    public bool SnapToGuides { get; set; } = true;

    /// <summary>Snap tolerance in screen pixels for guide / object / edge snapping.</summary>
    public double SnapPixels { get; set; } = 10.0;

    public bool AutoSaveEnabled { get; set; } = true;
    public int AutoSaveSeconds { get; set; } = 120;
    public int BackupCount { get; set; } = 3;

    /// <summary>Disable hover/transition highlighting (e.g. the top menu's underline) for users sensitive to motion.</summary>
    public bool ReduceMotion { get; set; }

    public bool DarkTheme { get; set; }

    /// <summary>The look: one of the glass themes (frost, aurora, midnight, graphite, ember, ocean, sakura, sage); null follows <see cref="DarkTheme"/>.</summary>
    public string? Theme { get; set; }

    /// <summary>Interface language ("en", "ar", "ckb"); none follows the language of Windows.</summary>
    public string? Language { get; set; }

    /// <summary>Look for a newer version about once a day.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Where updates are published (a web address or a shared folder); none uses the one built in.</summary>
    public string? UpdateAddress { get; set; }

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    /// <summary>A version the user chose to skip; it is not offered again, a later one is.</summary>
    public string? SkippedUpdateVersion { get; set; }

    /// <summary>Confirm before removing a page or clearing the data source.</summary>
    public bool ConfirmDestructiveActions { get; set; } = true;

    /// <summary>Restore the last-open project automatically when the app starts.</summary>
    public bool ReopenLastProjectOnStart { get; set; }

    /// <summary>Show the welcome window (new, open, recent, sheet templates) when the app starts.</summary>
    public bool ShowWelcomeOnStart { get; set; } = true;

    /// <summary>Assumed screen DPI for the actual-size preview, tuned by the ruler calibration.</summary>
    public double ScreenDpiCalibration { get; set; } = 96.0;

    public string? LastPrinterName { get; set; }
    public string? LastTemplateId { get; set; }
    public string? LastProjectDirectory { get; set; }

    public List<RecentProject> RecentProjects { get; set; } = new();

    /// <summary>Colours chosen most recently in the colour dialog, newest first, as hex.</summary>
    public List<string> RecentColors { get; set; } = new();

    /// <summary>Gradients chosen most recently, newest first, as "#start #end angle".</summary>
    public List<string> RecentGradients { get; set; } = new();

    /// <summary>Print requests from other programs: the watch folder and the local web link.</summary>
    public AutomationSettings Automation { get; set; } = new();

    /// <summary>Sheet templates starred in the Label manager, shown first under Favourites.</summary>
    public List<string> FavoriteTemplateIds { get; set; } = new();

    /// <summary>Wi-Fi / network printers added in the Print dialog, printed to directly over IPP.</summary>
    public List<Printing.Ipp.IppPrinterInfo> NetworkPrinters { get; set; } = new();

    public int MaxRecentProjects { get; set; } = 12;

    public void AddRecent(string path, string title)
    {
        RecentProjects.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, new RecentProject(path, title, DateTimeOffset.UtcNow));
        if (RecentProjects.Count > MaxRecentProjects)
        {
            RecentProjects.RemoveRange(MaxRecentProjects, RecentProjects.Count - MaxRecentProjects);
        }
    }

    public void PruneMissingRecents() =>
        RecentProjects.RemoveAll(r => !File.Exists(r.Path));

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        if (File.Exists(path))
        {
            File.Replace(tmp, path, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, path);
        }
    }
}
