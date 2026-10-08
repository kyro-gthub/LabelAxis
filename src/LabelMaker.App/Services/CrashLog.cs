using System.IO;

namespace LabelMaker.App.Services;

/// <summary>The crash log (crash.log in the log folder): errors nobody expected, with where they happened.</summary>
public static class CrashLog
{
    /// <summary>The folder the log is kept in; set at start-up. Nothing is written before that.</summary>
    public static string? Directory { get; set; }

    /// <summary>Adds <paramref name="ex"/> to the log. Never fails: a log that cannot be written is left alone.</summary>
    public static void Write(Exception ex)
    {
        if (Directory is not { } folder)
        {
            return;
        }

        try
        {
            System.IO.Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "crash.log"), $"{DateTimeOffset.Now:u}\n{ex}\n\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // The log is a help, not a requirement.
        }
    }
}
