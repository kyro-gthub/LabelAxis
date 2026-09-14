using System.Text.Json.Serialization;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Sequences;

namespace LabelMaker.Core.Model;

public sealed class ProjectMetadata
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled";
    public string? Author { get; set; }
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string AppVersion { get; set; } = "1.0.0";

    public ProjectMetadata Clone() => new()
    {
        Id = Id,
        Title = Title,
        Author = Author,
        Description = Description,
        Tags = new List<string>(Tags),
        CreatedUtc = CreatedUtc,
        ModifiedUtc = ModifiedUtc,
        AppVersion = AppVersion,
    };
}

/// <summary>A single per-record property tweak on top of the master label (spec §37).</summary>
public sealed class ObjectOverride
{
    public string ObjectId { get; set; } = string.Empty;

    /// <summary>Property path, e.g. "Text", "AssetId", "Bounds.X".</summary>
    public string Property { get; set; } = string.Empty;

    /// <summary>Serialized override value (string form; interpreted by the renderer).</summary>
    public string Value { get; set; } = string.Empty;

    public ObjectOverride Clone() => (ObjectOverride)MemberwiseClone();
}

/// <summary>
/// The whole document tree for one <c>.labelproj</c>. Deliberately a plain data
/// graph — deserializing it can never execute code (spec §28).
/// </summary>
public sealed class ProjectModel
{
    /// <summary>Bumped only for breaking changes; <c>ProjectUpgrader</c> migrates old files.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public ProjectMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Embedded copy of the chosen Formtec template so the project is portable.
    /// Stored in the archive as a separate <c>template.json</c> (via the template
    /// DTO), not inside <c>project.json</c>.
    /// </summary>
    [JsonIgnore]
    public LabelTemplate? Template { get; set; }

    /// <summary>The one design used for every record (spec §37).</summary>
    public LabelDocument MasterLabel { get; set; } = new();

    /// <summary>Standalone, individually-named designs (spec §5).</summary>
    public List<LabelPage> Pages { get; set; } = new();

    public List<DataConnectionModel> DataConnections { get; set; } = new();

    public List<SequenceDefinition> Sequences { get; set; } = new();

    public List<CustomVariable> Variables { get; set; } = new();

    public PrintSettings PrintSettings { get; set; } = new();

    /// <summary>Sparse per-record overrides keyed by 0-based record index.</summary>
    public Dictionary<int, List<ObjectOverride>> RecordOverrides { get; set; } = new();

    /// <summary>
    /// When true every page shows and snaps to the same guides (kept on the master label);
    /// otherwise each page has its own.
    /// </summary>
    public bool SharedGuides { get; set; }

    /// <summary>Id of the active data connection for the record navigator (null = none).</summary>
    public string? ActiveDataConnectionId { get; set; }

    public static ProjectModel CreateNew(LabelTemplate template, string title = "Untitled")
    {
        ArgumentNullException.ThrowIfNull(template);
        var project = new ProjectModel
        {
            Metadata = new ProjectMetadata { Title = title },
            Template = template,
            MasterLabel = new LabelDocument
            {
                Name = "Master label",
                SizeMm = template.LabelSizeMm,
            },
        };
        return project;
    }

    public void TouchModified() => Metadata.ModifiedUtc = DateTimeOffset.UtcNow;

    public ProjectModel Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Metadata = Metadata.Clone(),
        Template = Template,
        MasterLabel = MasterLabel.Clone(),
        Pages = Pages.Select(p => p.Clone()).ToList(),
        DataConnections = DataConnections.Select(d => d.Clone()).ToList(),
        Sequences = Sequences.Select(s => s.Clone()).ToList(),
        Variables = Variables.Select(v => v.Clone()).ToList(),
        PrintSettings = PrintSettings.Clone(),
        RecordOverrides = RecordOverrides.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Select(o => o.Clone()).ToList()),
        ActiveDataConnectionId = ActiveDataConnectionId,
        SharedGuides = SharedGuides,
    };
}
