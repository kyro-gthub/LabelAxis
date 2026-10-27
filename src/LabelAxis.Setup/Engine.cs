using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LabelAxis.Setup;

/// <summary>What the user chose on the Options page.</summary>
internal sealed class InstallChoice
{
    public bool ForEveryone { get; set; }
    public string Folder { get; set; } = string.Empty;
    public bool Associate { get; set; } = true;
    public bool Desktop { get; set; }
}

internal enum InstallStage
{
    Preparing,
    DownloadingRuntime,
    InstallingRuntime,
    Copying,
    Finishing,
}

internal sealed class InstallProgress
{
    public InstallStage Stage { get; set; }

    /// <summary>0 to 1 for the stage, or -1 when it cannot be told.</summary>
    public double Fraction { get; set; } = -1;

    public string Detail { get; set; } = string.Empty;
}

/// <summary>A step that went wrong, with the message to show.</summary>
internal sealed class SetupException : Exception
{
    public SetupException(string message) : base(message)
    {
    }

    public SetupException()
    {
    }

    public SetupException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// Runs the Inno Setup installer that Setup carries (installer\LabelAxis.iss). The window runs it
/// silently with the choices made on its pages and follows its log, one "Dest filename:" line per
/// file copied; a silent install started from LabelAxis's updater goes straight through to it.
/// </summary>
internal static class Engine
{
    private const string RuntimeUrl = "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe";

    public static bool Present => typeof(Engine).Assembly.GetManifestResourceInfo("engine.exe") is not null;

    /// <summary>Runs the installer with exactly these arguments and returns its exit code.</summary>
    public static int RunAsIs(string arguments)
    {
        string folder = NewTempFolder();
        try
        {
            using Process process = Process.Start(new ProcessStartInfo(Extract(folder), arguments) { UseShellExecute = false })!;
            process.WaitForExit();
            return process.ExitCode;
        }
        finally
        {
            TryDelete(folder);
        }
    }

    public static async Task InstallAsync(InstallChoice choice, int expectedFiles, IProgress<InstallProgress> progress, CancellationToken cancel)
    {
        string temp = NewTempFolder();
        try
        {
            progress.Report(new InstallProgress { Stage = InstallStage.Preparing });
            if (Machine.IsSmall && !Machine.DesktopRuntimeInstalled())
            {
                await GetRuntimeAsync(temp, progress, cancel).ConfigureAwait(false);
            }

            cancel.ThrowIfCancellationRequested();
            string engine = await Task.Run(() => Extract(temp), cancel).ConfigureAwait(false);
            string log = Path.Combine(temp, "install.log");
            string tasks = (choice.Associate ? "associate" : "!associate") + "," + (choice.Desktop ? "desktopicon" : "!desktopicon");
            string arguments = string.Join(" ",
                "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/SP-",
                "/LANG=" + Strings.InnoLanguage,
                choice.ForEveryone ? "/ALLUSERS" : "/CURRENTUSER",
                "/DIR=\"" + choice.Folder.TrimEnd('\\') + "\"",
                "/MERGETASKS=\"" + tasks + "\"",
                "/LOG=\"" + log + "\"");

            using Process process = Process.Start(new ProcessStartInfo(engine, arguments) { UseShellExecute = false })!;
            int copied = 0;
            bool started = false;
            long read = 0;
            var pending = new StringBuilder();
            void OnLine(string line)
            {
                if (line.Contains("Starting the installation process."))
                {
                    started = true;
                }

                int at = line.IndexOf("Dest filename: ", StringComparison.Ordinal);
                if (at >= 0)
                {
                    copied++;
                    progress.Report(new InstallProgress
                    {
                        Stage = InstallStage.Copying,
                        Fraction = expectedFiles > 0 ? Math.Min(0.99, copied / (double)expectedFiles) : -1,
                        Detail = Path.GetFileName(line.Substring(at + 15).Trim()),
                    });
                }
                else if (line.Contains("Installation process succeeded.") || (copied > 0 && line.Contains("Creating shortcut")))
                {
                    progress.Report(new InstallProgress { Stage = InstallStage.Finishing, Fraction = 1 });
                }
            }

            while (!process.WaitForExit(150))
            {
                read = ReadLog(log, read, pending, OnLine);
            }

            // What the log gained after the last look (all of it, for a quick install).
            _ = ReadLog(log, read, pending, OnLine);
            if (process.ExitCode != 0)
            {
                // Before anything was installed: the user said no to Windows' administrator prompt.
                throw new SetupException(choice.ForEveryone && !started
                    ? Strings.T("ErrorAdmin")
                    : Strings.F("ErrorExit", process.ExitCode));
            }

            progress.Report(new InstallProgress { Stage = InstallStage.Finishing, Fraction = 1 });
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>Downloads the .NET 10 Desktop Runtime from Microsoft and installs it, as the small installer's code did.</summary>
    private static async Task GetRuntimeAsync(string temp, IProgress<InstallProgress> progress, CancellationToken cancel)
    {
        string file = Path.Combine(temp, "windowsdesktop-runtime-win-x64.exe");
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(RuntimeUrl);
            request.UserAgent = "LabelAxis-Setup/" + Machine.Version;
            using CancellationTokenRegistration abort = cancel.Register(request.Abort);
            using WebResponse response = await request.GetResponseAsync().ConfigureAwait(false);
            long total = response.ContentLength;
            using Stream source = response.GetResponseStream();
            using FileStream target = File.Create(file);
            byte[] buffer = new byte[81920];
            long done = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer, 0, n, cancel).ConfigureAwait(false);
                done += n;
                progress.Report(new InstallProgress
                {
                    Stage = InstallStage.DownloadingRuntime,
                    Fraction = total > 0 ? done / (double)total : -1,
                    Detail = Strings.F("Downloading", done / (1024 * 1024), total > 0 ? total / (1024 * 1024) : 0),
                });
            }
        }
        catch (Exception ex) when (!cancel.IsCancellationRequested && ex is WebException or IOException)
        {
            throw new SetupException(Strings.T("ErrorDownload"), ex);
        }
        catch (WebException) when (cancel.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancel);
        }

