using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LabelMaker.App.Services;

/// <summary>The product's name, version and description, said the same way everywhere.</summary>
public static class AppInfo
{
    public const string Name = "LabelAxis";

    public const string Tagline = "Label design and printing software";

    public const string Description =
        "Design labels, stickers and barcodes, and print them precisely on label sheets, " +
        "thermal label printers and network printers.";

    public const string Copyright = "Copyright © 2026 LabelAxis.";

    /// <summary>"1.0.0", from the build.</summary>
    public static string Version
    {
        get
        {
            string? informational = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            string version = informational ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            int plus = version.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? version[..plus] : version;
        }
    }

    /// <summary>
    /// Where this build looks for updates, set when it is built (build\installer.ps1 -UpdateUrl);
    /// empty when none was given. Settings can name another address.
    /// </summary>
    public static string BuiltInUpdateAddress =>
        typeof(AppInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpdateFeed")?.Value ?? string.Empty;

    /// <summary>
    /// True when the .NET runtime is inside the program folder (the offline installer and the
    /// portable copy); false when it uses the runtime installed in Windows (the small installer).
    /// </summary>
    public static bool CarriesRuntime =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "coreclr.dll"));

    /// <summary>"64-bit" or "32-bit".</summary>
    public static string Architecture => RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "64-bit",
        System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
        System.Runtime.InteropServices.Architecture.X86 => "32-bit",
        var other => other.ToString(),
    };

    /// <summary>What support needs to know, for "Copy details".</summary>
    public static string SupportDetails(AppEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        return string.Join(Environment.NewLine,
            $"{Name} {Version} ({Architecture}, {(env.IsPortable ? "portable" : "installed")})",
            $"Windows: {RuntimeInformation.OSDescription}",
            $".NET: {RuntimeInformation.FrameworkDescription}",
            $"Data folder: {env.Paths.RoamingDirectory}");
    }
}
