using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace LabelMaker.Core.Data;

public enum DataColumnType
{
    Text = 0,
    Integer = 1,
    Decimal = 2,
    Date = 3,
    Boolean = 4,
}

public sealed record DataColumn(string Name, int Index)
{
    public DataColumnType InferredType { get; set; } = DataColumnType.Text;
}

/// <summary>
/// One immutable data row. Implements <see cref="IReadOnlyDictionary{TKey,TValue}"/>
/// (field name → cell text) so it plugs straight into the variable resolver.
/// </summary>
public sealed class DataRecord : IReadOnlyDictionary<string, string>, INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");
    private static readonly PropertyChangedEventArgs RawValuesChanged = new(nameof(RawValues));

    private readonly string[] _values;
    private readonly IReadOnlyDictionary<string, int> _columnIndex;

    internal DataRecord(string[] values, IReadOnlyDictionary<string, int> columnIndex, int sourceRowNumber)
    {
        _values = values;
        _columnIndex = columnIndex;
        SourceRowNumber = sourceRowNumber;
    }

    /// <summary>1-based row number in the original file (for error messages / navigation).</summary>
    public int SourceRowNumber { get; }

    public string this[string column] =>
        _columnIndex.TryGetValue(column, out int i) && i < _values.Length ? _values[i] : string.Empty;

    public string this[int index] => index >= 0 && index < _values.Length ? _values[index] : string.Empty;

    public IEnumerable<string> Keys => _columnIndex.Keys;
    public IEnumerable<string> Values => _columnIndex.Keys.Select(k => this[k]);
    public int Count => _columnIndex.Count;

    public bool ContainsKey(string key) => _columnIndex.ContainsKey(key);

    public bool TryGetValue(string key, out string value)
    {
        if (_columnIndex.TryGetValue(key, out int i) && i < _values.Length)
        {
            value = _values[i];
            return true;
        }

        value = string.Empty;
        return false;
    }

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
        _columnIndex.Select(kv => new KeyValuePair<string, string>(kv.Key, this[kv.Value])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public string[] RawValues => _values;

    /// <summary>Raised when a cell is edited, so a bound grid shows the new text.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void RaiseValuesChanged()
    {
        PropertyChanged?.Invoke(this, IndexerChanged);
        PropertyChanged?.Invoke(this, RawValuesChanged);
    }
}

/// <summary>An in-memory table backing variable-data printing. Rows are an array — the UI virtualises.</summary>
public sealed class DataSet
{
    private readonly Dictionary<string, int> _columnIndex;
    private readonly List<DataRecord> _rows;

