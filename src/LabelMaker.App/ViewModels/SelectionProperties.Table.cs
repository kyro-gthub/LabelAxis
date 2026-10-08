using System.Collections.ObjectModel;
using System.Windows.Media;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Services;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Units;

namespace LabelMaker.App.ViewModels;

/// <summary>The Properties panel for a table: rows, columns, cells and their look.</summary>
public sealed partial class SelectionProperties
{
    public bool IsTable => Obj is TableObject;

    public double TableRows
    {
        get => (Obj as TableObject)?.Rows ?? 2;
        set => ResizeTable(rows: (int)Math.Clamp(Math.Round(value), 1, 50), columns: null);
    }

    public double TableColumns
    {
        get => (Obj as TableObject)?.Columns ?? 2;
        set => ResizeTable(rows: null, columns: (int)Math.Clamp(Math.Round(value), 1, 20));
    }

    /// <summary>One editable box per cell, laid out row by row.</summary>
    public IReadOnlyList<TableCellItem> TableCells =>
        Obj is TableObject t
            ? Enumerable.Range(0, t.Rows * t.Columns).Select(i => new TableCellItem(this, t, i)).ToList()
            : Array.Empty<TableCellItem>();

    public bool TableHeader
    {
        get => (Obj as TableObject)?.FirstRowIsHeader ?? false;
        set
        {
            if (Obj is TableObject t)
            {
                Set(nameof(TableHeader), () => t.FirstRowIsHeader, v => t.FirstRowIsHeader = v, value);
            }
        }
    }

    public string TableHeaderFillHex
    {
        get => ((Obj as TableObject)?.HeaderFill ?? ColorRgba.Transparent).ToHex();
        set
        {
            if (Obj is TableObject t && ColorRgba.TryParse(value, out ColorRgba c))
            {
                Set(nameof(TableHeaderFillHex), () => t.HeaderFill, v => t.HeaderFill = v, c);
            }
        }
    }

    public string TableBorderHex
    {
        get => ((Obj as TableObject)?.BorderColor ?? ColorRgba.Black).ToHex();
        set
        {
            if (Obj is TableObject t && ColorRgba.TryParse(value, out ColorRgba c))
            {
                Set(nameof(TableBorderHex), () => t.BorderColor, v => t.BorderColor = v, c);
            }
        }
    }

    public double TableBorderWidth
    {
        get => Out((Obj as TableObject)?.BorderWidthMm ?? 0.2);
        set
        {
            if (Obj is TableObject t)
            {
                Set(nameof(TableBorderWidth), () => t.BorderWidthMm, v => t.BorderWidthMm = v, Math.Clamp(In(value), 0, 5));
            }
        }
    }

    public string TableFontFamily
    {
        get => (Obj as TableObject)?.FontFamily ?? "Segoe UI";
        set
        {
            if (Obj is TableObject t && !string.IsNullOrWhiteSpace(value))
            {
                Set(nameof(TableFontFamily), () => t.FontFamily, v => t.FontFamily = v, value);
            }
        }
    }

    public double TableFontSize
    {
        get => (Obj as TableObject)?.FontSizePt ?? 8;
        set
        {
            if (Obj is TableObject t)
            {
                Set(nameof(TableFontSize), () => t.FontSizePt, v => t.FontSizePt = v, Math.Clamp(value, 3, 72));
            }
        }
    }

    public HorizontalAlignment TableAlign
    {
        get => (Obj as TableObject)?.CellAlignment ?? HorizontalAlignment.Left;
        set
        {
            if (Obj is TableObject t)
            {
                Set(nameof(TableAlign), () => t.CellAlignment, v => t.CellAlignment = v, value);
            }
        }
    }

    public double TablePadding
    {
        get => Out((Obj as TableObject)?.CellPaddingMm ?? 1);
        set
        {
            if (Obj is TableObject t)
            {
                Set(nameof(TablePadding), () => t.CellPaddingMm, v => t.CellPaddingMm = v, Math.Clamp(In(value), 0, 20));
            }
        }
    }

    internal void SetTableCell(TableObject table, int index, string value)
    {
        if (index < 0 || index >= table.Cells.Count || table.Cells[index] == value)
        {
            return;
        }

        var cells = new List<string>(table.Cells) { [index] = value };
        _session.Execute(new SetPropertyCommand<List<string>>("Edit cell", () => table.Cells, v => table.Cells = v, cells));
    }

    /// <summary>
    /// Adds or removes rows/columns keeping existing cell text in place (the cell
    /// list is row-major, so changing the column count has to re-lay it out).
    /// </summary>
    private void ResizeTable(int? rows, int? columns)
    {
        if (Obj is not TableObject t)
        {
            return;
        }

        int newRows = rows ?? t.Rows;
        int newCols = columns ?? t.Columns;
        if (newRows == t.Rows && newCols == t.Columns)
        {
            return;
        }

        var cells = new List<string>(newRows * newCols);
        for (int r = 0; r < newRows; r++)
        {
            for (int c = 0; c < newCols; c++)
            {
                cells.Add(r < t.Rows && c < t.Columns ? t.CellAt(r, c) : string.Empty);
            }
        }

        var before = new TableLayout(t.Rows, t.Columns, t.Cells, t.ColumnWeights, t.RowWeights);
        var after = new TableLayout(
            newRows, newCols, cells,
            Enumerable.Range(0, newCols).Select(i => i < t.ColumnWeights.Count ? t.ColumnWeights[i] : 1.0).ToList(),
            Enumerable.Range(0, newRows).Select(i => i < t.RowWeights.Count ? t.RowWeights[i] : 1.0).ToList());

        _session.Execute(new SetPropertyCommand<TableLayout>(
            "Resize table", () => before, v => v.ApplyTo(t), after));
        RefreshAll();
    }

    private sealed record TableLayout(int Rows, int Columns, List<string> Cells, List<double> ColumnWeights, List<double> RowWeights)
    {
        public void ApplyTo(TableObject t)
        {
            t.Rows = Rows;
            t.Columns = Columns;
            t.Cells = new List<string>(Cells);
            t.ColumnWeights = new List<double>(ColumnWeights);
            t.RowWeights = new List<double>(RowWeights);
        }
    }
}
