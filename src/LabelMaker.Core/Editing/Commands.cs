using LabelMaker.Core.Model;
using LabelMaker.Core.Units;

namespace LabelMaker.Core.Editing;

/// <summary>Runs several commands as one undo unit.</summary>
public sealed class CompositeCommand : IUndoableCommand
{
    private readonly IReadOnlyList<IUndoableCommand> _commands;

    public CompositeCommand(string label, IReadOnlyList<IUndoableCommand> commands)
    {
        Label = label;
        _commands = commands;
    }

    public string Label { get; }

    public void Do()
    {
        foreach (IUndoableCommand c in _commands)
        {
            c.Do();
        }
    }

    public void Undo()
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
        {
            _commands[i].Undo();
        }
    }
}

public sealed class AddObjectCommand : IUndoableCommand
{
    private readonly LabelDocument _document;
    private readonly LabelObject _object;
    private readonly int _index;

    public AddObjectCommand(LabelDocument document, LabelObject obj, int? index = null)
    {
        _document = document;
        _object = obj;
        _index = index ?? document.Objects.Count;
    }

    public string Label => $"Add {_object.Kind}";

    public void Do() => _document.Objects.Insert(Math.Clamp(_index, 0, _document.Objects.Count), _object);

    public void Undo() => _document.Objects.Remove(_object);
}

public sealed class RemoveObjectsCommand : IUndoableCommand
{
    private readonly LabelDocument _document;
    private readonly List<(LabelObject Object, int Index)> _removed;

    public RemoveObjectsCommand(LabelDocument document, IEnumerable<LabelObject> objects)
    {
        _document = document;
        _removed = objects
            .Select(o => (o, _document.Objects.IndexOf(o)))
            .Where(t => t.Item2 >= 0)
            .OrderBy(t => t.Item2)
            .ToList();
    }

    public string Label => _removed.Count == 1 ? "Delete object" : $"Delete {_removed.Count} objects";

    public void Do()
    {
        foreach ((LabelObject obj, _) in _removed.OrderByDescending(t => _document.Objects.IndexOf(t.Object)))
        {
            _document.Objects.Remove(obj);
        }
    }

    public void Undo()
    {
        foreach ((LabelObject obj, int index) in _removed)
        {
            _document.Objects.Insert(Math.Clamp(index, 0, _document.Objects.Count), obj);
        }
    }
}

/// <summary>Move / resize / rotate one object. Consecutive drags on the same object merge.</summary>
public sealed class TransformObjectCommand : IUndoableCommand
{
    private readonly LabelObject _object;
    private readonly RectMm _oldBounds;
    private readonly double _oldRotation;
    private RectMm _newBounds;
    private double _newRotation;

    public TransformObjectCommand(LabelObject obj, RectMm newBounds, double newRotation)
    {
        _object = obj;
        _oldBounds = obj.Bounds;
        _oldRotation = obj.RotationDegrees;
        _newBounds = newBounds;
        _newRotation = newRotation;
    }

    public string Label => "Move / resize";

    public void Do()
    {
        _object.Bounds = _newBounds;
        _object.RotationDegrees = _newRotation;
    }

    public void Undo()
    {
        _object.Bounds = _oldBounds;
        _object.RotationDegrees = _oldRotation;
    }

    public bool TryMergeWith(IUndoableCommand next)
    {
        if (next is TransformObjectCommand t && ReferenceEquals(t._object, _object))
        {
            _newBounds = t._newBounds;
            _newRotation = t._newRotation;
            return true;
        }

        return false;
    }
}

/// <summary>Generic property setter with a getter/setter pair (spec §31 "property changes").</summary>
public sealed class SetPropertyCommand<T> : IUndoableCommand
{
    private readonly Action<T> _setter;
    private readonly T _oldValue;
    private T _newValue;
    private readonly object _mergeKey;

    public SetPropertyCommand(string label, Func<T> getter, Action<T> setter, T newValue, object? mergeKey = null)
    {
        Label = label;
        _setter = setter;
        _oldValue = getter();
        _newValue = newValue;
        _mergeKey = mergeKey ?? new object();
    }

    public string Label { get; }

    public void Do() => _setter(_newValue);

    public void Undo() => _setter(_oldValue);

    public bool TryMergeWith(IUndoableCommand next)
    {
        if (next is SetPropertyCommand<T> s && Equals(s._mergeKey, _mergeKey))
        {
            _newValue = s._newValue;
            return true;
        }

        return false;
    }
}

public sealed class ReorderObjectCommand : IUndoableCommand
{
    private readonly LabelDocument _document;
    private readonly LabelObject _object;
    private readonly int _from;
    private readonly int _to;

    public ReorderObjectCommand(LabelDocument document, LabelObject obj, int toIndex)
    {
        _document = document;
        _object = obj;
        _from = document.Objects.IndexOf(obj);
        _to = Math.Clamp(toIndex, 0, Math.Max(0, document.Objects.Count - 1));
    }

    public string Label => "Reorder";

    public void Do() => Move(_from, _to);

    public void Undo() => Move(_to, _from);

    private void Move(int from, int to)
    {
        if (from < 0 || from >= _document.Objects.Count)
        {
            return;
        }

        LabelObject obj = _document.Objects[from];
        _document.Objects.RemoveAt(from);
        _document.Objects.Insert(Math.Clamp(to, 0, _document.Objects.Count), obj);
    }
}
