using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Variables;

namespace LabelMaker.Core.Model.Objects;

/// <summary>
/// A simple fixed grid: uniform border, per-cell plain/tokenised text, per-column
/// width weights and per-row height weights. No merged cells, no spanning, no
/// per-cell rotation in v1 (see docs/OMISSIONS.md).
/// </summary>
public sealed class TableObject : LabelObject
{
    public override ObjectKind Kind => ObjectKind.Table;

    public int Rows { get; set; } = 2;
    public int Columns { get; set; } = 2;

    /// <summary>Row-major cell text; length must equal <see cref="Rows"/> * <see cref="Columns"/>.</summary>
    public List<string> Cells { get; set; } = new() { string.Empty, string.Empty, string.Empty, string.Empty };

    /// <summary>Relative column widths (defaults to equal).</summary>
    public List<double> ColumnWeights { get; set; } = new() { 1, 1 };

    /// <summary>Relative row heights (defaults to equal).</summary>
    public List<double> RowWeights { get; set; } = new() { 1, 1 };

    public ColorRgba BorderColor { get; set; } = ColorRgba.Black;
    public double BorderWidthMm { get; set; } = 0.2;
    public ColorRgba HeaderFill { get; set; } = ColorRgba.Transparent;
    public bool FirstRowIsHeader { get; set; }

    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSizePt { get; set; } = 8.0;
    public HorizontalAlignment CellAlignment { get; set; } = HorizontalAlignment.Left;
    public double CellPaddingMm { get; set; } = 1.0;

    public string CellAt(int row, int col)
    {
        int i = (row * Columns) + col;
        return i >= 0 && i < Cells.Count ? Cells[i] : string.Empty;
    }

    public void Normalize()
    {
        Rows = Math.Max(1, Rows);
        Columns = Math.Max(1, Columns);
        int need = Rows * Columns;
        while (Cells.Count < need)
        {
            Cells.Add(string.Empty);
        }

        if (Cells.Count > need)
        {
            Cells.RemoveRange(need, Cells.Count - need);
        }

        Resize(ColumnWeights, Columns);
        Resize(RowWeights, Rows);

        static void Resize(List<double> list, int count)
        {
            while (list.Count < count)
            {
                list.Add(1.0);
            }

            if (list.Count > count)
            {
                list.RemoveRange(count, list.Count - count);
            }
        }
    }

    public override IEnumerable<string> GetSearchableStrings() => Cells.Where(c => !string.IsNullOrEmpty(c));

    public override IEnumerable<string> GetReferencedFields() =>
        Cells.SelectMany(TokenScanner.ExtractFieldNames).Distinct(StringComparer.OrdinalIgnoreCase);

    public override LabelObject Clone()
    {
        var copy = new TableObject
        {
            Rows = Rows,
            Columns = Columns,
            Cells = new List<string>(Cells),
            ColumnWeights = new List<double>(ColumnWeights),
            RowWeights = new List<double>(RowWeights),
            BorderColor = BorderColor,
            BorderWidthMm = BorderWidthMm,
            HeaderFill = HeaderFill,
            FirstRowIsHeader = FirstRowIsHeader,
            FontFamily = FontFamily,
            FontSizePt = FontSizePt,
            CellAlignment = CellAlignment,
            CellPaddingMm = CellPaddingMm,
        };
        CopyBaseTo(copy);
        return copy;
    }
}
