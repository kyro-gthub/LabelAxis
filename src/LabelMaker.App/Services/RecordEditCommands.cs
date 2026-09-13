using LabelMaker.Core.Data;
using LabelMaker.Core.Editing;

namespace LabelMaker.App.Services;

/// <summary>
/// Undoable edits to the project's copy of the records. Each command holds the table
/// it was made against; if the records have since been replaced (a new import or a
/// refresh), undoing it does nothing rather than touching unrelated rows.
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
            Apply();
            Session.NotifyRecordsEdited();
        }
    }

    public void Undo()
    {
        if (ReferenceEquals(Session.Data, Data))
        {
            Revert();
            Session.NotifyRecordsEdited();
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