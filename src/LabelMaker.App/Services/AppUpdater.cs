using System.IO;
using LabelMaker.Core.Updates;

namespace LabelMaker.App.Services;

/// <summary>
/// Looks for a newer LabelAxis where updates are published, and fetches it. The update address
/// is the one in Settings, or else the one this build was made with. A check happens about
/// once a day (when switched on in Settings) and from Help › Check for updates.
/// </summary>
public sealed class AppUpdater
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(20);
    private readonly AppEnvironment _env;

    public AppUpdater(AppEnvironment env) => _env = env;

    public string Address => string.IsNullOrWhiteSpace(_env.Settings.UpdateAddress)
        ? AppInfo.BuiltInUpdateAddress
        : _env.Settings.UpdateAddress!.Trim();

    public bool HasAddress => Address.Length > 0;

    public static Version CurrentVersion => Version.TryParse(AppInfo.Version, out Version? v) ? v : new Version(1, 0);

    /// <summary>True when the daily check is switched on, an address is set, and the last check was a while ago.</summary>
    public bool IsCheckDue =>
        _env.Settings.CheckForUpdates && HasAddress &&
        (_env.Settings.LastUpdateCheckUtc is not { } last || DateTimeOffset.UtcNow - last > CheckEvery);

    /// <summary>The update file, when it offers a version newer than this one; otherwise null.</summary>
    public async Task<(UpdateFeed? Feed, Uri FeedUri)> CheckAsync(CancellationToken token)
    {
        Uri feedUri = UpdateSource.ToFeedUri(Address);
        UpdateFeed feed = await UpdateSource.ReadFeedAsync(feedUri, token).ConfigureAwait(true);
        _env.Settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
        _env.SaveSettings();
        return (feed.IsNewerThan(CurrentVersion) ? feed : null, feedUri);
    }

    public bool IsSkipped(UpdateFeed feed) =>
        string.Equals(_env.Settings.SkippedUpdateVersion, feed.Version, StringComparison.OrdinalIgnoreCase);

    public void Skip(UpdateFeed feed)
    {
        _env.Settings.SkippedUpdateVersion = feed.Version;
        _env.SaveSettings();
    }

    /// <summary>
    /// The setup program for this copy: the one with the .NET runtime inside when this copy
    /// carries its own runtime (so it keeps working without the shared one), otherwise the
    /// small one. A portable copy is not installed, so it has none.
    /// </summary>
    public UpdatePackage? InstallerFor(UpdateFeed feed) =>
        _env.IsPortable ? null
        : AppInfo.CarriesRuntime ? feed.OfflineInstaller ?? feed.Installer
        : feed.Installer ?? feed.OfflineInstaller;

    /// <summary>Where a downloaded setup program waits until it runs.</summary>
    public static string DownloadPath(UpdateFeed feed, UpdatePackage package)
    {
        // Never a name taken from the update file as it is: only its last part, and only an .exe.
        string name = Path.GetFileName(package.File.Split('?', '#')[0]);
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = $"LabelAxis-Setup-{feed.Version}.exe";
        }

        return Path.Combine(Path.GetTempPath(), "LabelAxis-Update", name);
    }

    /// <summary>
    /// What the setup program is told when it runs as an update: no questions, close LabelAxis
    /// if it is still open, never restart Windows, start LabelAxis again afterwards, and use the
    /// interface language for its own window where it has one.
    /// </summary>
    public static string SilentInstallArguments(string languageCode) =>
        "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH=1" +
        (languageCode == "ar" ? " /LANG=arabic" : string.Empty);
}
