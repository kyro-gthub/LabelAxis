using System.Threading.Channels;

namespace LabelMaker.Core.Automation;

/// <summary>
/// A folder other programs drop print requests into: a <c>.json</c> request (see
/// <see cref="PrintRequest"/>) or a <c>.csv</c> / <c>.txt</c> list of records, printed with the
/// default design. Each file is printed once, in the order they arrive, then moved into
/// <c>Done</c> (with the PDF, when one was asked for) or <c>Failed</c> with a note saying why.
/// </summary>
public sealed class PrintWatchFolder : IAsyncDisposable
{
    public const string DoneFolder = "Done";
    public const string FailedFolder = "Failed";

    private static readonly string[] Extensions = { ".json", ".csv", ".txt", ".tsv" };

    private readonly IPrintRequestHandler _handler;
    private readonly FileSystemWatcher _watcher;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);

    // Files that were handled but could not be moved away, so they are not printed again.
    private readonly Dictionary<string, DateTime> _handled = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private readonly Timer _sweep;

    public PrintWatchFolder(IPrintRequestHandler handler, string folder)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(Path.Combine(Folder, DoneFolder));
        Directory.CreateDirectory(Path.Combine(Folder, FailedFolder));

        _watcher = new FileSystemWatcher(Folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        _watcher.Created += (_, e) => Enqueue(e.FullPath);
        _watcher.Renamed += (_, e) => Enqueue(e.FullPath);
        _watcher.Changed += (_, e) => Enqueue(e.FullPath);
        _watcher.EnableRaisingEvents = true;

        _worker = Task.Run(Work);
        // A watcher can miss files (many at once, network folders): look every few seconds too.
        _sweep = new Timer(_ => Sweep(), null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }

    public string Folder { get; }

    /// <summary>A file was printed or failed: its name, and what happened.</summary>
    public event EventHandler<(string File, PrintRequestResult Result)>? Processed;

    private void Sweep()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(Folder))
            {
                Enqueue(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder is briefly unavailable: try again next time.
        }
    }

    private void Enqueue(string path)
    {
        if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(path), Folder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_pending)
        {
            if (_handled.TryGetValue(path, out DateTime written) && File.Exists(path) && File.GetLastWriteTimeUtc(path) == written)
            {
                return;
            }

            if (!_pending.Add(path))
            {
                return;
            }
        }

        _queue.Writer.TryWrite(path);
    }

    private async Task Work()
    {
        try
        {
            await foreach (string path in _queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                try
                {
                    await Process(path).ConfigureAwait(false);
                }
                finally
                {
                    lock (_pending)
                    {
                        _pending.Remove(path);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    private async Task Process(string path)
    {
        string? text = await ReadWhenComplete(path).ConfigureAwait(false);
        if (text is null)
        {
            return; // gone, or still being written: the next sweep finds it again
        }

        string name = Path.GetFileName(path);
        PrintRequestResult result;
        try
        {
            PrintRequest request = Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? PrintRequest.FromJson(text, _handler.DefaultDesign)
                : PrintRequest.FromDelimited(text, _handler.DefaultDesign);
            result = await _handler.RunAsync(request, "watch folder: " + name, _stop.Token).ConfigureAwait(false);
        }
        catch (PrintRequestException ex)
        {
            result = new PrintRequestResult(false, ex.Message);
        }

        try
        {
            string target = Unique(Path.Combine(Folder, result.Ok ? DoneFolder : FailedFolder, name));
            File.Move(path, target);
            if (result.Pdf is { } pdf)
            {
                await File.WriteAllBytesAsync(Path.ChangeExtension(target, ".pdf"), pdf).ConfigureAwait(false);
            }

            if (!result.Ok)
            {
                await File.WriteAllTextAsync(target + ".error.txt", result.Message).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Could not move it: it stays, but is remembered so it is not printed twice.
            result = result with { Message = result.Message + " (The file could not be moved: " + ex.Message + ")" };
        }

        if (File.Exists(path))
        {
            lock (_pending)
            {
                _handled[path] = File.GetLastWriteTimeUtc(path);
            }
        }

        Processed?.Invoke(this, (name, result));
    }

    /// <summary>The file's text once whoever writes it has finished (it can be opened alone and stops growing).</summary>
    private async Task<string?> ReadWhenComplete(string path)
    {
        long lastSize = -1;
        for (int attempt = 0; attempt < 40 && !_stop.IsCancellationRequested; attempt++)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                long size = new FileInfo(path).Length;
                if (size == lastSize && size > 0)
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                    return await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                lastSize = size;
            }
            catch (IOException)
            {
                // Still open in the program writing it.
            }

            await Task.Delay(250, _stop.Token).ConfigureAwait(false);
        }

        return null;
    }

    private static string Unique(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        string stem = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        string ext = Path.GetExtension(path);
        return $"{stem} {DateTime.Now:yyyy-MM-dd HH-mm-ss-fff}{ext}";
    }

    public async ValueTask DisposeAsync()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        await _sweep.DisposeAsync().ConfigureAwait(false);
        _stop.Cancel();
        _queue.Writer.TryComplete();
        try
        {
            await _worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            // A request still printing finishes on its own.
        }

        _stop.Dispose();
    }
}