        progress.Report(new InstallProgress { Stage = InstallStage.InstallingRuntime, Detail = Strings.T("InstallingRuntime") });

        // Microsoft's installer asks Windows for administrator rights itself; 3010 = done, restart
        // later; 1638 = a newer one is already there.
        int code;
        try
        {
            using Process runtime = Process.Start(new ProcessStartInfo(file, "/install /quiet /norestart") { UseShellExecute = true })!;
            await Task.Run(runtime.WaitForExit, CancellationToken.None).ConfigureAwait(false);
            code = runtime.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new SetupException(Strings.F("ErrorRuntime", ex.NativeErrorCode), ex);
        }

        if ((code != 0 && code != 3010 && code != 1638) || !Machine.DesktopRuntimeInstalled())
        {
            throw new SetupException(Strings.F("ErrorRuntime", code));
        }
    }

    /// <summary>Hands each whole new line of the log to <paramref name="line"/>; returns how far it has read.</summary>
    private static long ReadLog(string path, long from, StringBuilder pending, Action<string> line)
    {
        if (!File.Exists(path))
        {
            return from;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= from)
            {
                return from;
            }

            stream.Seek(from, SeekOrigin.Begin);
            byte[] bytes = new byte[stream.Length - from];
            int got = stream.Read(bytes, 0, bytes.Length);
            pending.Append(Encoding.UTF8.GetString(bytes, 0, got));
            string text = pending.ToString();
            int end = text.LastIndexOf('\n');
            if (end >= 0)
            {
                foreach (string l in text.Substring(0, end).Split('\n'))
                {
                    line(l.TrimEnd('\r'));
                }

                pending.Clear().Append(text.Substring(end + 1));
            }

            return from + got;
        }
        catch (IOException)
        {
            return from;
        }
    }

    private static string Extract(string folder)
    {
        string path = Path.Combine(folder, "LabelAxis-Setup-engine.exe");
        using Stream source = typeof(Engine).Assembly.GetManifestResourceStream("engine.exe")
                              ?? throw new SetupException(Strings.T("ErrorMissing"));
        using FileStream target = File.Create(path);
        source.CopyTo(target);
        return path;
    }

    private static string NewTempFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LabelAxis-Setup-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture).Substring(0, 8));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Windows clears its temporary folder in time.
        }
    }
}
