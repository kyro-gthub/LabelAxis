using LabelMaker.Core.Units;

namespace LabelMaker.Core.Formtec;

/// <summary>One physical label position on a sheet.</summary>
public readonly record struct LabelSlot(int Index, int Row, int Column, RectMm RectMm)
{
    /// <summary>Human-friendly 1-based position ("R2C3").</summary>
    public string PositionLabel => $"R{Row + 1}C{Column + 1}";
}

/// <summary>
/// The single authority for turning a <see cref="LabelTemplate"/> into label
/// rectangles on the page. Every rectangle is in page millimetres with the origin
/// at the page's top-left. One function; the test matrix covers each shipped
/// template's corner slots.
/// </summary>
public static class TemplateGeometry
{
    /// <summary>Rectangle of the label at (row, col), 0-based, in page millimetres.</summary>
    public static RectMm SlotRect(LabelTemplate template, int row, int column)
    {
        ArgumentNullException.ThrowIfNull(template);
        LabelGrid g = template.Grid;
        if (row < 0 || row >= g.Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row), row, $"Row must be 0..{g.Rows - 1}.");
        }

        if (column < 0 || column >= g.Columns)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, $"Column must be 0..{g.Columns - 1}.");
        }

        double x = g.MarginLeftMm + (column * (g.LabelWidthMm + g.HGapMm));
        double y = g.MarginTopMm + (row * (g.LabelHeightMm + g.VGapMm));
        return new RectMm(x, y, g.LabelWidthMm, g.LabelHeightMm);
    }

    /// <summary>Convert a linear 0-based slot index to (row, column) honouring <see cref="LabelTemplate.FillOrder"/>.</summary>
    public static (int Row, int Column) IndexToRowColumn(LabelTemplate template, int index)
    {
        ArgumentNullException.ThrowIfNull(template);
        LabelGrid g = template.Grid;
        if (index < 0 || index >= g.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be 0..{g.Count - 1}.");
        }

        return template.FillOrder == SheetFillOrder.RowMajor
            ? (index / g.Columns, index % g.Columns)
            : (index % g.Rows, index / g.Rows);
    }

    public static int RowColumnToIndex(LabelTemplate template, int row, int column)
    {
        ArgumentNullException.ThrowIfNull(template);
        LabelGrid g = template.Grid;
        return template.FillOrder == SheetFillOrder.RowMajor
            ? (row * g.Columns) + column
            : (column * g.Rows) + row;
    }

    public static RectMm SlotRect(LabelTemplate template, int index)
    {
        (int row, int column) = IndexToRowColumn(template, index);
        return SlotRect(template, row, column);
    }

    /// <summary>All slots on the sheet in fill order.</summary>
    public static IReadOnlyList<LabelSlot> AllSlots(LabelTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var list = new List<LabelSlot>(template.Grid.Count);
        for (int i = 0; i < template.Grid.Count; i++)
        {
            (int row, int column) = IndexToRowColumn(template, i);
            list.Add(new LabelSlot(i, row, column, SlotRect(template, row, column)));
        }

        return list;
    }

    /// <summary>
    /// Slots to actually print, given a 1-based start position, a set of skipped
    /// 1-based positions (already-used labels), and an optional explicit selection
    /// of 1-based positions. Selection, when non-empty, wins over start/skip.
    /// </summary>
    public static IReadOnlyList<LabelSlot> PrintableSlots(
        LabelTemplate template,
        int startPosition = 1,
        IReadOnlySet<int>? skipPositions = null,
        IReadOnlySet<int>? selectedPositions = null)
    {
        IReadOnlyList<LabelSlot> all = AllSlots(template);

        if (selectedPositions is { Count: > 0 })
        {
            return all.Where(s => selectedPositions.Contains(s.Index + 1)).ToList();
        }

        int start = Math.Clamp(startPosition, 1, Math.Max(1, all.Count));
        return all
            .Where(s => s.Index + 1 >= start)
            .Where(s => skipPositions is null || !skipPositions.Contains(s.Index + 1))
            .ToList();
    }

    /// <summary>Page rectangle covered by the printable area (page minus assumed printer margins).</summary>
    public static RectMm PrintableArea(LabelTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        PageSize p = template.EffectivePage;
        MarginsMm m = template.PrintableMarginMm;
        return RectMm.FromLtrb(m.Left, m.Top, p.WidthMm - m.Right, p.HeightMm - m.Bottom);
    }
}
