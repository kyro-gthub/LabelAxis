using LabelMaker.App.Mvvm;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;

namespace LabelMaker.App.ViewModels;

/// <summary>The left panel's page navigator and object list, and the canvas view options.</summary>
public sealed partial class MainViewModel
{
    public RelayCommand PreviousPageCommand { get; private set; } = null!;
    public RelayCommand NextPageCommand { get; private set; } = null!;
    public RelayCommand MovePageUpCommand { get; private set; } = null!;
    public RelayCommand MovePageDownCommand { get; private set; } = null!;
    public RelayCommand SortPagesCommand { get; private set; } = null!;
    public RelayCommand GridSmallerCommand { get; private set; } = null!;
    public RelayCommand GridLargerCommand { get; private set; } = null!;

    /// <summary>Grid steps offered by the smaller / larger buttons, in millimetres.</summary>
    private static readonly double[] GridSteps = { 0.25, 0.5, 1, 2, 2.5, 5, 10, 20 };

    private bool _syncingListSelection;

    private void BuildPanelCommands()
    {
        PreviousPageCommand = new RelayCommand(() => StepPage(-1), () => PageIndex() > 0);
        NextPageCommand = new RelayCommand(() => StepPage(1), () => PageIndex() < Pages.Count - 1);
        MovePageUpCommand = new RelayCommand(() => MoveActivePage(-1), () => ActivePageIndex() > 0);
        MovePageDownCommand = new RelayCommand(() => MoveActivePage(1),
            () => ActivePageIndex() is var i && i >= 0 && i < _session.Project.Pages.Count - 1);
        SortPagesCommand = new RelayCommand(p => SortPages(p as string), _ => _session.Project.Pages.Count > 1);
        GridSmallerCommand = new RelayCommand(() => StepGrid(-1), () => GridSizeMm > GridSteps[0]);
        GridLargerCommand = new RelayCommand(() => StepGrid(1), () => GridSizeMm < GridSteps[^1]);
    }

    // ---------- pages ----------

    /// <summary>Position of the page on screen in the navigator (the master label is row 0).</summary>
    private int PageIndex() => Pages.IndexOf(Pages.FirstOrDefault(p => p.PageId == _session.ActivePageId)!);

    /// <summary>Position of the active standalone page among the pages; -1 for the master label.</summary>
    private int ActivePageIndex() => _session.ActivePageId is { } id ? _session.Project.Pages.FindIndex(p => p.Id == id) : -1;

    private void StepPage(int direction)
    {
        int index = Math.Clamp(PageIndex() + direction, 0, Pages.Count - 1);
        if (index >= 0 && index < Pages.Count)
        {
            SelectedPage = Pages[index];
        }
    }

    private void MoveActivePage(int direction)
    {
        int index = ActivePageIndex();
        List<LabelPage> pages = _session.Project.Pages.ToList();
        int target = index + direction;
        if (index < 0 || target < 0 || target >= pages.Count)
        {
            return;
        }

        (pages[index], pages[target]) = (pages[target], pages[index]);
        _session.ReorderPages(pages, direction < 0 ? "Move page up" : "Move page down");
    }

    /// <summary>"name" A→Z, "name-desc" Z→A, "reverse" flips the current order. The master label stays first.</summary>
    private void SortPages(string? how)
    {
        IEnumerable<LabelPage> pages = _session.Project.Pages;
        var natural = new NaturalComparer();
        List<LabelPage> sorted = how switch
        {
            "name-desc" => pages.OrderByDescending(p => p.Name, natural).ToList(),
            "reverse" => pages.Reverse().ToList(),
            _ => pages.OrderBy(p => p.Name, natural).ToList(),
        };

        _session.ReorderPages(sorted, how switch
        {
            "name-desc" => "Sort pages Z to A",
            "reverse" => "Reverse page order",
            _ => "Sort pages A to Z",
        });
        StatusText = "Pages sorted";
    }