    public DataSet(IReadOnlyList<DataColumn> columns, IReadOnlyList<DataRecord> rows)
    {
        Columns = columns;
        _rows = rows as List<DataRecord> ?? rows.ToList();
        _columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Count; i++)
        {
            _columnIndex[columns[i].Name] = i;
        }
    }

    public IReadOnlyList<DataColumn> Columns { get; }

    public IReadOnlyList<DataRecord> Rows => _rows;

    // ---- editing (the records table edits the project's copy of the data) ----

    /// <summary>A new record with these values in column order; missing values are empty.</summary>
    public DataRecord CreateRecord(IReadOnlyList<string>? values = null)
    {
        var cells = new string[Columns.Count];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = values is not null && i < values.Count ? values[i] ?? string.Empty : string.Empty;
        }

        int next = _rows.Count == 0 ? 1 : _rows.Max(r => r.SourceRowNumber) + 1;
        return new DataRecord(cells, _columnIndex, next);
    }

    public void InsertRow(int index, DataRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _rows.Insert(Math.Clamp(index, 0, _rows.Count), record);
    }

    public void RemoveRowAt(int index) => _rows.RemoveAt(index);

    /// <summary>
    /// Removes these records in one pass and returns where each one was, for
    /// <see cref="RestoreRows"/>. Linear in the table size, so deleting thousands of
    /// rows from a large table stays instant.
    /// </summary>
    public IReadOnlyList<(int Index, DataRecord Record)> RemoveRows(IEnumerable<DataRecord> records)
    {
        var doomed = new HashSet<DataRecord>(records, ReferenceEqualityComparer.Instance);
        var removed = new List<(int, DataRecord)>(doomed.Count);
        if (doomed.Count == 0)
        {
            return removed;
        }

        var kept = new List<DataRecord>(Math.Max(0, _rows.Count - doomed.Count));
        for (int i = 0; i < _rows.Count; i++)
        {
            if (doomed.Contains(_rows[i]))
            {
                removed.Add((i, _rows[i]));
            }
            else
            {
                kept.Add(_rows[i]);
            }
        }

        _rows.Clear();
        _rows.AddRange(kept);
        return removed;
    }

    /// <summary>Puts rows removed by <see cref="RemoveRows"/> back at their original positions.</summary>
    public void RestoreRows(IReadOnlyList<(int Index, DataRecord Record)> removed)
    {
        if (removed.Count == 0)
        {
            return;
        }

        var merged = new List<DataRecord>(_rows.Count + removed.Count);
        int source = 0;
        foreach ((int index, DataRecord record) in removed.OrderBy(r => r.Index))
        {
            while (merged.Count < index && source < _rows.Count)
            {
                merged.Add(_rows[source++]);
            }

            merged.Add(record);
        }

        while (source < _rows.Count)
        {
            merged.Add(_rows[source++]);
        }

        _rows.Clear();
        _rows.AddRange(merged);
    }

    public int IndexOf(DataRecord record) => _rows.IndexOf(record);

    /// <summary>Changes one cell and returns what it held before.</summary>
    public string SetValue(DataRecord record, int column, string value)
    {
        ArgumentNullException.ThrowIfNull(record);
        string[] cells = record.RawValues;
        if (column < 0 || column >= cells.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        string old = cells[column];
        cells[column] = value ?? string.Empty;
        record.RaiseValuesChanged();
        return old;
    }

    /// <summary>The table as CSV text (header row first), for the snapshot saved inside the project.</summary>
    public string ToCsv()
    {
        var sb = new System.Text.StringBuilder(Math.Max(256, _rows.Count * Columns.Count * 8));
        AppendLine(Columns.Select(c => c.Name));
        foreach (DataRecord row in _rows)
        {
            AppendLine(row.RawValues);
        }

        return sb.ToString();

        void AppendLine(IEnumerable<string> values)
        {
            bool first = true;
            foreach (string value in values)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                if (value.AsSpan().IndexOfAny(",\"\r\n") >= 0)
                {
                    sb.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
                }
                else
                {
                    sb.Append(value);
                }
            }

            sb.Append('\n');
        }
    }

    public int RowCount => Rows.Count;

    public IReadOnlyDictionary<string, int> ColumnIndex => _columnIndex;

    public bool HasColumn(string name) => _columnIndex.ContainsKey(name);

    public static DataSet Empty { get; } = new(Array.Empty<DataColumn>(), Array.Empty<DataRecord>());

    public static DataSet FromRows(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var columns = headers.Select((h, i) => new DataColumn(string.IsNullOrWhiteSpace(h) ? $"Column{i + 1}" : h.Trim(), i)).ToList();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Count; i++)
        {
            index[columns[i].Name] = i;
        }

        var records = new List<DataRecord>();
        int rowNumber = 1;
        foreach (IReadOnlyList<string> raw in rows)
        {
            var values = new string[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                values[i] = i < raw.Count ? raw[i] ?? string.Empty : string.Empty;
            }

            records.Add(new DataRecord(values, index, rowNumber++));
        }

        var set = new DataSet(columns, records);
        set.InferColumnTypes();
        return set;
    }

    public void InferColumnTypes()
    {
        foreach (DataColumn column in Columns)
        {
            column.InferredType = InferType(column.Index);
        }
    }

    private DataColumnType InferType(int columnIndex)
    {
        bool allInt = true, allDecimal = true, allDate = true, allBool = true, any = false;
        foreach (DataRecord row in Rows)
        {
            string v = row[columnIndex];
            if (string.IsNullOrWhiteSpace(v))
            {
                continue;
            }

            any = true;
            if (allInt && !long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                allInt = false;
            }

            if (allDecimal && !double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            {
                allDecimal = false;
            }

            if (allDate && !DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                allDate = false;
            }

            if (allBool && v is not ("true" or "false" or "TRUE" or "FALSE" or "0" or "1" or "yes" or "no"))
            {
                allBool = false;
            }
        }

        if (!any)
        {
            return DataColumnType.Text;
        }

        if (allInt)
        {
            return DataColumnType.Integer;
        }

        if (allBool)
        {
            return DataColumnType.Boolean;
        }

        if (allDecimal)
        {
            return DataColumnType.Decimal;
        }

        return allDate ? DataColumnType.Date : DataColumnType.Text;
    }
}
