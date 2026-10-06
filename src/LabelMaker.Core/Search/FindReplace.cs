using System.Text;
using System.Text.RegularExpressions;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;

namespace LabelMaker.Core.Search;

public enum FindScope
{
    /// <summary>Only the label currently open on the canvas.</summary>
    CurrentLabel = 0,

    /// <summary>The master label and every standalone page.</summary>
    AllPages = 1,
}

[Flags]
public enum FindTargets
{
    None = 0,
    Text = 1,
    BarcodeData = 2,
    TableCells = 4,
    ObjectNames = 8,
    PageNames = 16,
    All = Text | BarcodeData | TableCells | ObjectNames | PageNames,
}

/// <summary>Where inside an object a match was found.</summary>
public enum FindField
{
    Text = 0,
    BarcodeData = 1,
    TableCell = 2,
    ObjectName = 3,

    /// <summary>The page's own name; the match has no object.</summary>
    PageName = 4,
}

public sealed record FindOptions
{
    public string Query { get; init; } = string.Empty;
    public bool MatchCase { get; init; }
    public bool WholeWord { get; init; }
    public bool UseRegex { get; init; }

    /// <summary>
    /// Leave <c>{Field}</c> tokens alone. On by default: replacing "Price" should
    /// not silently turn a <c>{Price}</c> binding into a broken <c>{Cost}</c> one.
    /// </summary>
    public bool SkipTokens { get; init; } = true;

    public FindScope Scope { get; init; } = FindScope.AllPages;
    public FindTargets Targets { get; init; } = FindTargets.All;
}

/// <summary>
/// One occurrence. <see cref="Part"/> is the paragraph index for text and the cell
/// index for tables (0 otherwise); <see cref="Start"/> is the offset inside that
/// part's plain text, so a single match can be replaced on its own. A match in a page's
/// name (<see cref="FindField.PageName"/>) has no <see cref="Object"/>.
/// </summary>
public sealed record FindMatch(
    string? PageId,
    string PageName,
    LabelObject? Object,
    FindField Field,
    int Part,
    int Start,
    int Length,
    string Before,
    string Hit,
    string After);

/// <summary>
/// Find and replace across label designs: text boxes (run formatting preserved),
/// barcode values, table cells and object names, on one label or the whole
/// project. Replacements are returned as undoable commands so the caller pushes a
/// single "Replace all" onto the undo stack.
/// </summary>
public static class FindReplaceEngine
{
    private const int ContextChars = 24;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Builds the matcher for a query. Plain text is escaped, so only regex mode
    /// treats characters like "." or "(" specially. Throws <see cref="ArgumentException"/>
    /// for an invalid regular expression.
    /// </summary>
    public static Regex BuildRegex(FindOptions options)
    {
        string pattern = options.UseRegex ? options.Query : Regex.Escape(options.Query);
        if (options.WholeWord)
        {
            // \b misbehaves when the query starts or ends with punctuation; explicit
            // look-arounds treat "word" the same way whatever the query contains.
            pattern = $@"(?<![\p{{L}}\p{{N}}_])(?:{pattern})(?![\p{{L}}\p{{N}}_])";
        }

        RegexOptions flags = RegexOptions.CultureInvariant;
        if (!options.MatchCase)
        {
            flags |= RegexOptions.IgnoreCase;
        }

        return new Regex(pattern, flags, RegexTimeout);
    }

    /// <summary>Returns a user-facing reason the query cannot run, or null if it is fine.</summary>
    public static string? Validate(FindOptions options)
    {
        if (string.IsNullOrEmpty(options.Query))
        {
            return "Type something to find.";
        }

        if (options.Targets == FindTargets.None)
        {
            return "Choose at least one place to look.";
        }

        try
        {
            _ = BuildRegex(options);
            return null;
        }
        catch (ArgumentException ex)
        {
            return "Invalid regular expression: " + ex.Message;
        }
    }

    // ---- finding ----------------------------------------------------------

