using System.Text.Json;
using System.Text.Json.Serialization;
using LabelMaker.Core.Model;

namespace LabelMaker.Core.Printing;

public enum PrintHistoryKind
{
    /// <summary>Sent to a printer.</summary>
    Printer = 0,

    /// <summary>Written to a PDF file.</summary>
    Pdf = 1,
}

public enum PrintHistoryStatus
{
    Completed = 0,
    Cancelled = 1,
    Failed = 2,
}

/// <summary>A job from the history to send again: all of it, or only the sheets that never went out.</summary>
public sealed record ReprintRequest(PrintHistoryEntry Entry, bool RemainingOnly)
{
    /// <summary>The settings to print with, sheet range included.</summary>
    public PrintSettings BuildSettings()
    {
        PrintSettings settings = Entry.Settings.Clone();
        settings.PrinterName = Entry.PrinterName ?? settings.PrinterName;

        // The sheet range is not part of the saved settings, so it comes from the entry.
        if (RemainingOnly && Entry.IsIncomplete)
        {
            settings.FirstSheet = Entry.ResumeSheet;
            settings.LastSheet = Entry.LastSheet;
        }
        else if (Entry.HadSheetRange)
        {
            settings.FirstSheet = Entry.FirstSheet;
            settings.LastSheet = Entry.LastSheet;
        }
        else
        {
            settings.FirstSheet = null;
            settings.LastSheet = null;
        }

        return settings;
    }
}

/// <summary>What a job contains, worked out before it is sent: for the dialog, the log and reprints.</summary>
public sealed record PrintJobSummary
{
    /// <summary>Sheets in this job (after any sheet range).</summary>
    public int Sheets { get; init; }

    /// <summary>Sheets the whole job would have without a sheet range.</summary>
    public int TotalSheets { get; init; }

    /// <summary>Labels printed across <see cref="Sheets"/>.</summary>
    public int Labels { get; init; }

    /// <summary>First and last data record printed (0-based), when the job prints records.</summary>
    public int? FirstRecord { get; init; }

    public int? LastRecord { get; init; }

    /// <summary>1-based position of this job's first sheet within the whole job.</summary>
    public int FirstSheet { get; init; } = 1;

    public static PrintJobSummary FromSheets(IReadOnlyList<PlannedSheet> all, IReadOnlyList<PlannedSheet> printed, int firstSheet)
    {
        int labels = 0;
        int? lo = null;
        int? hi = null;
        foreach (PlannedSheet sheet in printed)
        {
            labels += sheet.Labels.Count;
            foreach (Bulk.PlacedLabel label in sheet.Labels)
            {
                if (label.RecordIndex < 0)
                {
                    continue;
                }

                lo = lo is null ? label.RecordIndex : Math.Min(lo.Value, label.RecordIndex);
                hi = hi is null ? label.RecordIndex : Math.Max(hi.Value, label.RecordIndex);
            }
        }

        return new PrintJobSummary
        {
            Sheets = printed.Count,
            TotalSheets = all.Count,
            Labels = labels,
            FirstRecord = lo,
            LastRecord = hi,
            FirstSheet = firstSheet,
        };
    }
}

