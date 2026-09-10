using LabelMaker.App.Rendering;
using LabelMaker.Core.Data;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Search;
using LabelMaker.Core.Serialization;

namespace LabelMaker.App.Services;

/// <summary>
/// The single mutable state hub for the open project: the document, the undo
/// stack, the active dataset + record cursor, the current selection, and the
/// search index. Views subscribe to its events.
/// </summary>
public sealed class DocumentSession
{
    private readonly WpfImageSource _imageSource;
    private int _recordIndex;

    public DocumentSession(ProjectDocument document, WpfImageSource imageSource)
    {
        Document = document;
        _imageSource = imageSource;
        History = new CommandStack(200);
        History.Changed += (_, _) => HistoryChanged?.Invoke(this, EventArgs.Empty);
        SearchIndex = new ProjectSearchIndex();
        RebuildSearchIndex();
    }

    public ProjectDocument Document { get; }

    public ProjectModel Project => Document.Project;

    public LabelDocument ActiveLabel => Project.MasterLabel;

    public CommandStack History { get; }

    public ProjectSearchIndex SearchIndex { get; }

    public DataSet? Data { get; private set; }

    public int RecordIndex
    {
        get => _recordIndex;
        set
        {
            int clamped = Data is { RowCount: > 0 } ? Math.Clamp(value, 0, Data.RowCount - 1) : 0;
            if (_recordIndex != clamped)
            {
                _recordIndex = clamped;
                RecordChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int RecordCount => Data?.RowCount ?? 0;

    public IReadOnlyList<LabelObject> Selection { get; private set; } = Array.Empty<LabelObject>();

    public event EventHandler? DocumentChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler? RecordChanged;
    public event EventHandler? DataChanged;
    public event EventHandler? HistoryChanged;

    public void MarkDirty()
    {
        Document.HasUnsavedChanges = true;
        Project.TouchModified();
        _imageSource.Invalidate();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Execute(IUndoableCommand command)
    {
        History.Execute(command);
        MarkDirty();
    }

    public void Undo()
    {
        History.Undo();
        MarkDirty();
    }

    public void Redo()
    {
        History.Redo();
        MarkDirty();
    }

    public void SetSelection(IEnumerable<LabelObject> objects)
    {
        Selection = objects.Where(o => ActiveLabel.Objects.Contains(o)).ToList();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection() => SetSelection(Array.Empty<LabelObject>());

    public void SetData(DataSet? data, DataConnectionModel? connection)
    {
        Data = data;
        _recordIndex = 0;
        if (connection is not null)
        {
            Project.DataConnections.RemoveAll(c => c.Id == connection.Id);
            Project.DataConnections.Add(connection);
            Project.ActiveDataConnectionId = connection.Id;
        }

        RebuildSearchIndex();
        DataChanged?.Invoke(this, EventArgs.Empty);
        RecordChanged?.Invoke(this, EventArgs.Empty);
        MarkDirty();
    }

    public void RebuildSearchIndex() => SearchIndex.Rebuild(Project, Data);

    public LabelMaker.Core.Variables.ResolveContext CurrentResolveContext()
    {
        IReadOnlyDictionary<string, string>? record =
            Data is { RowCount: > 0 } ? Data.Rows[Math.Clamp(_recordIndex, 0, Data.RowCount - 1)] : null;

        return new LabelMaker.Core.Variables.ResolveContext
        {
            Record = record,
            Variables = Project.Variables,
            RecordNumber = _recordIndex + 1,
            RecordCount = Math.Max(1, RecordCount),
            TemplateName = Project.Template?.Name ?? string.Empty,
        };
    }
}
