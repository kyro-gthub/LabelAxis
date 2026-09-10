namespace LabelMaker.Core.Data.Import;

/// <summary>
/// Raw grid read from a file before header selection / renaming / filtering.
/// Keeps the importer dependencies (CsvHelper, ClosedXML) behind one interface so
/// they never leak into the rest of the engine.
/// </summary>
public sealed class RawTable
{
    public required IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }

    /// <summary>Sheet names for spreadsheet sources; empty for text sources.</summary>
    public IReadOnlyList<string> WorksheetNames { get; init; } = Array.Empty<string>();

    public string? WorksheetName { get; init; }

    public int RowCount => Rows.Count;

    public int MaxColumns => Rows.Count == 0 ? 0 : Rows.Max(r => r.Count);
}

public interface ITabularReader
{
    /// <summary>Read the whole source into a raw string grid (no type coercion).</summary>
    RawTable Read(Stream stream, TabularReadOptions options);
}

public sealed class TabularReadOptions
{
    /// <summary>Explicit delimiter for text sources (null = auto-detect , ; tab |).</summary>
    public string? Delimiter { get; set; }

    /// <summary>Encoding name for text sources (null = detect BOM / UTF-8).</summary>
    public string? EncodingName { get; set; }

    /// <summary>Worksheet to read for spreadsheet sources (null = first).</summary>
    public string? WorksheetName { get; set; }

    /// <summary>Maximum rows to read (0 = all). Used for the preview step.</summary>
    public int MaxRows { get; set; }
}
