using System.IO;
using System.Windows;
using System.Windows.Media;

namespace LabelMaker.App.Rendering;

/// <summary>
/// Fonts that come with LabelAxis, so a label using them looks the same on every PC whether or
/// not Windows has them: five Arabic families (all SIL Open Font License). They are offered in
/// every font list next to the installed fonts, drawn on screen and in print from the app's
/// own files, and handed to the PDF exporter too.
/// </summary>
public static class BundledFonts
{
    private const string Folder = "Assets/Fonts/Arabic/";
    private static readonly Uri BaseUri = new("pack://application:,,,/LabelAxis;component/");

    /// <summary>Family name as it is stored in labels, and its Regular and Bold files.</summary>
    public static IReadOnlyList<(string Family, string Regular, string Bold)> Families { get; } = new[]
    {
        ("Tajawal", "Tajawal-Regular.ttf", "Tajawal-Bold.ttf"),
        ("IBM Plex Sans Arabic", "IBMPlexSansArabic-Regular.ttf", "IBMPlexSansArabic-Bold.ttf"),
        ("Almarai", "Almarai-Regular.ttf", "Almarai-Bold.ttf"),
        ("Amiri", "Amiri-Regular.ttf", "Amiri-Bold.ttf"),
        ("Scheherazade New", "ScheherazadeNew-Regular.ttf", "ScheherazadeNew-Bold.ttf"),
    };

    public static IEnumerable<string> Names => Families.Select(f => f.Family);

    public static bool Contains(string family) => Families.Any(f => string.Equals(f.Family, family, StringComparison.OrdinalIgnoreCase));

    /// <summary>The app's own copy for a bundled family, then a font the user added, otherwise the installed font of that name.</summary>
    public static FontFamily Resolve(string family)
    {
        foreach ((string name, _, _) in Families)
        {
            if (string.Equals(name, family, StringComparison.OrdinalIgnoreCase))
            {
                return new FontFamily(BaseUri, "./" + Folder + "#" + name);
            }
        }

        return CustomFonts.Resolve(family) ?? new FontFamily(family);
    }

    /// <summary>The name a font is stored under: the family after "#" for one of the app's own fonts.</summary>
    public static string NameOf(FontFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        string source = family.Source;
        int hash = source.LastIndexOf('#');
        return hash >= 0 ? source[(hash + 1)..] : source;
    }

    /// <summary>The font file's bytes, for exporters that embed fonts themselves (PDF).</summary>
    public static IEnumerable<(string Family, bool Bold, byte[] Data)> Files()
    {
        foreach ((string family, string regular, string bold) in Families)
        {
            foreach ((string file, bool isBold) in new[] { (regular, false), (bold, true) })
            {
                byte[]? data = Read(file);
                if (data is not null)
                {
                    yield return (family, isBold, data);
                }
            }
        }
    }

    private static byte[]? Read(string file)
    {
        try
        {
            using Stream? stream = Application.GetResourceStream(new Uri(BaseUri, Folder + file))?.Stream;
            if (stream is null)
            {
                return null;
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch (IOException)
        {
            return null;
        }
    }
}
