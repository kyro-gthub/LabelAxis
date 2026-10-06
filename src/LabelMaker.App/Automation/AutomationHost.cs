using System.Collections.ObjectModel;
using System.IO;
using System.Net.Sockets;
using System.Windows.Threading;
using LabelMaker.App.Localization;
using LabelMaker.App.Services;
using LabelMaker.Core.Automation;
using LabelMaker.Core.Settings;

namespace LabelMaker.App.Automation;

/// <summary>
/// Keeps the watch folder and the local web link running as Settings say, while LabelAxis is
/// open, and keeps the list of recent requests (also written to print-requests.log).
/// </summary>
public sealed class AutomationHost : IAsyncDisposable
{
    private const int MaxLog = 200;

    private readonly AppEnvironment _env;
    private readonly Dispatcher _dispatcher;
    private LocalPrintLink? _link;
    private PrintWatchFolder? _folder;

    public AutomationHost(AppEnvironment env, Dispatcher dispatcher)
    {
        _env = env;
        _dispatcher = dispatcher;
        Runner = new PrintRequestRunner(env, dispatcher);
        Runner.Finished += (_, entry) => _dispatcher.BeginInvoke(() => Add(entry));
    }

    public PrintRequestRunner Runner { get; }

    /// <summary>The recent requests, newest first.</summary>
    public ObservableCollection<AutomationLogEntry> Log { get; } = new();

    /// <summary>What is running, in a sentence for the settings window.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>Why something could not start (port taken, folder missing…), or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>The web link's address on this PC while it runs.</summary>
    public string? Address => _link?.Address;

    public event EventHandler? Changed;

    /// <summary>Starts or stops the folder and the link to match the settings now.</summary>
    public async Task ApplyAsync()
    {
        await StopAsync().ConfigureAwait(true);
        AutomationSettings s = _env.Settings.Automation;
        var running = new List<string>();
        var problems = new List<string>();

        if (s.WatchFolderEnabled)
        {
            if (string.IsNullOrWhiteSpace(s.WatchFolder))
            {
                problems.Add(Localizer.T("Choose the folder to watch."));
            }
            else
            {
                try
                {
                    _folder = new PrintWatchFolder(Runner, s.WatchFolder);
                    running.Add(Localizer.F("Watching {0}", _folder.Folder));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    problems.Add(Localizer.F("The folder cannot be watched: {0}", ex.Message));
                }
            }
        }

        if (s.WebLinkEnabled)
        {
            try
            {
                string? key = s.AllowOtherComputers ? s.EnsureAccessKey() : s.AccessKey;
                _link = new LocalPrintLink(Runner, s.WebLinkPort, s.AllowOtherComputers, key);
                _link.Start();
                running.Add(Localizer.F("Taking requests at {0}", _link.Address) +
                            (s.AllowOtherComputers ? " " + Localizer.T("(and from other computers with the key)") : string.Empty));
            }
            catch (SocketException ex)
            {
                _link = null;
                problems.Add(Localizer.F("Port {0} is already used by another program; choose another port.", s.WebLinkPort) + $" ({ex.SocketErrorCode})");
            }
            catch (Exception ex) when (ex is ArgumentException)
            {
                _link = null;
                problems.Add(ex.Message);
            }
        }

        Status = running.Count == 0 ? Localizer.T("Off: other programs cannot send print requests.") : string.Join(" · ", running);
        Problem = problems.Count == 0 ? null : string.Join(" ", problems);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task StopAsync()
    {
        if (_link is not null)
        {
            await _link.DisposeAsync().ConfigureAwait(true);
            _link = null;
        }

        if (_folder is not null)
        {
            await _folder.DisposeAsync().ConfigureAwait(true);
            _folder = null;
        }
    }

    private void Add(AutomationLogEntry entry)
    {
        Log.Insert(0, entry);
        while (Log.Count > MaxLog)
        {
            Log.RemoveAt(Log.Count - 1);
        }

        try
        {
            Directory.CreateDirectory(_env.Paths.LogDirectory);
            File.AppendAllText(Path.Combine(_env.Paths.LogDirectory, "print-requests.log"),
                $"{entry.When:yyyy-MM-dd HH:mm:ss}\t{(entry.Ok ? "OK" : "FAILED")}\t{entry.Source}\t{entry.What}\t{entry.Message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log on screen is enough.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
