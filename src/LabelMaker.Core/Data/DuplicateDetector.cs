namespace LabelMaker.Core.Data;

public enum DuplicatePolicy
{
    /// <summary>Duplicates are an error; generation / print is blocked.</summary>
    Prevent = 0,

    /// <summary>Duplicates are reported but allowed.</summary>
    Warn = 1,

    /// <summary>No checking.</summary>
    Ignore = 2,
}

public readonly record struct DuplicateGroup(string Value, IReadOnlyList<int> RowIndexes)
{
    public int Count => RowIndexes.Count;
}

public sealed class DuplicateReport
{
    public required DuplicatePolicy Policy { get; init; }
    public required IReadOnlyList<DuplicateGroup> Groups { get; init; }

    public bool HasDuplicates => Groups.Count > 0;

    public bool IsBlocking => Policy == DuplicatePolicy.Prevent && HasDuplicates;

    public int TotalDuplicateRows => Groups.Sum(g => g.Count);

    /// <summary>Human-readable lines, e.g. "Duplicate barcode detected: ABC001258 (rows 12, 40)".</summary>
    public IEnumerable<string> Describe(string label = "value")
    {
        foreach (DuplicateGroup g in Groups)
        {
            yield return $"Duplicate {label} detected: {g.Value} (rows {string.Join(", ", g.RowIndexes.Select(i => i + 1))})";
        }
    }
}

/// <summary>
/// Finds repeated values in a sequence of keys (a data column, generated serials,
/// or resolved barcode values — spec §25). Works the same for imported datasets
/// and for generated series.
/// </summary>
public static class DuplicateDetector
{
    public static DuplicateReport Check(IEnumerable<string> values, DuplicatePolicy policy,
        bool caseInsensitive = false, bool ignoreEmpty = true)
    {
        if (policy == DuplicatePolicy.Ignore)
        {
            return new DuplicateReport { Policy = policy, Groups = Array.Empty<DuplicateGroup>() };
        }

        StringComparer comparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var index = new Dictionary<string, List<int>>(comparer);
        int i = 0;
        foreach (string raw in values)
        {
            string v = raw ?? string.Empty;
            if (!(ignoreEmpty && v.Length == 0))
            {
                if (!index.TryGetValue(v, out List<int>? list))
                {
                    index[v] = list = new List<int>(1);
                }

                list.Add(i);
            }

            i++;
        }

        var groups = index
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => new DuplicateGroup(kv.Key, kv.Value))
            .OrderBy(g => g.RowIndexes[0])
            .ToList();

        return new DuplicateReport { Policy = policy, Groups = groups };
    }

    public static DuplicateReport CheckColumn(DataSet data, string column, DuplicatePolicy policy,
        bool caseInsensitive = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!data.HasColumn(column))
        {
            return new DuplicateReport { Policy = policy, Groups = Array.Empty<DuplicateGroup>() };
        }

        int idx = data.ColumnIndex[column];
        return Check(data.Rows.Select(r => r[idx]), policy, caseInsensitive);
    }
}
