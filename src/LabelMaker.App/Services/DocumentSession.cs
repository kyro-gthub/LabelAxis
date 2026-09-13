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

    /// <summary>Null = the master label; otherwise the id of the standalone page being edited.</summary>
    public string? ActivePageId { get; private set; }

    public bool IsEditingMaster => ActivePageId is null;

    public LabelDocument ActiveLabel =>
        ActivePageId is { } id && Project.Pages.FirstOrDefault(p => p.Id == id) is { } page
            ? page.Document
            : Project.MasterLabel;

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

    /// <summary>Cells or rows of the current data were edited in place (the table is the same object).</summary>
    public event EventHandler? RecordsEdited;
    public event EventHandler? HistoryChanged;
    public event EventHandler? ActivePageChanged;
    public event EventHandler? PagesChanged;

    // ---- pages (MS Publisher-style page navigator) ----

    public void SelectPage(string? pageId)
    {
        if (pageId is not null && Project.Pages.All(p => p.Id != pageId))
        {
            pageId = null;
        }

        if (ActivePageId == pageId)
        {
            return;
        }

        ActivePageId = pageId;
        ClearSelection();
        ActivePageChanged?.Invoke(this, EventArgs.Empty);
    }

    public LabelPage AddPage(string? name = null)
    {
        var page = new LabelPage
        {
            Name = name ?? $"Page {Project.Pages.Count + 1}",
            Document = new LabelDocument
            {
                Name = name ?? $"Page {Project.Pages.Count + 1}",
                SizeMm = ActiveLabel.SizeMm,
            },
        };

        Execute(new PageStructureCommand(this, "Add page", () =>
        {
            Project.Pages.Add(page);
            ActivePageId = page.Id;
        }));
        return page;
    }

    public LabelPage? DuplicateActivePage()
    {
        LabelPage source;
        if (ActivePageId is { } id && Project.Pages.FirstOrDefault(p => p.Id == id) is { } existing)
        {
            source = existing;
        }
        else
        {
            // Duplicating the master creates the first real page from it.
            source = new LabelPage { Name = "Master copy", Document = Project.MasterLabel.Clone() };
        }

        LabelPage copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Document.Id = Guid.NewGuid().ToString("N");
        copy.Name = source.Name + " copy";
        copy.Document.Name = copy.Name;

        Execute(new PageStructureCommand(this, "Duplicate page", () =>
        {
            int index = ActivePageId is null ? Project.Pages.Count : Project.Pages.FindIndex(p => p.Id == ActivePageId) + 1;
            Project.Pages.Insert(Math.Clamp(index, 0, Project.Pages.Count), copy);
            ActivePageId = copy.Id;
        }));
        return copy;
    }

    public bool RemoveActivePage()
    {
        if (ActivePageId is not { } id || Project.Pages.FirstOrDefault(p => p.Id == id) is not { } page)
        {
            return false; // the master label cannot be removed
        }

        Execute(new PageStructureCommand(this, "Remove page", () =>
        {
            int idx = Project.Pages.IndexOf(page);
            Project.Pages.Remove(page);
            ActivePageId = Project.Pages.Count == 0
                ? null
                : Project.Pages[Math.Clamp(idx, 0, Project.Pages.Count - 1)].Id;
        }));
        return true;
    }

    public void RenameActivePage(string name)
    {
        Execute(new PageStructureCommand(this, "Rename page", () =>
        {
            if (ActivePageId is { } id && Project.Pages.FirstOrDefault(p => p.Id == id) is { } page)
            {
                page.Name = name;
                page.Document.Name = name;
            }
            else
            {
                // No standalone page selected: the master label is what is on screen.
                Project.MasterLabel.Name = name;
            }
        }));
    }

    /// <summary>
    /// Undo for page-list edits (add, duplicate, remove, rename). It snapshots the
    /// page order, every page's name and the active page before and after the
    /// change; the pages themselves are kept by reference, so a removed page comes
    /// back with its objects intact.
    /// </summary>
    private sealed class PageStructureCommand : IUndoableCommand
    {
        private readonly DocumentSession _session;
        private readonly Snapshot _before;
        private readonly Snapshot _after;

        public PageStructureCommand(DocumentSession session, string label, Action mutate)
        {
            _session = session;
            Label = label;
            _before = Snapshot.Capture(session);
            mutate();
            _after = Snapshot.Capture(session);
        }

        public string Label { get; }

        public void Do() => _after.Restore(_session);

        public void Undo() => _before.Restore(_session);

        private sealed record Snapshot(
            List<LabelPage> Pages, List<string> PageNames, string MasterName, string? ActivePageId)
        {
            public static Snapshot Capture(DocumentSession s) => new(
                s.Project.Pages.ToList(),
                s.Project.Pages.Select(p => p.Name).ToList(),
                s.Project.MasterLabel.Name,
                s.ActivePageId);

            public void Restore(DocumentSession s)
            {
                s.Project.Pages.Clear();
                s.Project.Pages.AddRange(Pages);
                for (int i = 0; i < Pages.Count; i++)
                {
                    Pages[i].Name = PageNames[i];
                    Pages[i].Document.Name = PageNames[i];
                }

                s.Project.MasterLabel.Name = MasterName;
                s.ActivePageId = ActivePageId is not null && Pages.Any(p => p.Id == ActivePageId) ? ActivePageId : null;
                s.Selection = Array.Empty<LabelObject>();

                s.PagesChanged?.Invoke(s, EventArgs.Empty);
                s.ActivePageChanged?.Invoke(s, EventArgs.Empty);
                s.SelectionChanged?.Invoke(s, EventArgs.Empty);
            }
        }
    }

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

    public void SetData(DataSet? data, DataConnectionModel? connection) => SetData(data, connection, markDirty: true);

    public void SetData(DataSet? data, DataConnectionModel? connection, bool markDirty)
    {
        Data = data;
        _recordIndex = 0;
        if (connection is not null)
        {
            Project.DataConnections.RemoveAll(c => c.Id == connection.Id);
            Project.DataConnections.Add(connection);
            Project.ActiveDataConnectionId = connection.Id;
        }

        if (data is null)
        {
            Document.DataCaches.Clear();
            Project.ActiveDataConnectionId = null;
            _dataCacheStale = false;
        }
        else
        {
            _dataCacheStale = true;
        }

        RebuildSearchIndex();
        DataChanged?.Invoke(this, EventArgs.Empty);
        RecordChanged?.Invoke(this, EventArgs.Empty);
        if (markDirty)
        {
            MarkDirty();
        }
    }

    private bool _dataCacheStale;

    /// <summary>The active connection, when the records came from one.</summary>
    public DataConnectionModel? ActiveConnection =>
        Project.DataConnections.FirstOrDefault(c => c.Id == Project.ActiveDataConnectionId);

    /// <summary>Called by record edits (and their undo) after changing <see cref="Data"/> in place.</summary>
    public void NotifyRecordsEdited()
    {
        _dataCacheStale = true;
        if (Data is { } data)
        {
            _recordIndex = Math.Clamp(_recordIndex, 0, Math.Max(0, data.RowCount - 1));
        }

        RebuildSearchIndex();
        RecordsEdited?.Invoke(this, EventArgs.Empty);
        RecordChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Writes the snapshot of the records into the document before it is saved, so the
    /// project reopens with exactly these records (edits included) even offline.
    /// Done lazily: rebuilding the CSV on every cell edit would be wasted work.
    /// </summary>
    public void SyncDataCache()
    {
        if (!_dataCacheStale)
        {
            return;
        }

        _dataCacheStale = false;
        Document.DataCaches.Clear();
        if (Data is { } data)
        {
            string id = Project.ActiveDataConnectionId ?? "manual";
            Document.DataCaches.Add(new ProjectDataCache(id, data.ToCsv(), "{}"));
        }
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
