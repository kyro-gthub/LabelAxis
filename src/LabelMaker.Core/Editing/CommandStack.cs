namespace LabelMaker.Core.Editing;

/// <summary>A reversible edit (spec §31). <see cref="Do"/> must be safe to call again after <see cref="Undo"/>.</summary>
public interface IUndoableCommand
{
    string Label { get; }

    void Do();

    void Undo();

    /// <summary>
    /// Try to fold <paramref name="next"/> into this command (e.g. consecutive
    /// drags / keystrokes on the same target). Return true if merged.
    /// </summary>
    bool TryMergeWith(IUndoableCommand next) => false;
}

/// <summary>
/// Bounded undo/redo history. Commands are executed through <see cref="Execute"/>
/// so the "do" and the first history entry are always consistent. Cleared on
/// project load.
/// </summary>
public sealed class CommandStack
{
    private readonly List<IUndoableCommand> _undo = new();
    private readonly List<IUndoableCommand> _redo = new();
    private DateTime _lastPushUtc = DateTime.MinValue;

    public CommandStack(int capacity = 200) => Capacity = Math.Max(1, capacity);

    public int Capacity { get; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public string? NextUndoLabel => CanUndo ? _undo[^1].Label : null;
    public string? NextRedoLabel => CanRedo ? _redo[^1].Label : null;

    public event EventHandler? Changed;

    /// <summary>Coalesce merge-compatible commands pushed within this window.</summary>
    public TimeSpan MergeWindow { get; set; } = TimeSpan.FromMilliseconds(600);

    public void Execute(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Do();
        Push(command);
    }

    /// <summary>Record an already-applied command without executing it again.</summary>
    public void Push(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _redo.Clear();

        DateTime now = DateTime.UtcNow;
        if (_undo.Count > 0 && now - _lastPushUtc <= MergeWindow && _undo[^1].TryMergeWith(command))
        {
            _lastPushUtc = now;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _undo.Add(command);
        if (_undo.Count > Capacity)
        {
            _undo.RemoveAt(0);
        }

        _lastPushUtc = now;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (StepBack())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Redo()
    {
        if (StepForward())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The steps that can be undone, oldest first.</summary>
    public IReadOnlyList<string> UndoLabels => _undo.Select(c => c.Label).ToList();

    /// <summary>The steps that can be redone, the next one first.</summary>
    public IReadOnlyList<string> RedoLabels => Enumerable.Reverse(_redo).Select(c => c.Label).ToList();

    /// <summary>How many steps are done (and so can be undone).</summary>
    public int DoneCount => _undo.Count;

    /// <summary>
    /// Undoes or redoes until <paramref name="doneCount"/> steps are done: 0 goes back to the
    /// start of the history. One <see cref="Changed"/> for the whole jump.
    /// </summary>
    public void GoTo(int doneCount)
    {
        int target = Math.Clamp(doneCount, 0, _undo.Count + _redo.Count);
        bool moved = false;
        while (_undo.Count > target && StepBack())
        {
            moved = true;
        }

        while (_undo.Count < target && StepForward())
        {
            moved = true;
        }

        if (moved)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool StepBack()
    {
        if (!CanUndo)
        {
            return false;
        }

        IUndoableCommand command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Undo();
        _redo.Add(command);
        _lastPushUtc = DateTime.MinValue;
        return true;
    }

    private bool StepForward()
    {
        if (!CanRedo)
        {
            return false;
        }

        IUndoableCommand command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Do();
        _undo.Add(command);
        _lastPushUtc = DateTime.MinValue;
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _lastPushUtc = DateTime.MinValue;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
