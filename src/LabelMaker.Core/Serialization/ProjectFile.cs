using LabelMaker.Core.Model;

namespace LabelMaker.Core.Serialization;

/// <summary>Everything needed to persist a project as one .labelproj file.</summary>
public sealed class ProjectDocument
{
    public required ProjectModel Project { get; init; }
    public AssetStore Assets { get; init; } = new();
    public List<ProjectDataCache> DataCaches { get; } = new();
    public byte[]? ThumbnailPng { get; set; }

    /// <summary>Full path of the last successful save, or null for a never-saved project.</summary>
    public string? FilePath { get; set; }

    public bool HasUnsavedChanges { get; set; }

    public ProjectBundle ToBundle() => new()
    {
        Manifest = new ProjectManifest
        {
            SchemaVersion = Project.SchemaVersion,
            AppVersion = Project.Metadata.AppVersion,
            CreatedUtc = Project.Metadata.CreatedUtc,
            ModifiedUtc = DateTimeOffset.UtcNow,
        },
        Project = Project,
        Assets = Assets.All.ToList(),
        DataCaches = DataCaches,
        ThumbnailPng = ThumbnailPng,
    };
}

public sealed class ProjectSaveOptions
{
    /// <summary>Keep a "&lt;name&gt;.labelproj.bak" copy of the previous file.</summary>
    public bool KeepBackup { get; init; } = true;

    /// <summary>Prune assets no longer referenced by any object/background before saving.</summary>
    public bool CollectGarbageAssets { get; init; } = true;
}

/// <summary>
/// Safe, atomic project persistence (spec §18, §28). Writes to a sibling
/// <c>.tmp</c>, flushes to disk, then swaps it into place with
/// <see cref="File.Replace(string, string, string)"/> (falling back to
/// delete+move across volumes) so a crash mid-save never destroys the last good
/// file. Loading never executes embedded code.
/// </summary>
public static class ProjectFile
{
    private const string Extension = ".labelproj";

    public static void Save(ProjectDocument document, string path, ProjectSaveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= new ProjectSaveOptions();

        if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            path += Extension;
        }

        if (options.CollectGarbageAssets)
        {
            document.Assets.RemoveUnreferenced(CollectReferencedAssetIds(document.Project));
        }

        document.Project.SchemaVersion = ProjectModel.CurrentSchemaVersion;
        document.Project.TouchModified();

        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, Path.GetFileName(path) + ".tmp");

        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            ProjectArchive.Write(fs, document.ToBundle());
            fs.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
        {
            string? backup = options.KeepBackup ? path + ".bak" : null;
            try
            {
                File.Replace(tempPath, path, backup, ignoreMetadataErrors: true);
            }
            catch (PlatformNotSupportedException)
            {
                ReplaceByMove(tempPath, path, backup);
            }
            catch (IOException)
            {
                ReplaceByMove(tempPath, path, backup);
            }
        }
        else
        {
            File.Move(tempPath, path);
        }

        document.FilePath = path;
        document.HasUnsavedChanges = false;
    }

    public static ProjectDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream fs = File.OpenRead(path);
        return Load(fs, path);
    }

    public static ProjectDocument Load(Stream stream, string? path = null)
    {
        ProjectBundle bundle = ProjectArchive.Read(stream);

        var assets = new AssetStore();
        foreach (ProjectAsset asset in bundle.Assets)
        {
            assets.AddExisting(asset);
        }

        var document = new ProjectDocument
        {
            Project = bundle.Project,
            Assets = assets,
            ThumbnailPng = bundle.ThumbnailPng,
            FilePath = path,
            HasUnsavedChanges = false,
        };
        document.DataCaches.AddRange(bundle.DataCaches);
        return document;
    }

    /// <summary>Every asset id referenced anywhere in the project (for garbage collection).</summary>
    public static IEnumerable<string> CollectReferencedAssetIds(ProjectModel project)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Scan(LabelDocument doc)
        {
            if (doc.Background.AssetId is { Length: > 0 } bg)
            {
                ids.Add(bg);
            }

            foreach (LabelObject obj in doc.Objects)
            {
                if (obj is Model.Objects.ImageObject { AssetId.Length: > 0 } image)
                {
                    ids.Add(image.AssetId);
                }
            }
        }

        Scan(project.MasterLabel);
        foreach (LabelPage page in project.Pages)
        {
            Scan(page.Document);
        }

        foreach (ProjectFont font in project.Fonts)
        {
            ids.Add(font.AssetId);
        }

        return ids;
    }

    private static void ReplaceByMove(string tempPath, string path, string? backupPath)
    {
        if (backupPath is not null)
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(path, backupPath);
        }
        else
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);
    }
}
