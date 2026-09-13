using System.Diagnostics;
using LabelMaker.App.Services;
using LabelMaker.Core.Model;
using LabelMaker.Core.Printing;

namespace LabelMaker.App.Printing;

/// <summary>
/// Writes print and PDF jobs to the print history. A job is described when it starts
/// (so the entry holds exactly the settings that were used) and closed with how it
/// ended and how many sheets went out.
/// </summary>
public sealed class PrintLog
{
    private readonly PrintHistoryStore _store;
    private readonly PrintHistoryEntry _entry;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _finished;

    private PrintLog(PrintHistoryStore store, PrintHistoryEntry entry)
    {
        _store = store;
        _entry = entry;
    }

    public static PrintLog Start(
        PrintHistoryStore store, DocumentSession session, PrintSettings settings, PrintJobSummary summary,
        PrintHistoryKind kind, string? printerName, string? outputPath)
    {
        ProjectModel project = session.Project;
        DataConnectionModel? connection = project.DataConnections.FirstOrDefault(c => c.Id == project.ActiveDataConnectionId);

        var entry = new PrintHistoryEntry
        {
            Kind = kind,
            ProjectId = project.Metadata.Id,
            ProjectTitle = project.Metadata.Title,
            ProjectPath = session.Document.FilePath,
            TemplateId = project.Template?.Id,
            TemplateName = project.Template is { } t ? $"{t.Brand} {t.ProductCode}".Trim() : null,
            PrinterName = printerName,
            OutputPath = outputPath,
            Content = settings.Content,
            Sheets = summary.Sheets,
            TotalSheets = summary.TotalSheets,
            FirstSheet = summary.FirstSheet,
            LastSheet = summary.FirstSheet + Math.Max(0, summary.Sheets - 1),
            Copies = Math.Max(1, settings.Copies),
            Labels = summary.Labels,
            FirstRecord = summary.FirstRecord,
            LastRecord = summary.LastRecord,
            DataSourceName = settings.Content == PrintContent.VariableData ? connection?.Name : null,
            DataRecordCount = session.RecordCount,
            Settings = settings.Clone(),
        };

        return new PrintLog(store, entry);
    }

    public void Completed() => Finish(PrintHistoryStatus.Completed, _entry.Sheets, null);

    public void Cancelled(int sheetsDone) => Finish(PrintHistoryStatus.Cancelled, sheetsDone, null);

    public void Failed(int sheetsDone, Exception error) => Finish(PrintHistoryStatus.Failed, sheetsDone, error.Message);

    private void Finish(PrintHistoryStatus status, int sheetsDone, string? error)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _entry.Status = status;
        _entry.SheetsDone = Math.Clamp(sheetsDone, 0, _entry.Sheets);
        _entry.Error = error;
        _entry.DurationSeconds = Math.Round(_clock.Elapsed.TotalSeconds, 1);
        _store.Add(_entry);
    }
}

/// <summary>Remembers the highest count reported while passing every report on.</summary>
public sealed class CountingProgress : IProgress<int>
{
    private readonly IProgress<int>? _inner;
    private int _max;

    public CountingProgress(IProgress<int>? inner) => _inner = inner;

    public int Max => Volatile.Read(ref _max);

    public void Report(int value)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref _max);
        }
        while (value > seen && Interlocked.CompareExchange(ref _max, value, seen) != seen);

        _inner?.Report(value);
    }
}
