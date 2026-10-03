using System.Text.Json.Serialization;
using LabelMaker.Core.Printing.Thermal;

namespace LabelMaker.Core.Model;

public enum PagesToPrint
{
    /// <summary>Every page (the master label too when it has content), or the advanced range.</summary>
    All = 0,

    /// <summary>Only the page that was on screen when Print was opened.</summary>
    Current = 1,

    /// <summary>The pages ticked in the Print dialog.</summary>
    Chosen = 2,
}

public enum PrintContent
{
    /// <summary>Repeat the master label on every slot (optionally with variable data).</summary>
    SheetFill = 0,

    /// <summary>One label per data record, flowed across slots and sheets.</summary>
    VariableData = 1,

    /// <summary>Print the standalone pages instead of the master/dataset.</summary>
    Pages = 2,
}

/// <summary>
/// Persisted print job configuration. Physical accuracy rule: <see cref="ScalePercent"/>
/// defaults to 100 and "fit to page" is never applied automatically (spec §16).
/// </summary>
public sealed class PrintSettings
{
    public string? PrinterName { get; set; }

    public string? PaperSizeName { get; set; }

    public bool Landscape { get; set; }

    public int Copies { get; set; } = 1;

    public PrintContent Content { get; set; } = PrintContent.VariableData;

    /// <summary>1-based sheet slot to start printing at (skip already-used labels — spec §15).</summary>
    public int StartPosition { get; set; } = 1;

    /// <summary>1-based slot positions to skip on the first sheet.</summary>
    public List<int> SkipPositions { get; set; } = new();

    /// <summary>Explicit 1-based slot positions to print (empty = all from StartPosition).</summary>
    public List<int> SelectedPositions { get; set; } = new();

    /// <summary>0-based record range to print (null = all).</summary>
    public int? FirstRecord { get; set; }
    public int? LastRecord { get; set; }

    /// <summary>Explicit 0-based record indices to print (empty = use range).</summary>
    public List<int> SelectedRecords { get; set; } = new();

    /// <summary>
    /// When printing pages: all of them, just the page that was on screen, or pages ticked
    /// in the Print dialog. For this job only, like the sheet range.
    /// </summary>
    [JsonIgnore]
    public PagesToPrint PagesToPrint { get; set; }

    /// <summary>The page on screen, as its position in the Pages panel (0 = the master label).</summary>
    [JsonIgnore]
    public int CurrentPage { get; set; }

    /// <summary>Pages ticked for <see cref="PagesToPrint.Chosen"/>, as Pages panel positions (0 = the master label).</summary>
    [JsonIgnore]
    public List<int> ChosenPages { get; set; } = new();

    /// <summary>Uniform output scale. 100 = physical size. Kept for correction only.</summary>
    public double ScalePercent { get; set; } = 100.0;

    public bool PrintLabelOutlines { get; set; }

    public bool PrintCropMarks { get; set; }

    /// <summary>Paper size, margins and gaps used instead of the sheet template's, when chosen.</summary>
    public PrintLayout Layout { get; set; } = new();

    /// <summary>How many times each standalone page is printed, one after another, when printing pages.</summary>
    public int CopiesPerPage { get; set; } = 1;

    /// <summary>Settings for thermal label printers (language, resolution, darkness, where to send).</summary>
    public ThermalPrintSettings Thermal { get; set; } = new();

    /// <summary>
    /// 1-based sheet range for this job only — reprinting a jammed sheet, or finishing a
    /// job that stopped part-way. Deliberately not saved with the project, so a range
    /// used once never silently shortens the next print.
    /// </summary>
    [JsonIgnore]
    public int? FirstSheet { get; set; }

    [JsonIgnore]
    public int? LastSheet { get; set; }

    [JsonIgnore]
    public bool HasSheetRange => FirstSheet is not null || LastSheet is not null;

    /// <summary>The sheets this job prints out of the whole job's <paramref name="sheets"/>, and the 1-based first one.</summary>
    public (IReadOnlyList<T> Sheets, int FirstSheet) ApplySheetRange<T>(IReadOnlyList<T> sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        if (!HasSheetRange || sheets.Count == 0)
        {
            return (sheets, 1);
        }

        int first = Math.Clamp(FirstSheet ?? 1, 1, sheets.Count);
        int last = Math.Clamp(LastSheet ?? sheets.Count, first, sheets.Count);
        var slice = new List<T>(last - first + 1);
        for (int i = first - 1; i < last; i++)
        {
            slice.Add(sheets[i]);
        }

        return (slice, first);
    }

    /// <summary>Overwrites every setting with <paramref name="other"/>'s (the project keeps its instance).</summary>
    public void CopyFrom(PrintSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);
        PrinterName = other.PrinterName;
        PaperSizeName = other.PaperSizeName;
        Landscape = other.Landscape;
        Copies = other.Copies;
        Content = other.Content;
        StartPosition = other.StartPosition;
        SkipPositions = new List<int>(other.SkipPositions);
        SelectedPositions = new List<int>(other.SelectedPositions);
        FirstRecord = other.FirstRecord;
        LastRecord = other.LastRecord;
        SelectedRecords = new List<int>(other.SelectedRecords);
        PagesToPrint = other.PagesToPrint;
        CurrentPage = other.CurrentPage;
        ChosenPages = new List<int>(other.ChosenPages);
        ScalePercent = other.ScalePercent;
        PrintLabelOutlines = other.PrintLabelOutlines;
        PrintCropMarks = other.PrintCropMarks;
        FirstSheet = other.FirstSheet;
        LastSheet = other.LastSheet;
        Layout = (other.Layout ?? new PrintLayout()).Clone();
        CopiesPerPage = other.CopiesPerPage;
        Thermal = (other.Thermal ?? new ThermalPrintSettings()).Clone();
    }

    public PrintSettings Clone() => new()
    {
        PrinterName = PrinterName,
        PaperSizeName = PaperSizeName,
        Landscape = Landscape,
        Copies = Copies,
        Content = Content,
        StartPosition = StartPosition,
        SkipPositions = new List<int>(SkipPositions),
        SelectedPositions = new List<int>(SelectedPositions),
        FirstRecord = FirstRecord,
        LastRecord = LastRecord,
        SelectedRecords = new List<int>(SelectedRecords),
        PagesToPrint = PagesToPrint,
        CurrentPage = CurrentPage,
        ChosenPages = new List<int>(ChosenPages),
        ScalePercent = ScalePercent,
        PrintLabelOutlines = PrintLabelOutlines,
        PrintCropMarks = PrintCropMarks,
        FirstSheet = FirstSheet,
        LastSheet = LastSheet,
        Layout = (Layout ?? new PrintLayout()).Clone(),
        CopiesPerPage = CopiesPerPage,
        Thermal = (Thermal ?? new ThermalPrintSettings()).Clone(),
    };
}
