using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelMaker.Core.Updates;

/// <summary>One downloadable file in an update: where it is, how big, and its SHA-256 fingerprint.</summary>
public sealed record UpdatePackage
{
    /// <summary>The file's address: a name next to update.json, or a full https:// or file:// address.</summary>
    [JsonPropertyName("file")]
    public string File { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }
}

/// <summary>
/// The update file (update.json) the build script writes next to the installers: the newest
/// version, what changed, and the installers with their fingerprints. LabelAxis reads it from
/// the update address (a web address or a shared folder) to see whether a newer version exists.
/// </summary>
public sealed record UpdateFeed
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("published")]
    public string? Published { get; init; }

    /// <summary>What is new, in English; <see cref="NotesByLanguage"/> may hold it in other languages.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("notesByLanguage")]
    public Dictionary<string, string>? NotesByLanguage { get; init; }

    /// <summary>The setup program that uses the .NET runtime shared by Windows apps (the small download).</summary>
    [JsonPropertyName("installer")]
    public UpdatePackage? Installer { get; init; }

    /// <summary>The setup program with the .NET runtime inside (works without internet during setup).</summary>
    [JsonPropertyName("offlineInstaller")]
    public UpdatePackage? OfflineInstaller { get; init; }

    /// <summary>The portable zip, for copies that run without being installed.</summary>
    [JsonPropertyName("portable")]
    public UpdatePackage? Portable { get; init; }

    public static UpdateFeed Parse(string json)
    {
        UpdateFeed feed = JsonSerializer.Deserialize<UpdateFeed>(json, Options)
                          ?? throw new FormatException("The update file is empty.");
        if (feed.ParsedVersion is null)
        {
            throw new FormatException($"The update file names no valid version (\"{feed.Version}\").");
        }

        return feed;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    [JsonIgnore]
    public Version? ParsedVersion => System.Version.TryParse(Version, out Version? v) ? Normalise(v) : null;

    public bool IsNewerThan(Version current) => ParsedVersion is { } v && v > Normalise(current);

    /// <summary>The notes in <paramref name="language"/> when the file has them, otherwise in English.</summary>
    public string NotesFor(string language) =>
        NotesByLanguage is not null && NotesByLanguage.TryGetValue(language, out string? notes) && !string.IsNullOrWhiteSpace(notes)
            ? notes
            : Notes ?? string.Empty;

    // 1.2 and 1.2.0.0 are the same version.
    private static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
}

/// <summary>Reads the update file and fetches packages over https or from a folder, and checks fingerprints.</summary>
public static class UpdateSource
{
    private static readonly HttpClient Http = CreateClient();

    // GitHub (where releases are published) asks every caller to name itself.
    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LabelAxis-Updater/1.0");
        return http;
    }

    /// <summary>
    /// The address typed in Settings or built into the app, as a URI: https:// web addresses,
    /// file:// addresses, shared folders (\\server\share\update.json) and local paths.
    /// A folder (no .json at the end) means its update.json.
    /// </summary>
    public static Uri ToFeedUri(string address)
    {
        address = address.Trim();
        if (address.Length == 0)
        {
            throw new FormatException("No update address is set.");
        }

        Uri uri = Uri.TryCreate(address, UriKind.Absolute, out Uri? absolute) ? absolute : new Uri(Path.GetFullPath(address));
        if (uri.Scheme is not ("https" or "http" or "file"))
        {
            throw new FormatException($"Updates can be read from https:// addresses and folders, not {uri.Scheme}://.");
        }

        // Plain http is open to tampering on the way; only a test server on this PC may use it.
        if (uri.Scheme == "http" && !uri.IsLoopback)
        {
            throw new FormatException("Use an https:// address for updates (plain http:// is not safe).");
        }

        if (!uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            uri = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/update.json");
        }

        return uri;
    }

    /// <summary>A package's address: next to the update file unless it is a full address of its own.</summary>
    public static Uri Resolve(Uri feed, UpdatePackage package)
    {
        Uri target = Uri.TryCreate(package.File, UriKind.Absolute, out Uri? absolute) ? absolute : new Uri(feed, package.File);
        if (target.Scheme is not ("https" or "file") && !(target.Scheme == "http" && target.IsLoopback))
        {
            throw new FormatException($"The update file points to an address LabelAxis does not download from ({target.Scheme}://).");
        }

        return target;
    }

    public static async Task<UpdateFeed> ReadFeedAsync(Uri feed, CancellationToken token)
    {
        string json = feed.IsFile
            ? await File.ReadAllTextAsync(feed.LocalPath, token).ConfigureAwait(false)
            : await Http.GetStringAsync(feed, token).ConfigureAwait(false);
        return UpdateFeed.Parse(json);
    }

    /// <summary>
    /// Copies the package to <paramref name="destination"/>, reporting bytes as they arrive, then
    /// checks its size and SHA-256. A file that does not match is deleted and an error raised.
    /// </summary>
    public static async Task DownloadAsync(Uri source, UpdatePackage package, string destination, IProgress<long>? progress, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(package.Sha256))
        {
            throw new InvalidDataException("The update file gives no fingerprint (SHA-256) for this download, so it cannot be checked.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string partial = destination + ".part";
        try
        {
            await using (Stream input = source.IsFile
                             ? new FileStream(source.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true)
                             : await Http.GetStreamAsync(source, token).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    total += read;
                    progress?.Report(total);
                }
            }

            Verify(partial, package);
            File.Move(partial, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }

    /// <summary>Throws when the file is not exactly the one the update file describes.</summary>
    public static void Verify(string path, UpdatePackage package)
    {
        long length = new FileInfo(path).Length;
        if (package.Size > 0 && length != package.Size)
        {
            throw new InvalidDataException($"The download is {length:N0} bytes, but the update file says {package.Size:N0}. It was not used.");
        }

        using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actual, package.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The download does not match its fingerprint (SHA-256), so it may be damaged or altered. It was not used.");
        }
    }

    /// <summary>The SHA-256 fingerprint of a file, as the build script writes it into update.json.</summary>
    public static string Fingerprint(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