/// <summary>
/// One line of the print log: what was printed, where, when, from which project and
/// records, and whether it finished. Carries a copy of the print settings so the job
/// can be sent again exactly as it was, or resumed from the sheet it stopped at.
/// </summary>
public sealed class PrintHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;

    public double DurationSeconds { get; set; }

    public PrintHistoryKind Kind { get; set; }

    public PrintHistoryStatus Status { get; set; }

    public string? Error { get; set; }

    public string ProjectId { get; set; } = string.Empty;

    public string ProjectTitle { get; set; } = string.Empty;

    /// <summary>Where the project was saved when it printed (null for an unsaved project).</summary>
    public string? ProjectPath { get; set; }

    public string? TemplateId { get; set; }

    public string? TemplateName { get; set; }

    public string? PrinterName { get; set; }

    /// <summary>The PDF written, for <see cref="PrintHistoryKind.Pdf"/> jobs.</summary>
    public string? OutputPath { get; set; }

    public PrintContent Content { get; set; }

    public int Sheets { get; set; }

    /// <summary>Sheets handed over before the job ended; equals <see cref="Sheets"/> when completed.</summary>
    public int SheetsDone { get; set; }

    /// <summary>1-based first and last sheet of the whole job that this entry covered.</summary>
    public int FirstSheet { get; set; } = 1;

    public int LastSheet { get; set; } = 1;

    /// <summary>Sheets in the whole job, of which this entry printed <see cref="FirstSheet"/>–<see cref="LastSheet"/>.</summary>
    public int TotalSheets { get; set; }

    [JsonIgnore]
    public bool HadSheetRange => FirstSheet > 1 || (TotalSheets > 0 && LastSheet < TotalSheets);

    public int Copies { get; set; } = 1;

    public int Labels { get; set; }

    public int? FirstRecord { get; set; }

    public int? LastRecord { get; set; }

    public string? DataSourceName { get; set; }

    /// <summary>Records loaded when the job ran; a different count on reprint means the data changed.</summary>
    public int DataRecordCount { get; set; }

    public PrintSettings Settings { get; set; } = new();

    /// <summary>True when the job stopped part-way and some sheets never went out.</summary>
    [JsonIgnore]
    public bool IsIncomplete => Status != PrintHistoryStatus.Completed && SheetsDone < Sheets;

    /// <summary>The first sheet (1-based, whole-job numbering) that did not go out.</summary>
    [JsonIgnore]
    public int ResumeSheet => FirstSheet + Math.Clamp(SheetsDone, 0, Math.Max(0, Sheets));

    [JsonIgnore]
    public string RecordsText => FirstRecord is { } lo && LastRecord is { } hi
        ? lo == hi ? $"record {lo + 1:N0}" : $"records {lo + 1:N0}–{hi + 1:N0}"
        : Content == PrintContent.Pages ? "standalone pages" : "no data";

    [JsonIgnore]
    public string SheetsText
    {
        get
        {
            string range = HadSheetRange ? $" (sheets {FirstSheet:N0}–{LastSheet:N0} of {TotalSheets:N0})" : string.Empty;
            string copies = Copies > 1 ? $" × {Copies}" : string.Empty;
            return $"{Sheets:N0} sheet{(Sheets == 1 ? "" : "s")}{copies}{range}";
        }
    }
}

/// <summary>
/// The print log, newest first, kept as a JSON file beside the other per-user data.
/// Old entries fall off the end so the file stays small. A damaged file is set aside
/// and a fresh log started: history must never stop anyone printing.
/// </summary>
public sealed class PrintHistoryStore
{
    public const int DefaultMaxEntries = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly List<PrintHistoryEntry> _entries = new();
    private readonly object _gate = new();

    public string? FilePath { get; private set; }

    public int MaxEntries { get; init; } = DefaultMaxEntries;

    public event EventHandler? Changed;

    /// <summary>A snapshot, newest first.</summary>
    public IReadOnlyList<PrintHistoryEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    public static PrintHistoryStore Load(string path)
    {
        var store = new PrintHistoryStore { FilePath = path };
        if (!File.Exists(path))
        {
            return store;
        }

        try
        {
            List<PrintHistoryEntry> list = JsonSerializer.Deserialize<List<PrintHistoryEntry>>(File.ReadAllText(path), JsonOptions) ?? new();
            store._entries.AddRange(list.OrderByDescending(e => e.StartedUtc));
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            try
            {
                File.Move(path, path + ".damaged-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
            }
            catch (IOException)
            {
                // leave it; the next save overwrites it
            }
        }

        return store;
    }

    public void Add(PrintHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
            }
        }

        SaveQuietly();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(string id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _entries.RemoveAll(e => e.Id == id) > 0;
        }

        if (removed)
        {
            SaveQuietly();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }

        SaveQuietly();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Save(string? path = null)
    {
        path ??= FilePath ?? throw new InvalidOperationException("No history file path set.");
        FilePath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        string json;
        lock (_gate)
        {
            json = JsonSerializer.Serialize(_entries, JsonOptions);
        }

        string tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(path))
        {
            File.Replace(tmp, path, null);
        }
        else
        {
            File.Move(tmp, path);
        }
    }

    private void SaveQuietly()
    {
        if (FilePath is null)
        {
            return;
        }

        try
        {
            Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log is a convenience; a locked or read-only file must not fail the print.
        }
    }
}
