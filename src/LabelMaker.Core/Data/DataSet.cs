using System.Collections;
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
public sealed class DataRecord : IReadOnlyDictionary<string, string>
{
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
}

/// <summary>An in-memory table backing variable-data printing. Rows are an array — the UI virtualises.</summary>
public sealed class DataSet
{
    private readonly Dictionary<string, int> _columnIndex;

    public DataSet(IReadOnlyList<DataColumn> columns, IReadOnlyList<DataRecord> rows)
    {
        Columns = columns;
        Rows = rows;
        _columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Count; i++)
        {
            _columnIndex[columns[i].Name] = i;
        }
    }

    public IReadOnlyList<DataColumn> Columns { get; }

    public IReadOnlyList<DataRecord> Rows { get; }

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
