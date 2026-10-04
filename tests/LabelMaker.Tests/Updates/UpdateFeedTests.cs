using FluentAssertions;
using LabelMaker.Core.Updates;
using Xunit;

namespace LabelMaker.Tests.Updates;

public sealed class UpdateFeedTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "labelaxis-update-tests-" + Guid.NewGuid().ToString("N"));

    public UpdateFeedTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void The_update_file_is_read_and_compared_with_the_running_version()
    {
        UpdateFeed feed = UpdateFeed.Parse("""
            {
              "version": "1.2.0",
              "notes": "Faster printing.",
              "notesByLanguage": { "ar": "طباعة أسرع." },
              "installer": { "file": "LabelAxis-Setup-1.2.0.exe", "sha256": "AB", "size": 10 }
            }
            """);

        feed.IsNewerThan(new Version(1, 1, 0)).Should().BeTrue();
        feed.IsNewerThan(new Version(1, 2, 0, 0)).Should().BeFalse("1.2.0 and 1.2.0.0 are the same version");
        feed.IsNewerThan(new Version(2, 0)).Should().BeFalse();
        feed.NotesFor("ar").Should().Be("طباعة أسرع.");
        feed.NotesFor("ckb").Should().Be("Faster printing.");
    }

    [Fact]
    public void A_file_without_a_version_is_refused()
    {
        FluentActions.Invoking(() => UpdateFeed.Parse("""{ "version": "soon" }""")).Should().Throw<FormatException>();
    }

    [Fact]
    public void Addresses_can_be_web_addresses_or_folders_but_not_plain_http()
    {
        UpdateSource.ToFeedUri("https://example.com/labelaxis/").AbsoluteUri.Should().Be("https://example.com/labelaxis/update.json");
        UpdateSource.ToFeedUri("https://example.com/feed.json").AbsoluteUri.Should().Be("https://example.com/feed.json");
        UpdateSource.ToFeedUri(@"\\server\share\LabelAxis").IsUnc.Should().BeTrue();
        UpdateSource.ToFeedUri(@"\\server\share\LabelAxis").LocalPath.Should().EndWith(@"LabelAxis\update.json");
        UpdateSource.ToFeedUri("http://localhost:8080/update.json").Scheme.Should().Be("http");

        FluentActions.Invoking(() => UpdateSource.ToFeedUri("http://example.com/update.json")).Should().Throw<FormatException>();
        FluentActions.Invoking(() => UpdateSource.ToFeedUri("ftp://example.com/update.json")).Should().Throw<FormatException>();
        FluentActions.Invoking(() => UpdateSource.ToFeedUri("  ")).Should().Throw<FormatException>();
    }

    [Fact]
    public void Packages_are_found_next_to_the_update_file()
    {
        var feed = new Uri("https://example.com/releases/update.json");
        UpdateSource.Resolve(feed, new UpdatePackage { File = "LabelAxis-Setup-1.2.0.exe" }).AbsoluteUri
            .Should().Be("https://example.com/releases/LabelAxis-Setup-1.2.0.exe");
        UpdateSource.Resolve(feed, new UpdatePackage { File = "https://cdn.example.com/x.exe" }).Host.Should().Be("cdn.example.com");
        FluentActions.Invoking(() => UpdateSource.Resolve(feed, new UpdatePackage { File = "http://evil.example.com/x.exe" }))
            .Should().Throw<FormatException>();
    }

    [Fact]
    public async Task A_download_from_a_folder_is_kept_only_when_its_fingerprint_matches()
    {
        string source = Path.Combine(_dir, "LabelAxis-Setup-9.9.9.exe");
        await File.WriteAllBytesAsync(source, new byte[] { 1, 2, 3, 4, 5 });
        string json = new UpdateFeed
        {
            Version = "9.9.9",
            Installer = new UpdatePackage { File = Path.GetFileName(source), Sha256 = UpdateSource.Fingerprint(source), Size = 5 },
        }.ToJson();
        await File.WriteAllTextAsync(Path.Combine(_dir, "update.json"), json);

        Uri feedUri = UpdateSource.ToFeedUri(_dir);
        UpdateFeed feed = await UpdateSource.ReadFeedAsync(feedUri, CancellationToken.None);
        string target = Path.Combine(_dir, "downloads", "setup.exe");
        long reported = 0;
        await UpdateSource.DownloadAsync(UpdateSource.Resolve(feedUri, feed.Installer!), feed.Installer!, target,
            new Progress<long>(b => reported = b), CancellationToken.None);

        File.ReadAllBytes(target).Should().Equal(1, 2, 3, 4, 5);

        // An altered file is thrown away.
        var wrong = feed.Installer! with { Sha256 = new string('0', 64) };
        string bad = Path.Combine(_dir, "downloads", "bad.exe");
        await FluentActions.Awaiting(() => UpdateSource.DownloadAsync(UpdateSource.Resolve(feedUri, wrong), wrong, bad, null, CancellationToken.None))
            .Should().ThrowAsync<InvalidDataException>();
        File.Exists(bad).Should().BeFalse();
        File.Exists(bad + ".part").Should().BeFalse();

        // So is one without a fingerprint.
        var unsigned = feed.Installer! with { Sha256 = string.Empty };
        await FluentActions.Awaiting(() => UpdateSource.DownloadAsync(UpdateSource.Resolve(feedUri, unsigned), unsigned, bad, null, CancellationToken.None))
            .Should().ThrowAsync<InvalidDataException>();
    }
}
