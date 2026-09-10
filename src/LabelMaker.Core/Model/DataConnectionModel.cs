namespace LabelMaker.Core.Model;

public enum DataSourceKind
{
    Manual = 0,
    Csv = 1,
    Xlsx = 2,
    Clipboard = 3,
}

/// <summary>
/// How a project reconnects to its data on load. The imported rows are cached in
/// the project archive (<c>data/&lt;id&gt;.csv</c>) so a project opens offline
/// even if the original file has moved; <see cref="ReloadOnOpen"/> re-reads the
/// original when it is still reachable.
/// </summary>
public sealed class DataConnectionModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Data";

    public DataSourceKind Kind { get; set; } = DataSourceKind.Manual;

    /// <summary>Absolute or project-relative path to the original CSV/XLSX (null for Manual/Clipboard).</summary>
    public string? SourcePath { get; set; }

    /// <summary>Worksheet name for XLSX sources.</summary>
    public string? WorksheetName { get; set; }

    /// <summary>0-based index of the header row within the sheet.</summary>
    public int HeaderRowIndex { get; set; }

    /// <summary>Delimiter override for CSV/TSV (null = auto-detect).</summary>
    public string? Delimiter { get; set; }

    /// <summary>Text encoding name for CSV (null = detect / UTF-8).</summary>
    public string? Encoding { get; set; }

    public bool ReloadOnOpen { get; set; } = true;

    /// <summary>0-based row indices excluded during import (spec §35).</summary>
    public List<int> ExcludedRows { get; set; } = new();

    /// <summary>Optional rename map: source column name → display field name.</summary>
    public Dictionary<string, string> ColumnRenames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Simple "column = value" / "column contains value" filters applied on load.</summary>
    public List<DataFilter> Filters { get; set; } = new();

    /// <summary>Column to sort by after load (null = source order).</summary>
    public string? SortColumn { get; set; }

    public bool SortDescending { get; set; }

    public DataConnectionModel Clone() => new()
    {
        Id = Id,
        Name = Name,
        Kind = Kind,
        SourcePath = SourcePath,
        WorksheetName = WorksheetName,
        HeaderRowIndex = HeaderRowIndex,
        Delimiter = Delimiter,
        Encoding = Encoding,
        ReloadOnOpen = ReloadOnOpen,
        ExcludedRows = new List<int>(ExcludedRows),
        ColumnRenames = new Dictionary<string, string>(ColumnRenames, StringComparer.OrdinalIgnoreCase),
        Filters = Filters.Select(f => f.Clone()).ToList(),
        SortColumn = SortColumn,
        SortDescending = SortDescending,
    };
}

public enum DataFilterOperator
{
    Equals = 0,
    NotEquals = 1,
    Contains = 2,
    StartsWith = 3,
    GreaterThan = 4,
    LessThan = 5,
    IsEmpty = 6,
    IsNotEmpty = 7,
}

public sealed class DataFilter
{
    public string Column { get; set; } = string.Empty;
    public DataFilterOperator Operator { get; set; } = DataFilterOperator.Equals;
    public string Value { get; set; } = string.Empty;

    public DataFilter Clone() => (DataFilter)MemberwiseClone();
}
