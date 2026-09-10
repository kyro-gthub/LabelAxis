namespace LabelMaker.Core.Serialization;

/// <summary>
/// Content-addressed image store for a project. Images are keyed by SHA-256 so
/// the same picture used on many objects is stored once (spec §18 "de-duplicated
/// images"). Pure bytes — decoding is the UI/exporter's job.
/// </summary>
public sealed class AssetStore
{
    private readonly Dictionary<string, ProjectAsset> _assets = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ProjectAsset> All => _assets.Values;

    public int Count => _assets.Count;

    public bool Contains(string assetId) => _assets.ContainsKey(assetId);

    public ProjectAsset? Get(string assetId) => _assets.GetValueOrDefault(assetId);

    public byte[]? GetBytes(string assetId) => _assets.GetValueOrDefault(assetId)?.Bytes;

    /// <summary>Add bytes, returning the asset id. Idempotent for identical content.</summary>
    public string Add(byte[] bytes, string extension)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        string id = ProjectAsset.ComputeId(bytes);
        if (!_assets.ContainsKey(id))
        {
            string ext = NormalizeExtension(extension);
            _assets[id] = new ProjectAsset(id, ext, bytes);
        }

        return id;
    }

    public string AddFile(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return Add(bytes, Path.GetExtension(path));
    }

    public void AddExisting(ProjectAsset asset) => _assets[asset.AssetId] = asset;

    public void Clear() => _assets.Clear();

    /// <summary>Drop assets not referenced by any id in <paramref name="referencedIds"/>.</summary>
    public int RemoveUnreferenced(IEnumerable<string> referencedIds)
    {
        var keep = new HashSet<string>(referencedIds, StringComparer.OrdinalIgnoreCase);
        var dead = _assets.Keys.Where(k => !keep.Contains(k)).ToList();
        foreach (string id in dead)
        {
            _assets.Remove(id);
        }

        return dead.Count;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return ".bin";
        }

        string ext = extension.Trim().ToLowerInvariant();
        if (!ext.StartsWith('.'))
        {
            ext = "." + ext;
        }

        return ext switch
        {
            ".jpeg" => ".jpg",
            ".tif" => ".tiff",
            _ => ext,
        };
    }
}
