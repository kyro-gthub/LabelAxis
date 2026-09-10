namespace LabelMaker.Core.Model;

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

    /// <summary>Uniform output scale. 100 = physical size. Kept for correction only.</summary>
    public double ScalePercent { get; set; } = 100.0;

    public bool PrintLabelOutlines { get; set; }

    public bool PrintCropMarks { get; set; }

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
        ScalePercent = ScalePercent,
        PrintLabelOutlines = PrintLabelOutlines,
        PrintCropMarks = PrintCropMarks,
    };
}
