using LabelMaker.Core.Units;

namespace LabelMaker.Core.Model;

public enum ObjectKind
{
    Text = 0,
    Image = 1,
    Shape = 2,
    Barcode = 3,
    Table = 4,
}

/// <summary>
/// Base type for everything placeable on a label. Geometry is stored in document
/// millimetres; nothing here is ever flattened to a bitmap while editing.
/// </summary>
public abstract class LabelObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Un-rotated bounding rectangle in document millimetres.</summary>
    public RectMm Bounds { get; set; } = new(0, 0, 20, 10);

    /// <summary>Clockwise rotation in degrees about the <see cref="RectMm.Center"/> of <see cref="Bounds"/>.</summary>
    public double RotationDegrees { get; set; }

    /// <summary>0..1 multiplier applied to the whole object.</summary>
    public double Opacity { get; set; } = 1.0;

    public bool Locked { get; set; }

    public bool Visible { get; set; } = true;

    /// <summary>Non-null when this object belongs to a group; all members share the id.</summary>
    public string? GroupId { get; set; }

    public abstract ObjectKind Kind { get; }

    /// <summary>Axis-aligned bounds after rotation — used for selection, snapping, preflight.</summary>
    public RectMm RotatedBounds => Bounds.RotatedBounds(RotationDegrees);

    /// <summary>
    /// Every literal + bound text this object contributes to the search index and
    /// to preflight "empty required field" checks. Implementations return the raw
    /// authored strings (including <c>{tokens}</c>), not resolved values.
    /// </summary>
    public abstract IEnumerable<string> GetSearchableStrings();

    /// <summary>Field/column names referenced via <c>{token}</c> syntax anywhere in this object.</summary>
    public virtual IEnumerable<string> GetReferencedFields() => Array.Empty<string>();

    public abstract LabelObject Clone();

    protected void CopyBaseTo(LabelObject target)
    {
        target.Id = Guid.NewGuid().ToString("N");
        target.Name = Name;
        target.Bounds = Bounds;
        target.RotationDegrees = RotationDegrees;
        target.Opacity = Opacity;
        target.Locked = Locked;
        target.Visible = Visible;
        target.GroupId = GroupId;
    }
}