    /// <summary>
    /// Brings the page navigator up to date. When only the active page changed, the rows are
    /// kept and just the selection moves: clearing the list took keyboard focus away from it,
    /// so the arrow keys jumped into the object list instead of stepping through the pages.
    /// </summary>
    private void RebuildPages()
    {
        var wanted = new List<PageListItem> { new(null, MasterPageName, 1, IsMaster: true) };
        for (int i = 0; i < _session.Project.Pages.Count; i++)
        {
            LabelPage p = _session.Project.Pages[i];
            wanted.Add(new PageListItem(p.Id, p.Name, i + 2, IsMaster: false));
        }

        _suppressPageSelection = true;
        try
        {
            if (!wanted.SequenceEqual(Pages))
            {
                for (int i = 0; i < wanted.Count; i++)
                {
                    if (i < Pages.Count)
                    {
                        if (Pages[i] != wanted[i])
                        {
                            Pages[i] = wanted[i];
                        }
                    }
                    else
                    {
                        Pages.Add(wanted[i]);
                    }
                }

                while (Pages.Count > wanted.Count)
                {
                    Pages.RemoveAt(Pages.Count - 1);
                }
            }

            _selectedPage = Pages.FirstOrDefault(p => p.PageId == _session.ActivePageId) ?? Pages[0];
            OnPropertyChanged(nameof(SelectedPage));
        }
        finally
        {
            _suppressPageSelection = false;
        }
    }

    // ---------- objects on page ----------

    /// <summary>
    /// Front-to-back rows; a group is one heading row placed where its front-most member is,
    /// followed by its members. Rows are refreshed in place when the structure is unchanged,
    /// so editing a property does not reset the list's selection and scroll position.
    /// </summary>
    private void RebuildObjectList()
    {
        IReadOnlyList<LabelObject> objects = _session.ActiveLabel.Objects;
        var rows = new List<ObjectListItem>();
        var seenGroups = new HashSet<string>();
        for (int i = objects.Count - 1; i >= 0; i--)
        {
            LabelObject obj = objects[i];
            if (obj.GroupId is not { } groupId)
            {
                rows.Add(ObjectListItem.ForObject(obj, inGroup: false));
                continue;
            }

            if (!seenGroups.Add(groupId))
            {
                continue;
            }

            List<LabelObject> members = objects.Where(o => o.GroupId == groupId).Reverse().ToList();
            if (members.Count < 2)
            {
                rows.Add(ObjectListItem.ForObject(obj, inGroup: false));
                continue;
            }

            rows.Add(ObjectListItem.ForGroup(groupId, members));
            rows.AddRange(members.Select(m => ObjectListItem.ForObject(m, inGroup: true)));
        }

        bool sameShape = rows.Count == ObjectList.Count &&
                         rows.Select(r => r.Key).SequenceEqual(ObjectList.Select(r => r.Key)) &&
                         rows.Zip(ObjectList).All(p => p.First.Members.SequenceEqual(p.Second.Members));
        if (sameShape)
        {
            foreach (ObjectListItem row in ObjectList)
            {
                row.Refresh();
            }
        }
        else
        {
            ObjectList.Clear();
            foreach (ObjectListItem row in rows)
            {
                ObjectList.Add(row);
            }
        }

        SyncObjectListSelection();
    }

