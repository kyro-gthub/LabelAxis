using System.Text.Json;
using System.Text.RegularExpressions;

namespace LabelMaker.Core.Localization;

/// <summary>
/// Turns the English text of the interface into another language, from a list of
/// English → translation pairs. Text the program puts together at run time ("Imported 12
/// records from stock.csv") is matched against entries with numbered holes ("Imported {0}
/// records from {1}"), and text made of several sentences or parts is translated part by part.
/// Anything it has no entry for is left in English rather than guessed.
/// </summary>
public sealed partial class Translator
{
    private const int MaxDepth = 3;
    private const int CacheLimit = 20_000;

    private readonly Dictionary<string, string> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _anyCase = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Template> _templates = new();
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private static readonly JsonSerializerOptions FileOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public Translator(IEnumerable<KeyValuePair<string, string>> entries)
    {
        foreach ((string english, string translated) in entries)
        {
            if (string.IsNullOrWhiteSpace(english) || string.IsNullOrWhiteSpace(translated))
            {
                continue;
            }

            string key = Normalise(english);
            _exact[key] = translated;
            _anyCase.TryAdd(key, translated);
            if (HoleRegex().IsMatch(key) && Template.TryCreate(key, translated) is { } template)
            {
                _templates.Add(template);
            }
        }

        // Longer patterns first, so "Printing {0} sheets × {1} copies" wins over "Printing {0}".
        _templates.Sort((a, b) => b.LiteralLength.CompareTo(a.LiteralLength));
    }

    /// <summary>English: every text is shown as it is.</summary>
    public static Translator None { get; } = new(Array.Empty<KeyValuePair<string, string>>());

    public int Count => _exact.Count;

    public bool IsEmpty => _exact.Count == 0;

    /// <summary>Reads a language file: one JSON object of "English": "translation" pairs.</summary>
    public static Translator FromJson(string json)
    {
        var pairs = JsonSerializer.Deserialize<Dictionary<string, string>>(json, FileOptions) ?? new Dictionary<string, string>();
        return new Translator(pairs);
    }

    /// <summary>True when there is an entry for exactly this English text.</summary>
    public bool Knows(string english) => _exact.ContainsKey(Normalise(english));

    /// <summary>The text in this language, or the text itself when there is no translation.</summary>
    public string Translate(string? text)
    {
        if (string.IsNullOrEmpty(text) || IsEmpty || !LatinLetter().IsMatch(text))
        {
            return text ?? string.Empty;
        }

        lock (_gate)
        {
            if (_cache.TryGetValue(text, out string? known))
            {
                return known;
            }
        }

        string result = Whole(text, 0) ?? Parts(text, 0) ?? text;
        lock (_gate)
        {
            if (_cache.Count >= CacheLimit)
            {
                _cache.Clear();
            }

            _cache[text] = result;
        }

        return result;
    }

