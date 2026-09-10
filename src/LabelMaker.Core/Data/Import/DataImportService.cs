using System.Globalization;
using System.Text;
using LabelMaker.Core.Model;

namespace LabelMaker.Core.Data.Import;

public sealed class DataPreview
{
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<IReadOnlyList<string>> SampleRows { get; init; }
    public int TotalDataRows { get; init; }
    public IReadOnlyList<string> WorksheetNames { get; init; } = Array.Empty<string>();
    public string? WorksheetName { get; init; }

    /// <summary>Column name → count of duplicate values (value appears more than once).</summary>
    public IReadOnlyDictionary<string, int> DuplicateCountsByColumn { get; init; } =
        new Dictionary<string, int>();
}

/// <summary>
/// Turns files / pasted text into a <see cref="DataSet"/>, honouring a
/// <see cref="DataConnectionModel"/> (header row, excluded rows, renames, filters,
/// sort — spec §35). All CsvHelper / ClosedXML use is confined here.
/// </summary>
public sealed class DataImportService
{
    private readonly ITabularReader _csv = new DelimitedTextReader();
    private readonly ITabularReader _xlsx = new XlsxReader();

    public RawTable ReadRaw(string path, TabularReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        options ??= new TabularReadOptions();
        using FileStream fs = File.OpenRead(path);
        return ReaderFor(path).Read(fs, options);
    }

    public RawTable ReadRaw(Stream stream, DataSourceKind kind, TabularReadOptions? options = null)
    {
        options ??= new TabularReadOptions();
        ITabularReader reader = kind == DataSourceKind.Xlsx ? _xlsx : _csv;
        return reader.Read(stream, options);
    }

    public static RawTable ReadClipboardText(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (string line in (text ?? string.Empty).Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
        {
            rows.Add(line.Split('\t'));
        }

        return new RawTable { Rows = rows };
    }

    private ITabularReader ReaderFor(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".xlsx" or ".xlsm" or ".xltx" ? _xlsx : _csv;
    }

    public DataPreview BuildPreview(RawTable raw, DataConnectionModel connection, int sampleRows = 25)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(connection);

        int headerRow = Math.Clamp(connection.HeaderRowIndex, 0, Math.Max(0, raw.RowCount - 1));
        IReadOnlyList<string> headers = raw.RowCount > headerRow
            ? NormalizeHeaders(raw.Rows[headerRow], raw.MaxColumns, connection)
            : GenerateHeaders(raw.MaxColumns);

        var dataRows = CollectDataRows(raw, connection, headerRow);
        var sample = dataRows.Take(sampleRows).ToList();

        var dupCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int c = 0; c < headers.Count; c++)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (IReadOnlyList<string> row in dataRows)
            {
                string v = c < row.Count ? row[c] : string.Empty;
                if (v.Length == 0)
                {
                    continue;
                }

                seen[v] = seen.GetValueOrDefault(v) + 1;
            }

