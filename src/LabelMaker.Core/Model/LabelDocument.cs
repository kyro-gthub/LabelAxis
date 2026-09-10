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
