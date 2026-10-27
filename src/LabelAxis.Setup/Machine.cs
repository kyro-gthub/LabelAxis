using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace LabelAxis.Setup;

/// <summary>A LabelAxis already installed on this PC, as Inno Setup recorded it.</summary>
internal sealed class InstalledCopy
{
    public string Folder { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool ForEveryone { get; set; }
    public bool Associate { get; set; } = true;
    public bool Desktop { get; set; }
}

/// <summary>What Setup needs to know about this PC and about itself.</summary>
internal static class Machine
{
    // installer\LabelAxis.iss's AppId; Inno Setup keeps the install under this key.
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{7F3B9A54-2C1E-4E6A-9E2D-4B1C7F0A9D31}_is1";

    public static string Version { get; } = Metadata<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";

    /// <summary>"small" gets .NET 10 from Microsoft when the PC needs it; "offline" carries it.</summary>
    public static string Flavour { get; } = Value("SetupFlavour", "small");

    public static int SizeMb { get; } = int.TryParse(Value("SetupSizeMb", "0"), out int mb) ? mb : 0;

    /// <summary>How many files the installer copies, for the progress bar.</summary>
    public static int FileCount { get; } = int.TryParse(Value("SetupFiles", "0"), out int n) ? n : 0;

    public static bool IsSmall => !string.Equals(Flavour, "offline", StringComparison.OrdinalIgnoreCase);

    public static InstalledCopy? FindInstalled()
    {
        // No tuples here: they need .NET Framework 4.7, and Windows 10 1607 comes with 4.6.2.
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            bool everyone = hive == RegistryHive.LocalMachine;
            try
            {
                using RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using RegistryKey? key = root.OpenSubKey(UninstallKey);
                if (key?.GetValue("InstallLocation") is string folder && folder.Length > 0)
                {
                    string tasks = key.GetValue("Inno Setup: Selected Tasks") as string ?? "associate";
                    string[] chosen = tasks.Split(',').Select(t => t.Trim()).ToArray();
                    return new InstalledCopy
                    {
                        Folder = folder.TrimEnd('\\'),
                        Version = key.GetValue("DisplayVersion") as string ?? string.Empty,
                        ForEveryone = everyone,
                        Associate = chosen.Contains("associate", StringComparer.OrdinalIgnoreCase),
                        Desktop = chosen.Contains("desktopicon", StringComparer.OrdinalIgnoreCase),
                    };
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                // Not readable: treat as not installed there.
            }
        }

        return null;
    }

    /// <summary>Where Inno Setup puts LabelAxis by default ({autopf}).</summary>
    public static string DefaultFolder(bool forEveryone) => forEveryone
        ? Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LabelAxis")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LabelAxis");

    /// <summary>The .NET 10 Desktop Runtime is installed for all users under Program Files\dotnet (as the .iss checks).</summary>
    public static bool DesktopRuntimeInstalled()
    {
        string programs = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string shared = Path.Combine(programs, "dotnet", "shared", "Microsoft.WindowsDesktop.App");
        try
        {
            return Directory.Exists(shared) && Directory.GetDirectories(shared, "10.*").Length > 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public static bool AppIsRunning()
    {
        Process[] running = Process.GetProcessesByName("LabelAxis");
        foreach (Process p in running)
        {
            p.Dispose();
        }

        return running.Length > 0;
    }

    /// <summary>Free space on the drive <paramref name="folder"/> is on, in MB; -1 when it cannot be told.</summary>
    public static long FreeMb(string folder)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(folder));
            return string.IsNullOrEmpty(root) ? -1 : new DriveInfo(root).AvailableFreeSpace / (1024 * 1024);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return -1;
        }
    }

    private static T? Metadata<T>() where T : Attribute => typeof(Machine).Assembly.GetCustomAttribute<T>();

    private static string Value(string key, string fallback) =>
        typeof(Machine).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } v ? v : fallback;
}
