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

/// <summary>Move / resize / rotate / skew one object. Consecutive drags on the same object merge.</summary>
public sealed class TransformObjectCommand : IUndoableCommand
{
    private readonly LabelObject _object;
    private readonly RectMm _oldBounds;
    private readonly double _oldRotation;
    private readonly double _oldSkewX;
    private readonly double _oldSkewY;
    private RectMm _newBounds;
    private double _newRotation;
    private double _newSkewX;
    private double _newSkewY;

    /// <param name="newSkewX">New horizontal skew; null keeps the object's current value.</param>
    /// <param name="newSkewY">New vertical skew; null keeps the object's current value.</param>
    public TransformObjectCommand(LabelObject obj, RectMm newBounds, double newRotation, double? newSkewX = null, double? newSkewY = null)
    {
        _object = obj;
        _oldBounds = obj.Bounds;
        _oldRotation = obj.RotationDegrees;
        _oldSkewX = obj.SkewXDegrees;
        _oldSkewY = obj.SkewYDegrees;
        _newBounds = newBounds;
        _newRotation = newRotation;
        _newSkewX = newSkewX ?? obj.SkewXDegrees;
        _newSkewY = newSkewY ?? obj.SkewYDegrees;
    }

    public string Label => "Move / resize";

    public void Do()
    {
        _object.Bounds = _newBounds;
        _object.RotationDegrees = _newRotation;
        _object.SkewXDegrees = _newSkewX;
        _object.SkewYDegrees = _newSkewY;
    }

    public void Undo()
    {
        _object.Bounds = _oldBounds;
        _object.RotationDegrees = _oldRotation;
        _object.SkewXDegrees = _oldSkewX;
        _object.SkewYDegrees = _oldSkewY;
    }

    public bool TryMergeWith(IUndoableCommand next)
    {
        if (next is TransformObjectCommand t && ReferenceEquals(t._object, _object))
        {
            _newBounds = t._newBounds;
            _newRotation = t._newRotation;
            _newSkewX = t._newSkewX;
            _newSkewY = t._newSkewY;
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

/// <summary>Add / move / delete ruler guides as one undoable step (snapshots both lists).</summary>
public sealed class GuideEditCommand : IUndoableCommand
{
    private readonly GuideCollection _target;
    private readonly List<double> _beforeV;
    private readonly List<double> _beforeH;
    private readonly List<double> _afterV;
    private readonly List<double> _afterH;

    public GuideEditCommand(GuideCollection target, string label, Action mutate)
    {
        _target = target;
        _beforeV = new List<double>(target.VerticalMm);
        _beforeH = new List<double>(target.HorizontalMm);
        mutate();
        _afterV = new List<double>(target.VerticalMm);
        _afterH = new List<double>(target.HorizontalMm);
        Label = label;
    }

    public string Label { get; }

    public void Do()
    {
        _target.VerticalMm = new List<double>(_afterV);
        _target.HorizontalMm = new List<double>(_afterH);
    }

    public void Undo()
    {
        _target.VerticalMm = new List<double>(_beforeV);
        _target.HorizontalMm = new List<double>(_beforeH);
    }
}

/// <summary>
/// Puts a document's objects in a new back-to-front order in one step (dragging rows in the
/// object list, bringing several objects forward), undoably.
/// </summary>
public sealed class ArrangeObjectsCommand : IUndoableCommand
{
    private readonly LabelDocument _document;
    private readonly List<LabelObject> _before;
    private readonly List<LabelObject> _after;

    public ArrangeObjectsCommand(LabelDocument document, IReadOnlyList<LabelObject> newOrder, string label = "Reorder")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(newOrder);
        if (newOrder.Count != document.Objects.Count || newOrder.Except(document.Objects).Any())
        {
            throw new ArgumentException("The new order must contain exactly the document's objects.", nameof(newOrder));
        }

        _document = document;
        _before = document.Objects.ToList();
        _after = newOrder.ToList();
        Label = label;
    }

    public string Label { get; }

    public bool ChangesAnything => !_before.SequenceEqual(_after);

    public void Do() => Set(_after);

    public void Undo() => Set(_before);

    private void Set(List<LabelObject> order)
    {
        _document.Objects.Clear();
        _document.Objects.AddRange(order);
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
