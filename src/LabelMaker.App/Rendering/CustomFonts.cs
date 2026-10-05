using System.IO;
using System.Windows.Media;
using LabelMaker.Core.Model;
using LabelMaker.Core.Serialization;

namespace LabelMaker.App.Rendering;

/// <summary>A font file that could not be added, with the reason in words for the user.</summary>
public sealed class FontFileException : Exception
{
    public FontFileException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// Fonts the user adds to LabelAxis from a .ttf or .otf file, without installing them in
/// Windows. They are kept in the LabelAxis fonts folder (one folder per family, so a family's
/// bold file joins its regular one), offered in every font list, drawn on screen and in print,
/// and handed to the PDF exporter. A project that uses one carries a copy inside it, and
/// opening that project on another PC adds the font there.
/// </summary>
public static class CustomFonts
{
    private sealed record Face(string Path, bool Bold, bool Italic);

    private sealed class Family
    {
        public required string Name { get; init; }
        public required string Folder { get; init; }
        public List<Face> Faces { get; } = new();
        public FontFamily? Wpf { get; set; }
    }

    private const string RemovedList = "removed.txt";
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Family> Families = new(StringComparer.OrdinalIgnoreCase);
    private static string? _directory;

    /// <summary>Raised when a font is added or removed, so font lists can show it.</summary>
    public static event EventHandler? Changed;

