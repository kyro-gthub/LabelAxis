using System.Text;

namespace LabelMaker.Core.Help;

/// <summary>A piece of a line: plain, bold, a key or menu name, or a link to another topic.</summary>
public abstract record HelpInline(string Text);

public sealed record HelpText(string Text) : HelpInline(Text);

public sealed record HelpBold(string Text) : HelpInline(Text);

/// <summary>A key, button or menu path, shown in a box: `Ctrl+P`, `Print > Wi-Fi printers…`.</summary>
public sealed record HelpKey(string Text) : HelpInline(Text);

/// <summary>A link to another topic: [[topic-id|text]], or [[topic-id]] for its title.</summary>
public sealed record HelpLink(string TopicId, string Text) : HelpInline(Text);

public abstract record HelpBlock;

public sealed record HelpHeading(int Level, string Text) : HelpBlock;

public sealed record HelpParagraph(IReadOnlyList<HelpInline> Inlines) : HelpBlock;

/// <summary>A list; <see cref="Levels"/> gives each item's depth (0, or 1 for a point under the item before it).</summary>
public sealed record HelpList(bool Numbered, IReadOnlyList<IReadOnlyList<HelpInline>> Items, IReadOnlyList<int>? Levels = null) : HelpBlock
{
    public int LevelOf(int index) => Levels is { } l && index < l.Count ? l[index] : 0;
}

/// <summary>A boxed note: "Tip", "Note" or "Important".</summary>
public sealed record HelpCallout(string Kind, IReadOnlyList<HelpInline> Inlines) : HelpBlock;

public sealed record HelpTable(IReadOnlyList<IReadOnlyList<IReadOnlyList<HelpInline>>> Rows) : HelpBlock;

/// <summary>One page of the help guide.</summary>
public sealed record HelpTopic(string Id, string Title, string Category, int Order, IReadOnlyList<string> Keywords, IReadOnlyList<HelpBlock> Blocks)
{
    /// <summary>All the words of the page, for searching.</summary>
    public string PlainText { get; } = string.Join(' ', Blocks.Select(HelpMarkup.TextOf));
}

/// <summary>
/// Reads help pages written in a small Markdown-like format:
/// <code>
/// ---
/// id: printing
/// title: Printing
/// category: Printing
/// order: 10
/// keywords: print, sheet, copies
/// ---
/// # Heading, ## Subheading
/// A paragraph with **bold**, `Ctrl+P` and a [[calibration|link]].
/// - a bullet
/// 1. a step
/// > Tip: a boxed tip (also Note: and Important:)
/// | a table | row |
/// </code>
/// </summary>
public static class HelpMarkup
{
    public static HelpTopic Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            for (i = 1; i < lines.Length && lines[i].Trim() != "---"; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0)
                {
                    meta[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
                }
            }

