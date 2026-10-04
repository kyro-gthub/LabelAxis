using PdfSharp.Fonts;

namespace LabelMaker.Export.Pdf;

/// <summary>
/// Resolves fonts for PdfSharp from the Windows font directory, with a safe
/// fallback chain so a missing family never breaks a PDF export. Registered once
/// via <see cref="Register"/>.
/// </summary>
public sealed class WindowsFontResolver : IFontResolver
{
    private static readonly object Gate = new();
    private static bool _registered;

    private readonly string _fontDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, (string Regular, string Bold, string Italic, string BoldItalic)> KnownFamilies =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Segoe UI"] = ("segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"),
            ["Arial"] = ("arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"),
            ["Calibri"] = ("calibri.ttf", "calibrib.ttf", "calibrii.ttf", "calibriz.ttf"),
            ["Tahoma"] = ("tahoma.ttf", "tahomabd.ttf", "tahoma.ttf", "tahomabd.ttf"),
            ["Verdana"] = ("verdana.ttf", "verdanab.ttf", "verdanai.ttf", "verdanaz.ttf"),
            ["Times New Roman"] = ("times.ttf", "timesbd.ttf", "timesi.ttf", "timesbi.ttf"),
            ["Courier New"] = ("cour.ttf", "courbd.ttf", "couri.ttf", "courbi.ttf"),
            ["Consolas"] = ("consola.ttf", "consolab.ttf", "consolai.ttf", "consolaz.ttf"),
        };

    // Fonts that come with the app rather than with Windows: family -> (regular, bold) bytes.
    private static readonly Dictionary<string, (byte[]? Regular, byte[]? Bold)> PrivateFonts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Makes a font the app ships with available to PDF export under <paramref name="family"/>.</summary>
    public static void AddPrivateFont(string family, bool bold, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentNullException.ThrowIfNull(data);
        lock (Gate)
        {
            (byte[]? regular, byte[]? boldData) = PrivateFonts.GetValueOrDefault(family);
            PrivateFonts[family] = bold ? (regular, data) : (data, boldData);
        }
    }

    public static void Register()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            GlobalFontSettings.FontResolver = new WindowsFontResolver();
            _registered = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        lock (Gate)
        {
            if (PrivateFonts.ContainsKey(familyName))
            {
                return new FontResolverInfo("private:" + familyName + "|" + (isBold ? "b" : string.Empty));
            }
        }

        string family = KnownFamilies.ContainsKey(familyName) ? familyName
            : familyName.Contains("mono", StringComparison.OrdinalIgnoreCase) ||
              familyName.Contains("consol", StringComparison.OrdinalIgnoreCase) ? "Consolas"
            : familyName.Contains("times", StringComparison.OrdinalIgnoreCase) ||
              familyName.Contains("serif", StringComparison.OrdinalIgnoreCase) ? "Times New Roman"
            : "Arial";

        string face = $"{family}|{(isBold ? "b" : string.Empty)}{(isItalic ? "i" : string.Empty)}";
        return new FontResolverInfo(face);
    }

    public byte[]? GetFont(string faceName)
    {
        if (_cache.TryGetValue(faceName, out byte[]? cached))
        {
            return cached;
        }

        if (faceName.StartsWith("private:", StringComparison.Ordinal))
        {
            string[] p = faceName["private:".Length..].Split('|');
            lock (Gate)
            {
                (byte[]? regular, byte[]? boldData) = PrivateFonts.GetValueOrDefault(p[0]);
                byte[]? data = p.Length > 1 && p[1].Contains('b') ? boldData ?? regular : regular ?? boldData;
                if (data is not null)
                {
                    _cache[faceName] = data;
                    return data;
                }
            }
        }

        string[] parts = faceName.Split('|');
        string family = parts[0];
        string style = parts.Length > 1 ? parts[1] : string.Empty;
        bool bold = style.Contains('b');
        bool italic = style.Contains('i');

        var faces = KnownFamilies.GetValueOrDefault(family, KnownFamilies["Arial"]);
        string file = (bold, italic) switch
        {
            (true, true) => faces.BoldItalic,
            (true, false) => faces.Bold,
            (false, true) => faces.Italic,
            _ => faces.Regular,
        };

        byte[] bytes = ReadFontFile(file) ?? ReadFontFile(KnownFamilies["Arial"].Regular) ?? Array.Empty<byte>();
        _cache[faceName] = bytes;
        return bytes.Length > 0 ? bytes : null;
    }

    private byte[]? ReadFontFile(string fileName)
    {
        string path = Path.Combine(_fontDir, fileName);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