    public static IReadOnlyList<string> Names
    {
        get
        {
            lock (Gate)
            {
                return Families.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }

    public static bool Contains(string family)
    {
        lock (Gate)
        {
            return Families.ContainsKey(family);
        }
    }

    /// <summary>The added font for <paramref name="family"/>, or null when it is not one of them.</summary>
    public static FontFamily? Resolve(string family)
    {
        lock (Gate)
        {
            if (!Families.TryGetValue(family, out Family? f))
            {
                return null;
            }

            // The family's folder: WPF picks the regular, bold or italic file in it as needed.
            return f.Wpf ??= new FontFamily(new Uri(f.Folder.TrimEnd('\\') + "\\"), "./#" + f.Name);
        }
    }

    /// <summary>The files of an added family, for showing in Settings.</summary>
    public static IReadOnlyList<string> FilesOf(string family)
    {
        lock (Gate)
        {
            return Families.TryGetValue(family, out Family? f) ? f.Faces.Select(x => Path.GetFileName(x.Path)).ToList() : Array.Empty<string>();
        }
    }

    /// <summary>Reads the fonts folder at start-up (and first finishes removing what was removed last time).</summary>
    public static void Load(string directory)
    {
        directory = Path.GetFullPath(directory);
        _directory = directory;
        try
        {
            Directory.CreateDirectory(directory);
            string removed = Path.Combine(directory, RemovedList);
            if (File.Exists(removed))
            {
                foreach (string name in File.ReadAllLines(removed).Where(l => l.Length > 0))
                {
                    TryDelete(Path.Combine(directory, name));
                }

                File.Delete(removed);
            }

            foreach (string folder in Directory.EnumerateDirectories(directory))
            {
                foreach (string file in Directory.EnumerateFiles(folder).Where(IsFontFile))
                {
                    try
                    {
                        Register(file);
                    }
                    catch (FontFileException)
                    {
                        // A damaged file in the folder is left out; the rest still load.
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // No fonts folder (read-only profile): only the installed fonts are offered.
        }
    }

    /// <summary>
    /// Adds a font file to LabelAxis and returns its family name. The file is copied into the
    /// fonts folder, so the original can be moved or deleted afterwards.
    /// </summary>
    public static string Add(string path)
    {
        if (_directory is null)
        {
            throw new FontFileException("The fonts folder is not ready.");
        }

        if (!IsFontFile(path))
        {
            throw new FontFileException("Only TrueType (.ttf) and OpenType (.otf) font files can be added.");
        }

        (string family, _, _) = Describe(path);
        string folder = Path.Combine(_directory, SafeName(family));
        Directory.CreateDirectory(folder);

        // Added back after being removed in this session: it is not to be deleted at the next start.
        string removed = Path.Combine(_directory, RemovedList);
        if (File.Exists(removed))
        {
            File.WriteAllLines(removed, File.ReadAllLines(removed).Where(l => !string.Equals(l, Path.GetFileName(folder), StringComparison.OrdinalIgnoreCase)));
        }

        string target = Path.Combine(folder, Path.GetFileName(path));
        if (File.Exists(target) && !SameBytes(target, path))
        {
            target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(path)}-{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(path)}");
        }

        if (!File.Exists(target))
        {
            File.Copy(path, target);
        }

        string name = Register(target);
        Changed?.Invoke(null, EventArgs.Empty);
        return name;
    }

    /// <summary>
    /// Takes a family out of the font lists. Its files go at once when Windows lets go of them,
    /// otherwise the next time LabelAxis starts. Labels that use it fall back to another font.
    /// </summary>
    public static void Remove(string family)
    {
        Family? f;
        lock (Gate)
        {
            if (!Families.Remove(family, out f))
            {
                return;
            }
        }

        if (!TryDelete(f.Folder) && _directory is not null)
        {
            File.AppendAllLines(Path.Combine(_directory, RemovedList), new[] { Path.GetFileName(f.Folder) });
        }

        WpfFontCache.Clear();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Adds the fonts a project carries that this PC has neither added nor installed. Returns
    /// the families added.
    /// </summary>
    public static IReadOnlyList<string> InstallFrom(ProjectDocument document, Func<string, bool> isInstalled)
    {
        ArgumentNullException.ThrowIfNull(document);
        var added = new List<string>();
        if (_directory is null)
        {
            return added;
        }

        foreach (ProjectFont font in document.Project.Fonts)
        {
            if (Contains(font.Family) && !added.Contains(font.Family, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (BundledFonts.Contains(font.Family) || isInstalled(font.Family) || document.Assets.GetBytes(font.AssetId) is not { } bytes)
            {
                continue;
            }

            try
            {
                string folder = Path.Combine(_directory, SafeName(font.Family));
                Directory.CreateDirectory(folder);
                string target = Path.Combine(folder, Path.GetFileName(font.FileName) is { Length: > 0 } n ? n : font.AssetId + ".ttf");
                if (!File.Exists(target))
                {
                    File.WriteAllBytes(target, bytes);
                }

                Register(target);
                if (!added.Contains(font.Family, StringComparer.OrdinalIgnoreCase))
                {
                    added.Add(font.Family);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FontFileException)
            {
                // The label then shows in a fallback font, as it would have without the copy.
            }
        }

        if (added.Count > 0)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }

        return added;
    }

    /// <summary>
    /// Puts a copy of every added font the project uses inside it, and drops copies of fonts it
    /// no longer uses — called just before the project is saved.
    /// </summary>
    public static void EmbedInto(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ProjectModel project = document.Project;
        IReadOnlySet<string> used = FontUsage.Families(project);

        // Fonts that came inside the project stay with it while it uses them, even on a PC
        // where they could not be added.
        var fonts = project.Fonts.Where(f => used.Contains(f.Family) && document.Assets.Contains(f.AssetId)).ToList();
        foreach (string family in used)
        {
            List<Face> faces;
            lock (Gate)
            {
                if (!Families.TryGetValue(family, out Family? f))
                {
                    continue;
                }

                faces = f.Faces.ToList();
            }

            foreach (Face face in faces)
            {
                try
                {
                    string id = document.Assets.Add(File.ReadAllBytes(face.Path), Path.GetExtension(face.Path));
                    if (!fonts.Any(x => x.AssetId == id))
                    {
                        fonts.Add(new ProjectFont { Family = family, AssetId = id, FileName = Path.GetFileName(face.Path) });
                    }
                }
                catch (IOException)
                {
                    // Saved without that file; the label still names the font.
                }
            }
        }

        project.Fonts = fonts;
    }

    private static string Register(string path)
    {
        (string family, bool bold, bool italic) = Describe(path);
        lock (Gate)
        {
            if (!Families.TryGetValue(family, out Family? f))
            {
                f = new Family { Name = family, Folder = Path.GetDirectoryName(Path.GetFullPath(path))! };
                Families[family] = f;
            }

            if (f.Faces.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                return family;
            }

            f.Faces.Add(new Face(path, bold, italic));
            f.Wpf = null;
        }

        // PDF export: a TrueType file is embedded as a real font; PdfSharp cannot embed an
        // OpenType font with PostScript outlines, so text in one is drawn as outlines instead.
        byte[] bytes = File.ReadAllBytes(path);
        if (IsPostScriptOutlines(bytes))
        {
            LabelMaker.Export.Pdf.PdfDrawingSurface.DrawAsOutlines(family);
        }
        else if (!italic)
        {
            LabelMaker.Export.Pdf.WindowsFontResolver.AddPrivateFont(family, bold, bytes);
        }

        WpfFontCache.Clear();
        return family;
    }

    /// <summary>The family name a font file gives itself, and whether it is a bold or italic face.</summary>
    private static (string Family, bool Bold, bool Italic) Describe(string path)
    {
        try
        {
            var glyphs = new GlyphTypeface(new Uri(Path.GetFullPath(path)));
            string? family = glyphs.FamilyNames.TryGetValue(System.Globalization.CultureInfo.GetCultureInfo("en-US"), out string? en)
                ? en
                : glyphs.FamilyNames.Values.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(family))
            {
                throw new FontFileException($"“{Path.GetFileName(path)}” does not say what font it is.");
            }

            return (family.Trim(), glyphs.Weight.ToOpenTypeWeight() >= 600, glyphs.Style != System.Windows.FontStyles.Normal);
        }
        catch (Exception e) when (e is not FontFileException)
        {
            throw new FontFileException($"“{Path.GetFileName(path)}” is not a font file LabelAxis can read.", e);
        }
    }

    private static bool IsFontFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".ttf" or ".otf";

    // "OTTO" opens an OpenType font with PostScript (CFF) outlines.
    private static bool IsPostScriptOutlines(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == 'O' && bytes[1] == 'T' && bytes[2] == 'T' && bytes[3] == 'O';

    private static string SafeName(string family)
    {
        char[] bad = Path.GetInvalidFileNameChars();
        string safe = new(family.Select(c => bad.Contains(c) ? '_' : c).ToArray());
        return safe.Trim().TrimEnd('.') is { Length: > 0 } s ? s : "Font";
    }

    private static bool SameBytes(string a, string b) =>
        new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));

    private static bool TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
