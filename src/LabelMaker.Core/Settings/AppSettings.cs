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

    /// <summary>Assumed screen DPI for the actual-size preview, tuned by the ruler calibration.</summary>
    public double ScreenDpiCalibration { get; set; } = 96.0;

    public string? LastPrinterName { get; set; }
    public string? LastTemplateId { get; set; }
    public string? LastProjectDirectory { get; set; }

    public List<RecentProject> RecentProjects { get; set; } = new();

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
