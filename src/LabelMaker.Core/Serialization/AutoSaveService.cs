using System.Text.Json;

namespace LabelMaker.Core.Serialization;

/// <summary>Metadata about a recoverable auto-save, shown in the "restore?" prompt (spec §32).</summary>
public sealed record RecoveryInfo(
    string ProjectId,
    string Title,
    string? OriginalPath,
    DateTimeOffset SavedUtc,
    string RecoveryFilePath);

/// <summary>
/// Writes crash-recovery snapshots to a location <b>separate</b> from the user's
/// saved file, so a recovery write can never corrupt real work (spec §32). On a
/// clean shutdown the recovery folder for the project is purged; anything left
/// behind on next launch is offered for recovery.
/// </summary>
public sealed class AutoSaveService
{
    private readonly string _recoveryRoot;
    private static readonly JsonSerializerOptions MetaOptions = new() { WriteIndented = true };

    public AutoSaveService(string recoveryRoot)
    {
        _recoveryRoot = recoveryRoot;
        Directory.CreateDirectory(_recoveryRoot);
    }

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(120);

    private string ProjectFolder(string projectId) => Path.Combine(_recoveryRoot, Sanitize(projectId));

    /// <summary>Write a recovery snapshot for the current in-memory document.</summary>
    public void Snapshot(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        string folder = ProjectFolder(document.Project.Metadata.Id);
        Directory.CreateDirectory(folder);

        string dataPath = Path.Combine(folder, "autosave.labelproj");
        string tmp = dataPath + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            ProjectArchive.Write(fs, document.ToBundle());
            fs.Flush(flushToDisk: true);
        }

        if (File.Exists(dataPath))
        {
            File.Replace(tmp, dataPath, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, dataPath);
        }

        var info = new RecoveryInfo(
            document.Project.Metadata.Id,
            document.Project.Metadata.Title,
            document.FilePath,
            DateTimeOffset.UtcNow,
            dataPath);
        File.WriteAllText(Path.Combine(folder, "recovery.json"), JsonSerializer.Serialize(info, MetaOptions));
    }

    /// <summary>Call on a clean save or clean exit to discard the project's recovery data.</summary>
    public void ClearFor(string projectId)
    {
        string folder = ProjectFolder(projectId);
        if (Directory.Exists(folder))
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // best-effort
            }
        }
    }

    /// <summary>Recovery snapshots left over from a previous run (unclean shutdown).</summary>
    public IReadOnlyList<RecoveryInfo> FindRecoverable()
    {
        var results = new List<RecoveryInfo>();
        if (!Directory.Exists(_recoveryRoot))
        {
            return results;
        }

        foreach (string folder in Directory.EnumerateDirectories(_recoveryRoot))
        {
            string metaPath = Path.Combine(folder, "recovery.json");
            string dataPath = Path.Combine(folder, "autosave.labelproj");
            if (!File.Exists(metaPath) || !File.Exists(dataPath))
            {
                continue;
            }

            try
            {
                RecoveryInfo? info = JsonSerializer.Deserialize<RecoveryInfo>(File.ReadAllText(metaPath), MetaOptions);
                if (info is not null)
                {
                    results.Add(info with { RecoveryFilePath = dataPath });
                }
            }
            catch (JsonException)
            {
                // ignore an unreadable marker
            }
        }

        return results.OrderByDescending(r => r.SavedUtc).ToList();
    }

    public ProjectDocument Restore(RecoveryInfo info)
    {
        ProjectDocument document = ProjectFile.Load(info.RecoveryFilePath);
        document.FilePath = info.OriginalPath;
        document.HasUnsavedChanges = true;
        return document;
    }

    private static string Sanitize(string id)
    {
        Span<char> buffer = stackalloc char[id.Length == 0 ? 1 : id.Length];
        int n = 0;
        foreach (char c in id)
        {
            buffer[n++] = Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c;
        }

        return n == 0 ? "project" : new string(buffer[..n]);
    }
}
