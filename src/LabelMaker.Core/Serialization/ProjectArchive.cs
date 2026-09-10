using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;

namespace LabelMaker.Core.Serialization;

/// <summary>Header written to <c>manifest.json</c> at the root of a .labelproj archive.</summary>
public sealed class ProjectManifest
{
    public string Schema { get; set; } = "labelmaker.project/1";
    public int SchemaVersion { get; set; } = ProjectModel.CurrentSchemaVersion;
    public string AppVersion { get; set; } = "1.0.0";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>In-memory image asset (content-addressed by SHA-256).</summary>
public sealed record ProjectAsset(string AssetId, string Extension, byte[] Bytes)
{
    public string ArchiveEntryName => $"assets/{AssetId}{Extension}";

    public static string ComputeId(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}

/// <summary>Cached snapshot of an imported dataset kept inside the project.</summary>
public sealed record ProjectDataCache(string ConnectionId, string Csv, string MetaJson);

/// <summary>The full deserialized contents of a .labelproj file.</summary>
public sealed class ProjectBundle
{
    public required ProjectManifest Manifest { get; init; }
    public required ProjectModel Project { get; init; }
    public IReadOnlyList<ProjectAsset> Assets { get; init; } = Array.Empty<ProjectAsset>();
    public IReadOnlyList<ProjectDataCache> DataCaches { get; init; } = Array.Empty<ProjectDataCache>();
    public byte[]? ThumbnailPng { get; init; }
}

/// <summary>
/// Reads and writes the ZIP-based <c>.labelproj</c> container (spec §18).
/// Layout:
/// <code>
/// manifest.json                header
/// project.json                 ProjectModel (System.Text.Json, closed converters)
/// template.json                embedded Formtec template (DTO form) — portability
/// data/&lt;id&gt;.csv / .meta.json  cached datasets
/// assets/&lt;sha256&gt;.&lt;ext&gt;       de-duplicated images
/// thumbnails/label.png         preview for Recent / template library
/// </code>
/// </summary>
public static class ProjectArchive
{
    private static readonly JsonSerializerOptions ManifestOptions = new() { WriteIndented = true };

    public static void Write(Stream destination, ProjectBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(bundle);

        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        WriteText(zip, "manifest.json", JsonSerializer.Serialize(bundle.Manifest, ManifestOptions));
        WriteText(zip, "project.json", JsonSerializer.Serialize(bundle.Project, ProjectJson.Default));

        if (bundle.Project.Template is { } template)
        {
            WriteText(zip, "template.json", TemplateJson.ToJson(template));
        }

        foreach (ProjectAsset asset in bundle.Assets)
        {
            ZipArchiveEntry entry = zip.CreateEntry(asset.ArchiveEntryName, CompressionLevel.Optimal);
            using Stream s = entry.Open();
            s.Write(asset.Bytes);
        }

        foreach (ProjectDataCache cache in bundle.DataCaches)
        {
            WriteText(zip, $"data/{cache.ConnectionId}.csv", cache.Csv);
            WriteText(zip, $"data/{cache.ConnectionId}.meta.json", cache.MetaJson);
        }

        if (bundle.ThumbnailPng is { Length: > 0 })
        {
            ZipArchiveEntry entry = zip.CreateEntry("thumbnails/label.png", CompressionLevel.Optimal);
            using Stream s = entry.Open();
            s.Write(bundle.ThumbnailPng);
        }
    }

    public static ProjectBundle Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);

        ProjectManifest manifest = ReadJson<ProjectManifest>(zip, "manifest.json", ManifestOptions)
                                   ?? new ProjectManifest();

        if (manifest.SchemaVersion > ProjectModel.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"This project was created by a newer version of Label Maker (schema {manifest.SchemaVersion}). " +
                "Update the application to open it.");
        }

        string projectRaw = ReadText(zip, "project.json")
                            ?? throw new InvalidDataException("project.json is missing from the archive.");
        projectRaw = ProjectUpgrader.Upgrade(projectRaw, manifest.SchemaVersion);

        ProjectModel project = JsonSerializer.Deserialize<ProjectModel>(projectRaw, ProjectJson.Default)
                               ?? throw new InvalidDataException("project.json could not be parsed.");

        string? templateRaw = ReadText(zip, "template.json");
        if (templateRaw is not null)
        {
            try
            {
                project.Template = TemplateJson.FromJson(templateRaw);
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                // Keep the project loadable even if the embedded template is broken.
                project.Template = null;
            }
        }

        var assets = new List<ProjectAsset>();
        var dataCaches = new Dictionary<string, (string? Csv, string? Meta)>();
        byte[]? thumbnail = null;

        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (entry.FullName.StartsWith("assets/", StringComparison.Ordinal) && entry.Length > 0)
            {
                string fileName = Path.GetFileNameWithoutExtension(entry.FullName);
                string ext = Path.GetExtension(entry.FullName);
                assets.Add(new ProjectAsset(fileName, ext, ReadBytes(entry)));
            }
            else if (entry.FullName.StartsWith("data/", StringComparison.Ordinal))
            {
                string name = Path.GetFileName(entry.FullName);
                if (name.EndsWith(".meta.json", StringComparison.Ordinal))
                {
                    string id = name[..^".meta.json".Length];
                    dataCaches[id] = (dataCaches.GetValueOrDefault(id).Csv, ReadEntryText(entry));
                }
                else if (name.EndsWith(".csv", StringComparison.Ordinal))
                {
                    string id = name[..^".csv".Length];
                    dataCaches[id] = (ReadEntryText(entry), dataCaches.GetValueOrDefault(id).Meta);
                }
            }
            else if (entry.FullName == "thumbnails/label.png" && entry.Length > 0)
            {
                thumbnail = ReadBytes(entry);
            }
        }

        return new ProjectBundle
        {
            Manifest = manifest,
            Project = project,
            Assets = assets,
            DataCaches = dataCaches
                .Where(kv => kv.Value.Csv is not null)
                .Select(kv => new ProjectDataCache(kv.Key, kv.Value.Csv!, kv.Value.Meta ?? "{}"))
                .ToList(),
            ThumbnailPng = thumbnail,
        };
    }

    private static void WriteText(ZipArchive zip, string entryName, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using Stream s = entry.Open();
        using var writer = new StreamWriter(s, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string? ReadText(ZipArchive zip, string entryName)
    {
        ZipArchiveEntry? entry = zip.GetEntry(entryName);
        return entry is null ? null : ReadEntryText(entry);
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using Stream s = entry.Open();
        using var reader = new StreamReader(s, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static T? ReadJson<T>(ZipArchive zip, string entryName, JsonSerializerOptions options)
    {
        string? text = ReadText(zip, entryName);
        return text is null ? default : JsonSerializer.Deserialize<T>(text, options);
    }

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using Stream s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