    public static IReadOnlyList<FindMatch> FindAll(ProjectModel project, string? activePageId, FindOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (Validate(options) is not null)
        {
            return Array.Empty<FindMatch>();
        }

        Regex rx = BuildRegex(options);
        var results = new List<FindMatch>();

        foreach ((string? pageId, string pageName, LabelDocument doc) in Documents(project, activePageId, options.Scope))
        {
            if (options.Targets.HasFlag(FindTargets.PageNames))
            {
                foreach (Match m in Matches(rx, pageName, options.SkipTokens))
                {
                    results.Add(new FindMatch(
                        pageId, pageName, null, FindField.PageName, 0, m.Index, m.Length,
                        Context(pageName, m.Index - ContextChars, m.Index, leading: true),
                        m.Value,
                        Context(pageName, m.Index + m.Length, m.Index + m.Length + ContextChars, leading: false)));
                }
            }

            foreach (LabelObject obj in doc.Objects)
            {
                foreach ((FindField field, int part, string text) in Fields(obj, options.Targets))
                {
                    foreach (Match m in Matches(rx, text, options.SkipTokens))
                    {
                        results.Add(new FindMatch(
                            pageId, pageName, obj, field, part, m.Index, m.Length,
                            Context(text, m.Index - ContextChars, m.Index, leading: true),
                            m.Value,
                            Context(text, m.Index + m.Length, m.Index + m.Length + ContextChars, leading: false)));
                    }
                }
            }
        }

        return results;
    }

    // ---- replacing --------------------------------------------------------

    /// <summary>Commands that replace every match in scope, one per changed field.</summary>
    public static IReadOnlyList<IUndoableCommand> BuildReplaceAll(
        ProjectModel project, string? activePageId, FindOptions options, string replacement, out int occurrences) =>
        BuildReplaceAll(project, activePageId, options, replacement, skipLocked: false, out occurrences, out _, out _);

    /// <summary>
    /// As above, reporting how many objects changed. With <paramref name="skipLocked"/>,
    /// locked objects — the user's fixed template parts — are left untouched and
    /// counted in <paramref name="lockedSkipped"/> instead.
    /// </summary>
    public static IReadOnlyList<IUndoableCommand> BuildReplaceAll(
        ProjectModel project, string? activePageId, FindOptions options, string replacement, bool skipLocked,
        out int occurrences, out int objectsChanged, out int lockedSkipped)
    {
        occurrences = 0;
        objectsChanged = 0;
        lockedSkipped = 0;
        var commands = new List<IUndoableCommand>();
        if (Validate(options) is not null)
        {
            return commands;
        }

        Regex rx = BuildRegex(options);
        foreach ((_, _, LabelDocument doc) in Documents(project, activePageId, options.Scope))
        {
            foreach (LabelObject obj in doc.Objects)
            {
                int before = occurrences;
                IEnumerable<IUndoableCommand> forObject =
                    ReplaceInObject(obj, rx, options, replacement, onlyPart: null, onlyStart: null, ref occurrences);

                if (occurrences == before)
                {
                    continue;
                }

                if (skipLocked && obj.Locked)
                {
                    occurrences = before;
                    lockedSkipped++;
                    continue;
                }

                objectsChanged++;
                commands.AddRange(forObject);
            }
        }

        return commands;
    }

    /// <summary>
    /// A command replacing just <paramref name="match"/>, or null if the text changed
    /// underneath it since it was found (the caller should search again).
    /// </summary>
    public static IUndoableCommand? BuildReplaceOne(FindMatch match, FindOptions options, string replacement)
    {
        if (Validate(options) is not null || match.Object is null)
        {
            return null;
        }

        Regex rx = BuildRegex(options);
        int count = 0;
        List<IUndoableCommand> commands = ReplaceInObject(
            match.Object, rx, options, replacement, match.Part, match.Start, ref count, match.Field).ToList();

        return count == 1 && commands.Count == 1 ? commands[0] : null;
    }

    private static IEnumerable<IUndoableCommand> ReplaceInObject(
        LabelObject obj, Regex rx, FindOptions options, string replacement,
        int? onlyPart, int? onlyStart, ref int occurrences, FindField? onlyField = null)
    {
        var commands = new List<IUndoableCommand>();
        FindTargets t = options.Targets;
        bool Wants(FindField f, FindTargets flag) => (onlyField is null || onlyField == f) && t.HasFlag(flag);

        if (obj is TextObject text && Wants(FindField.Text, FindTargets.Text))
        {
            RichTextContent updated = text.Content.Clone();
            int changed = 0;
            for (int p = 0; p < updated.Paragraphs.Count; p++)
            {
                if (onlyPart is { } op && op != p)
                {
                    continue;
                }

                changed += ReplaceInParagraph(updated.Paragraphs[p], rx, options, replacement, onlyStart);
            }

            if (changed > 0)
            {
                occurrences += changed;
                commands.Add(new SetPropertyCommand<RichTextContent>(
                    "Replace text", () => text.Content, v => text.Content = v, updated));
            }
        }

        if (obj is BarcodeObject barcode && Wants(FindField.BarcodeData, FindTargets.BarcodeData))
        {
            string before = barcode.Spec.Data;
            string after = ReplaceInString(before, rx, options, replacement, onlyStart, out int changed);
            if (changed > 0)
            {
                occurrences += changed;
                commands.Add(new SetPropertyCommand<string>(
                    "Replace barcode value", () => barcode.Spec.Data, v => barcode.Spec.Data = v, after));
            }
        }

        if (obj is TableObject table && Wants(FindField.TableCell, FindTargets.TableCells))
        {
            var cells = new List<string>(table.Cells);
            int changed = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                if (onlyPart is { } op && op != i)
                {
                    continue;
                }

                cells[i] = ReplaceInString(cells[i], rx, options, replacement, onlyStart, out int n);
                changed += n;
            }

            if (changed > 0)
            {
                occurrences += changed;
                commands.Add(new SetPropertyCommand<List<string>>(
                    "Replace table text", () => table.Cells, v => table.Cells = v, cells));
            }
        }

