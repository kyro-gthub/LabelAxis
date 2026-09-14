using LabelMaker.Core.Data;
using LabelMaker.Core.Model;

namespace LabelMaker.Export;

public enum ExportScope
{
    /// <summary>One page per Formtec sheet, labels laid out in their slots.</summary>
    Sheet = 0,

    /// <summary>One page per label, cropped to the label size.</summary>
    IndividualLabels = 1,

    /// <summary>One page per data record at label size (ignores sheet slots).</summary>
    PerRecord = 2,
}

/// <summary>Inputs for a PDF / image export job.</summary>
public sealed class ExportRequest
{
    public required ProjectModel Project { get; init; }

    public DataSet? Data { get; init; }

    public ExportScope Scope { get; init; } = ExportScope.Sheet;

    /// <summary>Raster DPI for PNG/JPEG (PDF stays vector). 300 / 600 / custom.</summary>
    public int Dpi { get; init; } = 300;

    public int StartPosition { get; init; } = 1;

    public IReadOnlySet<int>? SkipPositions { get; init; }

    public IReadOnlyList<int>? RecordSelection { get; init; }

    /// <summary>Draw a hairline label outline (useful for die-cutting / alignment checks).</summary>
    public bool DrawLabelOutlines { get; init; }

    public bool DrawCropMarks { get; init; }

    /// <summary>1-based range of sheets to write (null = from the first / to the last). Sheet scope only.</summary>
    public int? FirstSheet { get; init; }

    public int? LastSheet { get; init; }

    /// <summary>Fill every sheet slot with the master label when there is no data.</summary>
    public bool FillSheetWhenNoData { get; init; } = true;

    public string Title { get; init; } = "LabelAxis export";
}