            i++;
        }

        var blocks = new List<HelpBlock>();
        var paragraph = new StringBuilder();
        List<IReadOnlyList<HelpInline>>? list = null;
        List<int>? levels = null;
        bool numbered = false;
        List<IReadOnlyList<IReadOnlyList<HelpInline>>>? table = null;

        void FlushParagraph()
        {
            if (paragraph.Length > 0)
            {
                blocks.Add(new HelpParagraph(Inlines(paragraph.ToString())));
                paragraph.Clear();
            }
        }

        void FlushList()
        {
            if (list is { Count: > 0 })
            {
                blocks.Add(new HelpList(numbered, list, levels));
            }

            list = null;
            levels = null;
        }

        void FlushTable()
        {
            if (table is { Count: > 0 })
            {
                blocks.Add(new HelpTable(table));
            }

            table = null;
        }

        void FlushAll()
        {
            FlushParagraph();
            FlushList();
            FlushTable();
        }

        for (; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            string t = line.TrimStart();

            if (t.Length == 0)
            {
                FlushAll();
                continue;
            }

            if (t.StartsWith('#'))
            {
                FlushAll();
                int level = t.TakeWhile(c => c == '#').Count();
                blocks.Add(new HelpHeading(Math.Clamp(level, 1, 3), t[level..].Trim()));
                continue;
            }

            if (t.StartsWith('>'))
            {
                FlushAll();
                string body = t[1..].Trim();
                string kind = "Note";
                foreach (string k in new[] { "Tip", "Note", "Important" })
                {
                    if (body.StartsWith(k + ":", StringComparison.OrdinalIgnoreCase))
                    {
                        kind = k;
                        body = body[(k.Length + 1)..].Trim();
                        break;
                    }
                }

                // A callout continues on following "> " lines.
                while (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith('>'))
                {
                    body += " " + lines[++i].TrimStart()[1..].Trim();
                }

                blocks.Add(new HelpCallout(kind, Inlines(body)));
                continue;
            }

            if (t.StartsWith('|'))
            {
                FlushParagraph();
                FlushList();
                string[] cells = t.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                if (cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':')))
                {
                    continue;   // the |---|---| line under a header
                }

                table ??= new List<IReadOnlyList<IReadOnlyList<HelpInline>>>();
                table.Add(cells.Select(Inlines).ToList());
                continue;
            }

            bool bullet = t.StartsWith("- ", StringComparison.Ordinal) || t.StartsWith("* ", StringComparison.Ordinal);
            int dot = t.IndexOf(". ", StringComparison.Ordinal);
            bool step = dot > 0 && dot <= 3 && t[..dot].All(char.IsDigit);
            if (bullet || step)
            {
                FlushParagraph();
                FlushTable();
                // An indented point under an item is a sub-point of it; it never starts a new list.
                int level = list is not null && line.Length - t.Length >= 2 ? 1 : 0;
                if (list is not null && level == 0 && numbered != step)
                {
                    FlushList();
                }

                if (list is null)
                {
                    numbered = step;
                }

                list ??= new List<IReadOnlyList<HelpInline>>();
                levels ??= new List<int>();
                list.Add(Inlines(bullet ? t[2..] : t[(dot + 2)..]));
                levels.Add(level);
                continue;
            }

            // A line indented under a list item carries on that item.
            if (list is not null && line.StartsWith("  ", StringComparison.Ordinal))
            {
                list[^1] = list[^1].Concat(Inlines(" " + t)).ToList();
                continue;
            }

            FlushList();
            FlushTable();
            paragraph.Append(paragraph.Length > 0 ? " " : string.Empty).Append(t);
        }

        FlushAll();

        string id = meta.GetValueOrDefault("id") ?? throw new FormatException("A help page needs an id.");
        return new HelpTopic(
            id,
            meta.GetValueOrDefault("title") ?? id,
            meta.GetValueOrDefault("category") ?? "General",
            int.TryParse(meta.GetValueOrDefault("order"), out int order) ? order : 100,
            (meta.GetValueOrDefault("keywords") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            blocks);
    }

    /// <summary>Splits a line into plain text, **bold**, `keys` and [[links]].</summary>
    public static IReadOnlyList<HelpInline> Inlines(string text)
    {
        var result = new List<HelpInline>();
        var plain = new StringBuilder();

        void Flush()
        {
            if (plain.Length > 0)
            {
                result.Add(new HelpText(plain.ToString()));
                plain.Clear();
            }
        }

        int i = 0;
        while (i < text.Length)
        {
            if (text.AsSpan(i).StartsWith("**") && text.IndexOf("**", i + 2, StringComparison.Ordinal) is var endBold and > 0)
            {
                Flush();
                result.Add(new HelpBold(text[(i + 2)..endBold]));
                i = endBold + 2;
            }
            else if (text[i] == '`' && text.IndexOf('`', i + 1) is var endKey and > 0)
            {
                Flush();
                result.Add(new HelpKey(text[(i + 1)..endKey]));
                i = endKey + 1;
            }
            else if (text.AsSpan(i).StartsWith("[[") && text.IndexOf("]]", i + 2, StringComparison.Ordinal) is var endLink and > 0)
            {
                Flush();
                string inner = text[(i + 2)..endLink];
                int bar = inner.IndexOf('|');
                result.Add(bar > 0 ? new HelpLink(inner[..bar].Trim(), inner[(bar + 1)..].Trim()) : new HelpLink(inner.Trim(), string.Empty));
                i = endLink + 2;
            }
            else
            {
                plain.Append(text[i]);
                i++;
            }
        }

        Flush();
        return result;
    }

    public static string TextOf(HelpBlock block) => block switch
    {
        HelpHeading h => h.Text,
        HelpParagraph p => Join(p.Inlines),
        HelpList l => string.Join(' ', l.Items.Select(Join)),
        HelpCallout c => Join(c.Inlines),
        HelpTable t => string.Join(' ', t.Rows.SelectMany(r => r).Select(Join)),
        _ => string.Empty,
    };

    private static string Join(IEnumerable<HelpInline> inlines) => string.Concat(inlines.Select(x => x.Text));
}

/// <summary>All the help pages, in order, with search.</summary>
public sealed class HelpLibrary
{
    public HelpLibrary(IEnumerable<HelpTopic> topics)
    {
        ArgumentNullException.ThrowIfNull(topics);
        Topics = topics.OrderBy(t => CategoryOrder(t.Category)).ThenBy(t => t.Order).ThenBy(t => t.Title, StringComparer.CurrentCulture).ToList();
    }

    public IReadOnlyList<HelpTopic> Topics { get; }

    public HelpTopic? Find(string? id) => Topics.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Pages matching every word of <paramref name="query"/>, best first: a word in the title
    /// counts most, then the keywords, then the text.
    /// </summary>
    public IReadOnlyList<HelpTopic> Search(string? query)
    {
        string[] words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return Topics;
        }

        return Topics
            .Select(t => (Topic: t, Score: Score(t, words)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Topic)
            .ToList();
    }

    /// <summary>A short piece of the page around the first searched word, for the results list.</summary>
    public static string Snippet(HelpTopic topic, string? query, int length = 110)
    {
        ArgumentNullException.ThrowIfNull(topic);
        string text = topic.PlainText;
        string? word = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        int at = word is null ? -1 : text.IndexOf(word, StringComparison.CurrentCultureIgnoreCase);
        int start = Math.Max(0, at - 30);
        string piece = text.Substring(start, Math.Min(length, text.Length - start)).Trim();
        return (start > 0 ? "…" : string.Empty) + piece + (start + length < text.Length ? "…" : string.Empty);
    }

    private static int Score(HelpTopic t, string[] words)
    {
        int total = 0;
        foreach (string w in words)
        {
            int s = 0;
            if (t.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase))
            {
                s += 10;
            }

            if (t.Keywords.Any(k => k.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            {
                s += 6;
            }

            if (t.PlainText.Contains(w, StringComparison.CurrentCultureIgnoreCase))
            {
                s += 2;
            }

            if (s == 0)
            {
                return 0;   // every word has to be somewhere
            }

            total += s;
        }

        return total;
    }

    private static int CategoryOrder(string category) => category switch
    {
        "Getting started" => 0,
        "Label sheets" => 1,
        "Designing" => 2,
        "Pages" => 3,
        "Data" => 4,
        "Printing" => 5,
        "Exporting" => 6,
        "Reference" => 7,
        _ => 8,
    };
}