        if (Wants(FindField.ObjectName, FindTargets.ObjectNames) && !string.IsNullOrEmpty(obj.Name))
        {
            string after = ReplaceInString(obj.Name, rx, options, replacement, onlyStart, out int changed);
            if (changed > 0)
            {
                occurrences += changed;
                commands.Add(new SetPropertyCommand<string>(
                    "Rename object", () => obj.Name, v => obj.Name = v, after));
            }
        }

        return commands;
    }

    /// <summary>
    /// The new names of the pages whose names match, for renaming them (page names are not
    /// objects, so the caller renames them through its own page commands). Page ids are
    /// null for the first page (the master label).
    /// </summary>
    public static IReadOnlyList<(string? PageId, string NewName)> PageRenames(
        ProjectModel project, string? activePageId, FindOptions options, string replacement, out int occurrences)
    {
        ArgumentNullException.ThrowIfNull(project);
        occurrences = 0;
        var renames = new List<(string?, string)>();
        if (Validate(options) is not null || !options.Targets.HasFlag(FindTargets.PageNames))
        {
            return renames;
        }

        Regex rx = BuildRegex(options);
        foreach ((string? pageId, string pageName, _) in Documents(project, activePageId, options.Scope))
        {
            string after = ReplaceInString(pageName, rx, options, replacement, onlyStart: null, out int changed);
            if (changed > 0 && after.Trim().Length > 0)
            {
                occurrences += changed;
                renames.Add((pageId, after.Trim()));
            }
        }

        return renames;
    }

    /// <summary>The page's name with just <paramref name="match"/> replaced, or null if the name changed since.</summary>
    public static string? RenamedPage(FindMatch match, FindOptions options, string replacement)
    {
        if (match.Field != FindField.PageName || Validate(options) is not null)
        {
            return null;
        }

        string after = ReplaceInString(match.PageName, BuildRegex(options), options, replacement, match.Start, out int count);
        return count == 1 && after.Trim().Length > 0 ? after.Trim() : null;
    }

    /// <summary>Replaces matches in a plain string; <paramref name="onlyStart"/> limits it to one occurrence.</summary>
    public static string ReplaceInString(
        string input, Regex rx, FindOptions options, string replacement, int? onlyStart, out int count)
    {
        count = 0;
        var sb = new StringBuilder();
        int last = 0;
        foreach (Match m in Matches(rx, input, options.SkipTokens))
        {
            if (onlyStart is { } s && m.Index != s)
            {
                continue;
            }

            sb.Append(input, last, m.Index - last).Append(Substitute(m, options, replacement));
            last = m.Index + m.Length;
            count++;
        }

        if (count == 0)
        {
            return input;
        }

        sb.Append(input, last, input.Length - last);
        return sb.ToString();
    }

    /// <summary>
    /// Replaces inside one paragraph while keeping run formatting. A match that
    /// sits in one run stays in that run; a match straddling runs takes the
    /// formatting of the run it starts in, and the covered characters are removed
    /// from the runs after it — the way word processors behave.
    /// </summary>
    public static int ReplaceInParagraph(Paragraph paragraph, Regex rx, FindOptions options, string replacement, int? onlyStart)
    {
        string plain = paragraph.PlainText;
        var matches = Matches(rx, plain, options.SkipTokens)
            .Where(m => onlyStart is null || m.Index == onlyStart)
            .ToList();

        // Work from the end so earlier offsets stay valid as text changes length.
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            Match m = matches[i];
            ReplaceRange(paragraph, m.Index, m.Length, Substitute(m, options, replacement));
        }

        return matches.Count;
    }

    private static void ReplaceRange(Paragraph paragraph, int start, int length, string replacement)
    {
        int offset = 0;
        bool inserted = false;
        int remaining = length;

        foreach (TextRun run in paragraph.Runs)
        {
            int runStart = offset;
            int runEnd = offset + run.Text.Length;
            offset = runEnd;

            if (!inserted)
            {
                // Matches are never empty, so the run holding the match's first
                // character is the one with runStart <= start < runEnd.
                if (run.Text.Length == 0 || start < runStart || start >= runEnd)
                {
                    continue;
                }

                int local = start - runStart;
                int take = Math.Min(remaining, run.Text.Length - local);
                run.Text = run.Text.Remove(local, take).Insert(local, replacement);
                remaining -= take;
                inserted = true;
                continue;
            }

            if (remaining <= 0)
            {
                break;
            }

            int cut = Math.Min(remaining, run.Text.Length);
            run.Text = run.Text[cut..];
            remaining -= cut;
        }

        if (!inserted)
        {
            // Empty paragraph or a match at the very end: append to the last run.
            if (paragraph.Runs.Count == 0)
            {
                paragraph.Runs.Add(new TextRun());
            }

            paragraph.Runs[^1].Text += replacement;
        }
    }

    private static string Substitute(Match m, FindOptions options, string replacement) =>
        options.UseRegex ? m.Result(replacement) : replacement;

    // ---- helpers ----------------------------------------------------------

    private static IEnumerable<Match> Matches(Regex rx, string text, bool skipTokens)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        List<(int Start, int End)>? tokens = skipTokens ? TokenRanges(text) : null;
        foreach (Match m in rx.Matches(text))
        {
            if (m.Length == 0)
            {
                continue; // "^" or "\b" alone would otherwise "match" everywhere
            }

            if (tokens is not null && tokens.Any(t => m.Index < t.End && m.Index + m.Length > t.Start))
            {
                continue;
            }

            yield return m;
        }
    }

    /// <summary>Character ranges of every <c>{…}</c> token, braces included.</summary>
    private static List<(int Start, int End)> TokenRanges(string text)
    {
        var ranges = new List<(int, int)>();
        int open = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                open = i;
            }
            else if (text[i] == '}' && open >= 0)
            {
                ranges.Add((open, i + 1));
                open = -1;
            }
        }

        return ranges;
    }

    private static IEnumerable<(string? PageId, string PageName, LabelDocument Document)> Documents(
        ProjectModel project, string? activePageId, FindScope scope)
    {
        string masterName = string.IsNullOrWhiteSpace(project.MasterLabel.Name) ? "Master label" : project.MasterLabel.Name;

        if (scope == FindScope.CurrentLabel)
        {
            if (activePageId is { } id && project.Pages.FirstOrDefault(p => p.Id == id) is { } page)
            {
                yield return (page.Id, page.Name, page.Document);
            }
            else
            {
                yield return (null, masterName, project.MasterLabel);
            }

            yield break;
        }

        yield return (null, masterName, project.MasterLabel);
        foreach (LabelPage page in project.Pages)
        {
            yield return (page.Id, page.Name, page.Document);
        }
    }

    private static IEnumerable<(FindField Field, int Part, string Text)> Fields(LabelObject obj, FindTargets targets)
    {
        if (obj is TextObject text && targets.HasFlag(FindTargets.Text))
        {
            for (int p = 0; p < text.Content.Paragraphs.Count; p++)
            {
                yield return (FindField.Text, p, text.Content.Paragraphs[p].PlainText);
            }
        }

        if (obj is BarcodeObject barcode && targets.HasFlag(FindTargets.BarcodeData))
        {
            yield return (FindField.BarcodeData, 0, barcode.Spec.Data);
        }

        if (obj is TableObject table && targets.HasFlag(FindTargets.TableCells))
        {
            for (int i = 0; i < table.Cells.Count; i++)
            {
                yield return (FindField.TableCell, i, table.Cells[i]);
            }
        }

        if (targets.HasFlag(FindTargets.ObjectNames) && !string.IsNullOrEmpty(obj.Name))
        {
            yield return (FindField.ObjectName, 0, obj.Name);
        }
    }

    private static string Context(string text, int from, int to, bool leading)
    {
        int a = Math.Clamp(from, 0, text.Length);
        int b = Math.Clamp(to, 0, text.Length);
        string slice = text[a..b];
        return leading
            ? (a > 0 ? "…" : string.Empty) + slice
            : slice + (b < text.Length ? "…" : string.Empty);
    }
}
