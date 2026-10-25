using LabelMaker.Core.Data;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Services;

/// <summary>
/// Undoable edits to the project's copy of the records. Each command holds the table
/// it was made against; if the records have since been replaced (a new import or a
/// refresh), undoing it does nothing rather than touching unrelated rows. The records
/// chosen for printing are kept by row number, so around every edit they are followed
/// as records: a row added above them, or one taken out, does not make others print.
/// </summary>
public abstract class RecordEditCommand : IUndoableCommand
{
    protected RecordEditCommand(DocumentSession session, DataSet data)
    {
        Session = session;
        Data = data;
    }

    protected DocumentSession Session { get; }

    protected DataSet Data { get; }

    public abstract string Label { get; }

    public void Do()
    {
        if (ReferenceEquals(Session.Data, Data))
        {
            ChosenRecords chosen = ChosenRecords.Capture(Session.Project.PrintSettings, Data);
            Apply();
            chosen.Restore(Session.Project.PrintSettings, Data);
            Session.NotifyRecordsEdited();
        }
    }

    public void Undo()
    {
        if (ReferenceEquals(Session.Data, Data))
        {
            ChosenRecords chosen = ChosenRecords.Capture(Session.Project.PrintSettings, Data);
            Revert();
            chosen.Restore(Session.Project.PrintSettings, Data);
            Session.NotifyRecordsEdited();
        }
    }

    /// <summary>
    /// The records chosen for printing (a list, or a first-to-last range) held as the records
    /// themselves while rows move, then turned back into the row numbers they now have.
    /// Chosen records that were taken out are dropped; if none are left, all records print.
    /// </summary>
    private sealed class ChosenRecords
    {
        private List<DataRecord>? _list;
        private List<DataRecord>? _range;
        private bool _hasFirst, _hasLast;

        public static ChosenRecords Capture(PrintSettings settings, DataSet data)
        {
            var chosen = new ChosenRecords();
            if (settings.RangeIsForPages)
            {
                return chosen; // page numbers, not records: editing records leaves them alone
            }

            if (settings.SelectedRecords.Count > 0)
            {
                chosen._list = settings.SelectedRecords.Where(i => i >= 0 && i < data.RowCount).Select(i => data.Rows[i]).ToList();
            }

            if (settings.FirstRecord is not null || settings.LastRecord is not null)
            {
                int first = Math.Max(0, settings.FirstRecord ?? 0);
                int last = Math.Min(data.RowCount - 1, settings.LastRecord ?? data.RowCount - 1);
                chosen._range = Enumerable.Range(first, Math.Max(0, last - first + 1)).Select(i => data.Rows[i]).ToList();
                chosen._hasFirst = settings.FirstRecord is not null;
                chosen._hasLast = settings.LastRecord is not null;
            }

            return chosen;
        }

        public void Restore(PrintSettings settings, DataSet data)
        {
            if (_list is not null)
            {
                settings.SelectedRecords = _list.Select(data.IndexOf).Where(i => i >= 0).Order().ToList();
            }

            if (_range is not null)
            {
                List<int> rows = _range.Select(data.IndexOf).Where(i => i >= 0).ToList();
                settings.FirstRecord = _hasFirst && rows.Count > 0 ? rows.Min() : null;
                settings.LastRecord = _hasLast && rows.Count > 0 ? rows.Max() : null;
            }
        }
    }

    public virtual bool TryMergeWith(IUndoableCommand next) => false;

    protected abstract void Apply();

    protected abstract void Revert();
}

public sealed class EditRecordCellCommand : RecordEditCommand
{
    private readonly DataRecord _record;
    private readonly int _column;
    private readonly string _value;
    private string _previous = string.Empty;

    public EditRecordCellCommand(DocumentSession session, DataSet data, DataRecord record, int column, string value)
        : base(session, data)
    {
        _record = record;
        _column = column;
        _value = value;
    }

    public override string Label => "Edit record";

    protected override void Apply() => _previous = Data.SetValue(_record, _column, _value);

    protected override void Revert() => Data.SetValue(_record, _column, _previous);
}

public sealed class InsertRecordsCommand : RecordEditCommand
{
    private readonly int _index;
    private readonly IReadOnlyList<DataRecord> _records;

    public InsertRecordsCommand(DocumentSession session, DataSet data, int index, IReadOnlyList<DataRecord> records)
        : base(session, data)
    {
        _index = index;
        _records = records;
    }

    public override string Label => _records.Count == 1 ? "Add record" : $"Add {_records.Count} records";

    protected override void Apply()
    {
        for (int i = 0; i < _records.Count; i++)
        {
            Data.InsertRow(_index + i, _records[i]);
        }
    }

    protected override void Revert()
    {
        foreach (DataRecord record in _records)
        {
            int at = Data.IndexOf(record);
            if (at >= 0)
            {
                Data.RemoveRowAt(at);
            }
        }
    }
}

public sealed class RemoveRecordsCommand : RecordEditCommand
{
    private readonly IReadOnlyList<DataRecord> _records;
    private IReadOnlyList<(int Index, DataRecord Record)> _removed = Array.Empty<(int, DataRecord)>();

    public RemoveRecordsCommand(DocumentSession session, DataSet data, IReadOnlyList<DataRecord> records)
        : base(session, data)
    {
        _records = records;
    }

    public override string Label => _records.Count == 1 ? "Delete record" : $"Delete {_records.Count:N0} records";

    protected override void Apply() => _removed = Data.RemoveRows(_records);

    protected override void Revert() => Data.RestoreRows(_removed);
}