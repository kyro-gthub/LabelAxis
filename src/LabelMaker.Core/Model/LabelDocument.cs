using LabelMaker.Core.Units;

namespace LabelMaker.Core.Model;

/// <summary>
/// One label design: a physical size plus a z-ordered list of objects and an
/// optional background. Used both as the project's single <see cref="ProjectModel.MasterLabel"/>
/// and as the body of each standalone <see cref="LabelPage"/>.
/// </summary>
public sealed class LabelDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Label";

    public string? Description { get; set; }

    public List<string> Tags { get; set; } = new();

    /// <summary>Physical label size in millimetres (normally taken from the Formtec template).</summary>
    public SizeMm SizeMm { get; set; } = new(99.1, 38.1);

    public BackgroundSpec Background { get; set; } = new();

    /// <summary>Objects from back (index 0) to front.</summary>
    public List<LabelObject> Objects { get; set; } = new();

    /// <summary>User ruler guides that objects snap to (spec §2 "guides", "snap-to-object").</summary>
    public GuideCollection Guides { get; set; } = new();

    /// <summary>
    /// How many times this page is printed, one after another, when printing pages; null uses
    /// the job's "copies of every page". Saved with the project, so page 1 can always print
    /// 6 and page 2 always 12.
    /// </summary>
    public int? PrintCopies { get; set; }

    public RectMm Bounds => new(0, 0, SizeMm.Width, SizeMm.Height);

    public LabelObject? FindObject(string id) => Objects.FirstOrDefault(o => o.Id == id);

    public IEnumerable<LabelObject> GroupMembers(string groupId) =>
        Objects.Where(o => o.GroupId == groupId);

    public LabelDocument Clone() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        Tags = new List<string>(Tags),
        SizeMm = SizeMm,
        Background = Background.Clone(),
        Objects = Objects.Select(o => o.Clone()).ToList(),
        Guides = Guides.Clone(),
        PrintCopies = PrintCopies,
    };
}

/// <summary>Draggable ruler guides on a label. Positions are document millimetres.</summary>
public sealed class GuideCollection
{
    /// <summary>X positions of vertical guide lines.</summary>
    public List<double> VerticalMm { get; set; } = new();

    /// <summary>Y positions of horizontal guide lines.</summary>
    public List<double> HorizontalMm { get; set; } = new();

    /// <summary>When true, guides cannot be moved or deleted on the canvas.</summary>
    public bool Locked { get; set; }

    public bool IsEmpty => VerticalMm.Count == 0 && HorizontalMm.Count == 0;

    public void Clear()
    {
        VerticalMm.Clear();
        HorizontalMm.Clear();
    }

    public GuideCollection Clone() => new()
    {
        VerticalMm = new List<double>(VerticalMm),
        HorizontalMm = new List<double>(HorizontalMm),
        Locked = Locked,
    };
}

/// <summary>A named, searchable one-off design page inside a project (spec §5).</summary>
public sealed class LabelPage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Page";

    public string? Description { get; set; }

    public List<string> Tags { get; set; } = new();

    public LabelDocument Document { get; set; } = new();

    public LabelPage Clone() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        Tags = new List<string>(Tags),
        Document = Document.Clone(),
    };
}