    /// <summary>Highlights the rows for what is selected on the canvas.</summary>
    private void SyncObjectListSelection()
    {
        _syncingListSelection = true;
        try
        {
            var selected = new HashSet<LabelObject>(_session.Selection);
            foreach (ObjectListItem row in ObjectList)
            {
                row.IsSelected = row.Members.Count > 0 && row.Members.All(selected.Contains);
            }
        }
        finally
        {
            _syncingListSelection = false;
        }

        ObjectListSelectionSynced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised after the list's highlighted rows were updated from the canvas.</summary>
    public event EventHandler? ObjectListSelectionSynced;

    /// <summary>
    /// The rows picked in the list (click, Shift+click for a range, Ctrl+click to add) become
    /// the canvas selection. A group heading stands for all its members.
    /// </summary>
    public void SelectObjectsFromList(IEnumerable<ObjectListItem> rows)
    {
        if (_syncingListSelection)
        {
            return;
        }

        var picked = new List<LabelObject>();
        foreach (LabelObject obj in rows.SelectMany(r => r.Members))
        {
            if (!picked.Contains(obj))
            {
                picked.Add(obj);
            }
        }

        if (!picked.SequenceEqual(_session.Selection))
        {
            _session.SetSelection(picked);
        }
    }

    /// <summary>Gives the object on <paramref name="row"/> a new name, undoably. An empty name goes back to the automatic one.</summary>
    public void RenameObject(ObjectListItem row, string? name)
    {
        row.IsEditing = false;
        if (row.Object is not { } obj || name is null)
        {
            return;
        }

        string trimmed = name.Trim();
        if (trimmed == obj.Name || (trimmed == row.Display && string.IsNullOrEmpty(obj.Name)))
        {
            return;
        }

        _session.Execute(new SetPropertyCommand<string>("Rename object", () => obj.Name, v => obj.Name = v, trimmed));
        _properties.RefreshAll();
        StatusText = trimmed.Length == 0 ? "Name cleared" : $"Renamed to \"{trimmed}\"";
    }

    /// <summary>
    /// Drag and drop in the object list: <paramref name="dragged"/> rows go in front of
    /// <paramref name="target"/> (or behind it when <paramref name="below"/>), changing the
    /// stacking order on the page. Rows are listed front to back, so higher in the list is
    /// further forward. A group moves as a whole; members can be reordered inside their group.
    /// </summary>
    public void MoveObjectsInList(IReadOnlyList<ObjectListItem> dragged, ObjectListItem target, bool below)
    {
        List<LabelObject> moving = dragged.SelectMany(r => r.Members).Distinct().ToList();
        if (moving.Count == 0 || (target.Object is { } targetObject && moving.Contains(targetObject)))
        {
            return;
        }

        List<LabelObject> frontToBack = ObjectList.Where(r => !r.IsGroup).Select(r => r.Object!).ToList();
        IReadOnlyList<LabelObject> anchorBlock = target.Members;

        // Dropping among a group's members from outside the group places the objects next to
        // the whole group, so a group is never split by objects that are not in it.
        if (target.IsGroupMember && target.GroupId is { } groupId && !moving.All(o => o.GroupId == groupId))
        {
            anchorBlock = frontToBack.Where(o => o.GroupId == groupId).ToList();
            List<LabelObject> members = anchorBlock.ToList();
            below = members.IndexOf(target.Object!) >= members.Count / 2.0;
        }

        if (anchorBlock.All(moving.Contains))
        {
            return;
        }

        int insertAt = below
            ? frontToBack.IndexOf(anchorBlock[^1]) + 1
            : frontToBack.IndexOf(anchorBlock[0]);
        var command = new ArrangeObjectsCommand(_session.ActiveLabel, ObjectStacking.MoveTo(frontToBack, moving, insertAt), "Reorder objects");
        if (command.ChangesAnything)
        {
            _session.Execute(command);
            _session.SetSelection(moving);
            StatusText = moving.Count == 1 ? "Moved in the stacking order" : $"Moved {moving.Count} objects in the stacking order";
        }
    }

    // ---------- canvas view options ----------

    public bool SharedGuides
    {
        get => _session.Project.SharedGuides;
        set
        {
            _session.SetSharedGuides(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(LockGuides));
            RefreshCanvas();
        }
    }

    public double GridSizeMm
    {
        get => _env.Settings.GridSizeMm;
        set
        {
            double v = Math.Clamp(value, 0.1, 50);
            if (Math.Abs(v - _env.Settings.GridSizeMm) < 1e-9)
            {
                return;
            }

            _env.Settings.GridSizeMm = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GridSizeText));
            GridSmallerCommand?.RaiseCanExecuteChanged();
            GridLargerCommand?.RaiseCanExecuteChanged();
            RefreshCanvas();
        }
    }

    public string GridSizeText => $"{_env.Settings.GridSizeMm:0.##} mm";

    private void StepGrid(int direction)
    {
        double current = GridSizeMm;
        double next = direction > 0
            ? GridSteps.FirstOrDefault(s => s > current + 1e-9, GridSteps[^1])
            : GridSteps.LastOrDefault(s => s < current - 1e-9, GridSteps[0]);
        GridSizeMm = next;
        if (!ShowGrid)
        {
            ShowGrid = true;
        }

        StatusText = $"Grid {GridSizeText}";
    }

    /// <summary>Orders "Page 2" before "Page 10".</summary>
    private sealed class NaturalComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            x ??= string.Empty;
            y ??= string.Empty;
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    int si = i, sj = j;
                    while (i < x.Length && char.IsDigit(x[i]))
                    {
                        i++;
                    }

                    while (j < y.Length && char.IsDigit(y[j]))
                    {
                        j++;
                    }

                    string a = x[si..i].TrimStart('0'), b = y[sj..j].TrimStart('0');
                    int byLength = a.Length.CompareTo(b.Length);
                    if (byLength != 0)
                    {
                        return byLength;
                    }

                    int byDigits = string.CompareOrdinal(a, b);
                    if (byDigits != 0)
                    {
                        return byDigits;
                    }

                    continue;
                }

                int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (c != 0)
                {
                    return c;
                }

                i++;
                j++;
            }

            return (x.Length - i).CompareTo(y.Length - j);
        }
    }
}