    /// <summary>
    /// Fills in a template in this language: <c>Format("Saved {0} picture(s) to {1}.", 3, path)</c>.
    /// The arguments are put in as they are.
    /// </summary>
    public string Format(string englishTemplate, params object?[] args)
    {
        string pattern = _exact.TryGetValue(Normalise(englishTemplate), out string? t) ? t : englishTemplate;
        try
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, args);
        }
        catch (FormatException)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, englishTemplate, args);
        }
    }

    // Line ends are kept as \n in the file; the menus mark access keys with _ ("_File").
    private static string Normalise(string s) => s.Replace("\r\n", "\n");

    private string? Whole(string text, int depth)
    {
        if (Lookup(text) is { } direct)
        {
            return direct;
        }

        // Keep the spaces around the text and the "…" or ":" after it.
        Match frame = FrameRegex().Match(text);
        string core = frame.Groups["core"].Value;
        if (core.Length > 0 && core.Length != text.Length && Lookup(core) is { } framed)
        {
            return frame.Groups["lead"].Value + framed + Punctuation(frame.Groups["tail"].Value);
        }

        if (depth >= MaxDepth)
        {
            return null;
        }

        // Without its full stop first, so "… from x.csv." does not take the stop into the file name.
        if (core.Length > 0 && core.Length != text.Length && FromTemplate(core, depth) is { } inner)
        {
            return frame.Groups["lead"].Value + inner + Punctuation(frame.Groups["tail"].Value);
        }

        return FromTemplate(text, depth);
    }

    private string? FromTemplate(string text, int depth)
    {
        foreach (Template template in _templates)
        {
            if (template.TryFill(text, hole => Whole(hole, depth + 1) ?? Parts(hole, depth + 1) ?? hole) is { } filled)
            {
                return filled;
            }
        }

        return null;
    }

    private string? Lookup(string s)
    {
        string key = Normalise(s);
        if (_exact.TryGetValue(key, out string? hit))
        {
            return hit;
        }

        // Menus and buttons: "_Save as…" is the same words as "Save as…".
        if (key.Contains('_') && AccessKey().Replace(key, "$1") is var plain && plain != key &&
            _exact.TryGetValue(plain, out hit))
        {
            return hit;
        }

        // Headings written in capitals ("CURRENT PAGE") use the entry for "Current page".
        if (key.Length > 1 && !key.Any(char.IsLower) && _anyCase.TryGetValue(key, out hit))
        {
            return hit;
        }

        return null;
    }

    // Arabic script has its own comma and question mark; the others read the same both ways.
    private static string Punctuation(string tail) => tail.Replace("?", "؟").Replace(",", "،");

    private string? Parts(string text, int depth)
    {
        string[] pieces = Separators().Split(text);
        if (pieces.Length < 2)
        {
            return null;
        }

        bool changed = false;
        for (int i = 0; i < pieces.Length; i += 2)
        {
            if (pieces[i].Length == 0 || !LatinLetter().IsMatch(pieces[i]))
            {
                continue;
            }

            if (Whole(pieces[i], depth) is { } translated)
            {
                pieces[i] = translated;
                changed = true;
            }
        }

        return changed ? string.Concat(pieces) : null;
    }

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex HoleRegex();

    [GeneratedRegex("[A-Za-z]")]
    private static partial Regex LatinLetter();

    [GeneratedRegex(@"_(?!_)(\S)")]
    private static partial Regex AccessKey();

    // The lead also keeps a list marker: "★  Master label", "2.  Page 2", "↻  Look again".
    [GeneratedRegex(@"^(?<lead>(?:\s|[★☆✓•·＋+↻]|\d+\.\s)*)(?<core>.*?)(?<tail>[\s.…:,;!?)]*)$", RegexOptions.Singleline)]
    private static partial Regex FrameRegex();

    // Sentences, lines and the " · " / " — " that join parts of a status line.
    [GeneratedRegex(@"(\n+|\s+[·—|]\s+|(?<=[.!?:])\s+(?=\S)|(?<=\S), (?=[A-Z]))")]
    private static partial Regex Separators();

    /// <summary>The left-to-right mark, which keeps English inside right-to-left text in its own order.</summary>
    public const char LeftToRightMark = (char)0x200E;

    private static bool IsRightToLeftLetter(char c) => c is >= (char)0x0590 and <= (char)0x08FF or >= (char)0xFB1D and <= (char)0xFEFC;

    private sealed class Template
    {
        private readonly Regex _pattern;
        private readonly string _translation;
        private readonly int[] _holes;

        private Template(Regex pattern, string translation, int[] holes, int literalLength)
        {
            _pattern = pattern;
            _translation = translation;
            _holes = holes;
            LiteralLength = literalLength;
        }

        public int LiteralLength { get; }

        public static Template? TryCreate(string english, string translation)
        {
            var holes = new List<int>();
            var regex = new System.Text.StringBuilder("^");
            int last = 0;
            foreach (Match m in HoleRegex().Matches(english))
            {
                regex.Append(Regex.Escape(english[last..m.Index]));
                regex.Append("(.+?)");
                holes.Add(int.Parse(m.Value[1..^1], System.Globalization.CultureInfo.InvariantCulture));
                last = m.Index + m.Length;
            }

            regex.Append(Regex.Escape(english[last..])).Append('$');
            string literal = HoleRegex().Replace(english, string.Empty);

            // "{0} · {1}" has nothing to recognise it by; the parts are translated on their own.
            // "{0} of {1}" would catch "Interleaved 2 of 5", so a two-letter word only counts with
            // three or more holes ("{0} {1} on {2}").
            int letters = literal.Count(char.IsLetter);
            if (letters < 2 || (letters == 2 && holes.Count < 3))
            {
                return null;
            }

            return new Template(
                new Regex(regex.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant),
                translation, holes.ToArray(), literal.Length);
        }

        public string? TryFill(string text, Func<string, string> translateHole)
        {
            Match m = _pattern.Match(text);
            if (!m.Success)
            {
                return null;
            }

            bool rightToLeft = _translation.Any(IsRightToLeftLetter);
            var filled = new string[_holes.Length];
            for (int i = 0; i < _holes.Length; i++)
            {
                filled[i] = translateHole(m.Groups[i + 1].Value);

                // A file, printer or technical name inside Arabic or Kurdish keeps its own order
                // ("64-bit" must not read "bit-64"): left-to-right marks hold it together.
                if (rightToLeft && LatinLetter().IsMatch(filled[i]) && !filled[i].Any(IsRightToLeftLetter))
                {
                    filled[i] = LeftToRightMark + filled[i] + LeftToRightMark;
                }
            }

            // One pass, so a hole whose own text contains "{1}" is not filled in twice.
            return HoleRegex().Replace(_translation, hole =>
            {
                int index = Array.IndexOf(_holes, int.Parse(hole.Value[1..^1], System.Globalization.CultureInfo.InvariantCulture));
                return index >= 0 ? filled[index] : hole.Value;
            });
        }
    }
}
