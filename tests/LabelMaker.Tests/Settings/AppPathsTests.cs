using FluentAssertions;
using LabelMaker.Core.Settings;
using Xunit;

namespace LabelMaker.Tests.Settings;

public sealed class AppPathsTests
{
    [Fact]
    public void Data_from_the_old_LabelMaker_folder_carries_over_to_LabelAxis()
    {
        string root = Path.Combine(Path.GetTempPath(), "labelaxis-paths-" + Guid.NewGuid().ToString("N"));
        try
        {
            string legacy = Path.Combine(root, AppPaths.LegacyFolderName);
            Directory.CreateDirectory(Path.Combine(legacy, "templates"));
            File.WriteAllText(Path.Combine(legacy, "settings.json"), "{\"DarkTheme\": true}");
            File.WriteAllText(Path.Combine(legacy, "templates", "mine.json"), "{}");

            var paths = new AppPaths(root, root);

            paths.RoamingDirectory.Should().EndWith(AppPaths.AppFolderName);
            File.ReadAllText(paths.SettingsFilePath).Should().Contain("DarkTheme");
            File.Exists(Path.Combine(paths.UserTemplatesDirectory, "mine.json")).Should().BeTrue();
            Directory.Exists(legacy).Should().BeTrue("the old folder is left alone");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void An_existing_LabelAxis_folder_is_not_overwritten()
    {
        string root = Path.Combine(Path.GetTempPath(), "labelaxis-paths-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, AppPaths.LegacyFolderName));
            File.WriteAllText(Path.Combine(root, AppPaths.LegacyFolderName, "settings.json"), "old");
            Directory.CreateDirectory(Path.Combine(root, AppPaths.AppFolderName));
            File.WriteAllText(Path.Combine(root, AppPaths.AppFolderName, "settings.json"), "new");

            var paths = new AppPaths(root, root);

            File.ReadAllText(paths.SettingsFilePath).Should().Be("new");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