            int dupes = seen.Values.Count(n => n > 1);
            if (dupes > 0)
            {
                dupCounts[headers[c]] = dupes;
            }
        }

        return new DataPreview
        {
            Columns = headers,
            SampleRows = sample,
            TotalDataRows = dataRows.Count,
            WorksheetNames = raw.WorksheetNames,
            WorksheetName = raw.WorksheetName,
            DuplicateCountsByColumn = dupCounts,
        };
    }

    public DataSet BuildDataSet(RawTable raw, DataConnectionModel connection)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(connection);

        int headerRow = Math.Clamp(connection.HeaderRowIndex, 0, Math.Max(0, raw.RowCount - 1));
        IReadOnlyList<string> headers = raw.RowCount > headerRow
            ? NormalizeHeaders(raw.Rows[headerRow], raw.MaxColumns, connection)
            : GenerateHeaders(raw.MaxColumns);

        List<IReadOnlyList<string>> dataRows = CollectDataRows(raw, connection, headerRow);
        dataRows = ApplyFilters(dataRows, headers, connection.Filters);
        dataRows = ApplySort(dataRows, headers, connection.SortColumn, connection.SortDescending);

        return DataSet.FromRows(headers, dataRows);
    }

    private static List<IReadOnlyList<string>> CollectDataRows(
        RawTable raw, DataConnectionModel connection, int headerRow)
    {
        var excluded = new HashSet<int>(connection.ExcludedRows);
        int width = raw.MaxColumns;
        var result = new List<IReadOnlyList<string>>();

        for (int i = headerRow + 1; i < raw.RowCount; i++)
        {
            if (excluded.Contains(i))
            {
                continue;
            }

            IReadOnlyList<string> src = raw.Rows[i];
            if (src.Count == 0 || src.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (src.Count == width)
            {
                result.Add(src);
            }
            else
            {
                var padded = new string[width];
                for (int c = 0; c < width; c++)
                {
                    padded[c] = c < src.Count ? src[c] : string.Empty;
                }

                result.Add(padded);
            }
        }

        return result;
    }

    private static IReadOnlyList<string> NormalizeHeaders(
        IReadOnlyList<string> raw, int width, DataConnectionModel connection)
    {
        var headers = new List<string>(width);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < width; i++)
        {
            string name = i < raw.Count ? (raw[i] ?? string.Empty).Trim() : string.Empty;
            if (name.Length == 0)
            {
                name = $"Column{i + 1}";
            }

            if (connection.ColumnRenames.TryGetValue(name, out string? renamed) && !string.IsNullOrWhiteSpace(renamed))
            {
                name = renamed.Trim();
            }

            string unique = name;
            int suffix = 2;
            while (!used.Add(unique))
            {
                unique = $"{name}_{suffix++}";
            }

            headers.Add(unique);
        }

        return headers;
    }

    private static IReadOnlyList<string> GenerateHeaders(int width) =>
        Enumerable.Range(1, Math.Max(1, width)).Select(i => $"Column{i}").ToList();

    private static List<IReadOnlyList<string>> ApplyFilters(
        List<IReadOnlyList<string>> rows, IReadOnlyList<string> headers, List<DataFilter> filters)
    {
        if (filters.Count == 0)
        {
            return rows;
        }

        int Index(string col) => IndexOf(headers, col);

        return rows.Where(row => filters.All(f =>
        {
            int idx = Index(f.Column);
            string v = idx >= 0 && idx < row.Count ? row[idx] : string.Empty;
            return f.Operator switch
            {
                DataFilterOperator.Equals => string.Equals(v, f.Value, StringComparison.OrdinalIgnoreCase),
                DataFilterOperator.NotEquals => !string.Equals(v, f.Value, StringComparison.OrdinalIgnoreCase),
                DataFilterOperator.Contains => v.Contains(f.Value, StringComparison.OrdinalIgnoreCase),
                DataFilterOperator.StartsWith => v.StartsWith(f.Value, StringComparison.OrdinalIgnoreCase),
                DataFilterOperator.GreaterThan => Compare(v, f.Value) > 0,
                DataFilterOperator.LessThan => Compare(v, f.Value) < 0,
                DataFilterOperator.IsEmpty => v.Length == 0,
                DataFilterOperator.IsNotEmpty => v.Length > 0,
                _ => true,
            };
        })).ToList();

        static int Compare(string a, string b)
        {
            if (double.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out double da) &&
                double.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out double db))
            {
                return da.CompareTo(db);
            }

            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static List<IReadOnlyList<string>> ApplySort(
        List<IReadOnlyList<string>> rows, IReadOnlyList<string> headers, string? sortColumn, bool descending)
    {
        if (string.IsNullOrWhiteSpace(sortColumn))
        {
            return rows;
        }

        int idx = IndexOf(headers, sortColumn);
        if (idx < 0)
        {
            return rows;
        }

        IOrderedEnumerable<IReadOnlyList<string>> ordered = descending
            ? rows.OrderByDescending(r => idx < r.Count ? r[idx] : string.Empty, DataValueComparer.Instance)
            : rows.OrderBy(r => idx < r.Count ? r[idx] : string.Empty, DataValueComparer.Instance);
        return ordered.ToList();
    }

    private static int IndexOf(IReadOnlyList<string> headers, string name)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class DataValueComparer : IComparer<string>
    {
        public static readonly DataValueComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            x ??= string.Empty;
            y ??= string.Empty;
            if (double.TryParse(x, NumberStyles.Any, CultureInfo.InvariantCulture, out double dx) &&
                double.TryParse(y, NumberStyles.Any, CultureInfo.InvariantCulture, out double dy))
            {
                return dx.CompareTo(dy);
            }

            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }
    }
}
