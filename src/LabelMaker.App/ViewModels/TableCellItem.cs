using LabelMaker.Core.Model.Objects;

namespace LabelMaker.App.ViewModels;

/// <summary>One cell of the selected table, bound to a text box in the Table panel.</summary>
public sealed class TableCellItem
{
    private readonly SelectionProperties _owner;
    private readonly TableObject _table;
    private readonly int _index;

    public TableCellItem(SelectionProperties owner, TableObject table, int index)
    {
        _owner = owner;
        _table = table;
        _index = index;
    }

    public string Text
    {
        get => _index < _table.Cells.Count ? _table.Cells[_index] : string.Empty;
        set => _owner.SetTableCell(_table, _index, value ?? string.Empty);
    }

    public bool IsHeader => _table.FirstRowIsHeader && _index < _table.Columns;
}
