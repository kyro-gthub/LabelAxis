using LabelMaker.Core.Model;
using LabelMaker.Core.Serialization;

namespace LabelMaker.Core.Designs;

/// <summary>
/// The user's own designs: a page saved to reuse later, shown first in the design gallery under
/// <see cref="Group"/>. Each is an ordinary one-page project file (with its sheet, fonts,
/// pictures, data connection and a picture of it) in the designs folder, so it can also be
/// copied to another PC or opened like any project.
/// </summary>
public sealed class MyDesigns
{
    /// <summary>The gallery group the user's designs are listed under.</summary>
    public const string Group = "My designs";

    private const string Extension = ".labelproj";

    public MyDesigns(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = folder;
    }

    /// <summary>Where the designs are kept.</summary>
    public string Folder { get; }

    /// <summary>Every saved design, newest first. Files that cannot be read are left out.</summary>
    public IReadOnlyList<LabelDesign> List()
    {
        if (!Directory.Exists(Folder))
        {
            return Array.Empty<LabelDesign>();
        }

        var designs = new List<(LabelDesign Design, DateTime Saved)>();
        foreach (string path in Directory.EnumerateFiles(Folder, "*" + Extension))
        {
            try
            {
                ProjectDocument document = ProjectFile.Load(path);
                designs.Add((Describe(document, path), File.GetLastWriteTimeUtc(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or
                                           System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
            {
                // A damaged or half-copied file is skipped rather than breaking the gallery.
            }
        }

        return designs.OrderByDescending(d => d.Saved).Select(d => d.Design).ToList();
    }

    /// <summary>The design of that name (any case), or null.</summary>
    public LabelDesign? Find(string name) =>
        List().FirstOrDefault(d => string.Equals(d.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Saves <paramref name="page"/> of <paramref name="source"/> as a design named
    /// <paramref name="name"/>, replacing a design of the same name. The page keeps its sheet,
    /// print settings, data connections, variables, sequences, the fonts and pictures it uses,
    /// and <paramref name="thumbnailPng"/> as its picture in the gallery.
    /// </summary>
    public LabelDesign Save(ProjectDocument source, LabelDocument page, string name, string? description = null, byte[]? thumbnailPng = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("A design needs a name.", nameof(name));
        }

        ProjectModel from = source.Project;
        LabelDocument copy = page.Clone();
        copy.Name = "Page 1";
        var project = new ProjectModel
        {
            Metadata = new ProjectMetadata
            {
                Title = name,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                Tags = new List<string> { Group },
            },
            Template = from.Template,
            MasterLabel = copy,
            DataConnections = from.DataConnections.Select(d => d.Clone()).ToList(),
            ActiveDataConnectionId = from.ActiveDataConnectionId,
            Sequences = from.Sequences.Select(s => s.Clone()).ToList(),
            Variables = from.Variables.Select(v => v.Clone()).ToList(),
            PrintSettings = from.PrintSettings.Clone(),
            SharedGuides = from.SharedGuides,
        };

        // Only the fonts the page uses, and the pictures and font files behind them.
        IReadOnlySet<string> families = FontUsage.Families(new ProjectModel { MasterLabel = copy });
        project.Fonts = from.Fonts.Where(f => families.Contains(f.Family)).Select(f => f.Clone()).ToList();
        var document = new ProjectDocument { Project = project, ThumbnailPng = thumbnailPng };
        foreach (string id in ProjectFile.CollectReferencedAssetIds(project))
        {
            if (source.Assets.Get(id) is { } asset)
            {
                document.Assets.AddExisting(asset);
            }
        }

        document.DataCaches.AddRange(source.DataCaches);

        Directory.CreateDirectory(Folder);
        string path = Find(name)?.SavedPath ?? FreePath(name);
        ProjectFile.Save(document, path);
        return Describe(document, path);
    }

    /// <summary>A new, unsaved project from the design (the design itself is left as it is).</summary>
    public static ProjectDocument Open(LabelDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (design.SavedPath is not { } path)
        {
            throw new ArgumentException("This is not one of your designs.", nameof(design));
        }

        ProjectDocument saved = ProjectFile.Load(path);
        ProjectModel project = saved.Project;
        project.Metadata = new ProjectMetadata { Title = project.Metadata.Title };
        var document = new ProjectDocument { Project = project, Assets = saved.Assets, FilePath = null, HasUnsavedChanges = false };
        document.DataCaches.AddRange(saved.DataCaches);
        return document;
    }

    /// <summary>Gives the design another name (and description), keeping everything else.</summary>
    public LabelDesign Rename(LabelDesign design, string name, string? description)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (design.SavedPath is not { } path)
        {
            throw new ArgumentException("This is not one of your designs.", nameof(design));
        }

        name = (name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("A design needs a name.", nameof(name));
        }

        ProjectDocument document = ProjectFile.Load(path);
        document.Project.Metadata.Title = name;
        document.Project.Metadata.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ProjectFile.Save(document, path);
        return Describe(document, path);
    }

    /// <summary>Removes the design's file (to the Recycle Bin is the caller's choice; this deletes it).</summary>
    public static void Delete(LabelDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (design.SavedPath is { } path && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>The gallery card of a saved design: its name, sheet and size.</summary>
    private static LabelDesign Describe(ProjectDocument document, string path)
    {
        ProjectModel project = document.Project;
        Formtec.LabelTemplate? template = project.Template;
        Units.SizeMm size = project.MasterLabel.SizeMm;
        return new LabelDesign
        {
            Id = "my:" + Path.GetFileNameWithoutExtension(path),
            Name = project.Metadata.Title,
            Group = Group,
            Description = project.Metadata.Description ?? string.Empty,
            Size = new DesignSize
            {
                Sheet = template is null ? string.Empty
                    : template.IsRoll ? template.Name
                    : $"{template.Brand} {template.ProductCode}".Trim(),
                WidthMm = size.Width,
                HeightMm = size.Height,
                Media = template?.IsRoll == true ? "thermal" : "sheet",
                Shape = template?.Grid.LabelShape == Formtec.LabelShape.Ellipse ? "round" : null,
            },
            SavedPath = path,
            ThumbnailPng = document.ThumbnailPng,
        };
    }

    /// <summary>A file name for a new design: its name without the characters Windows refuses.</summary>
    private string FreePath(string name)
    {
        char[] bad = Path.GetInvalidFileNameChars();
        string stem = string.Concat(name.Select(c => bad.Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (stem.Length == 0)
        {
            stem = "Design";
        }

        if (stem.Length > 60)
        {
            stem = stem[..60];
        }

        string path = Path.Combine(Folder, stem + Extension);
        for (int i = 2; File.Exists(path); i++)
        {
            path = Path.Combine(Folder, $"{stem} ({i}){Extension}");
        }

        return path;
    }
}
