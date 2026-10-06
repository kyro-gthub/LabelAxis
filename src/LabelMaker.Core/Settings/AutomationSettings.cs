using System.Security.Cryptography;

namespace LabelMaker.Core.Settings;

/// <summary>
/// How other programs can ask LabelAxis to print: by dropping a request file into a watch
/// folder, or by sending it to a web link on this PC. Both are off until switched on.
/// </summary>
public sealed class AutomationSettings
{
    public const int DefaultPort = 8765;

    /// <summary>Print the .json and .csv request files that appear in <see cref="WatchFolder"/>.</summary>
    public bool WatchFolderEnabled { get; set; }

    public string? WatchFolder { get; set; }

    /// <summary>Take print requests at http://localhost:<see cref="WebLinkPort"/>/print.</summary>
    public bool WebLinkEnabled { get; set; }

    public int WebLinkPort { get; set; } = DefaultPort;

    /// <summary>Also take requests from other computers on the network (they must send the access key).</summary>
    public bool AllowOtherComputers { get; set; }

    /// <summary>The key other computers send (header X-LabelAxis-Key, or ?key=); made the first time it is needed.</summary>
    public string? AccessKey { get; set; }

    /// <summary>The design a CSV request is printed with when it does not name one.</summary>
    public string? DefaultDesign { get; set; }

    /// <summary>The printer used when neither the request nor the design names one.</summary>
    public string? DefaultPrinter { get; set; }

    /// <summary>The access key, made now if there is none yet.</summary>
    public string EnsureAccessKey()
    {
        if (string.IsNullOrWhiteSpace(AccessKey))
        {
            AccessKey = NewAccessKey();
        }

        return AccessKey;
    }

    /// <summary>A new random key: 24 letters and digits that are easy to read out.</summary>
    public static string NewAccessKey()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        return string.Create(24, 0, (span, _) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }
        });
    }
}
