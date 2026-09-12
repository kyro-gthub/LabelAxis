namespace LabelMaker.Core.Model.Text;

/// <summary>
/// Applies a plain-text edit to rich text while keeping its formatting.
///
/// The properties panel edits text as a plain string. Rebuilding the content from
/// that string (<see cref="RichTextContent.FromPlainText"/>) threw away every bold
/// word and colour change on each keystroke. Instead this finds the part of the
/// text that actually changed — the span between the longest common prefix and
/// suffix — and splices only that. Untouched characters keep their run; typed
/// characters take the formatting of the character they were typed after.
/// </summary>
public static class RichTextEditing
{
    /// <summary>Returns new content whose plain text is <paramref name="newText"/>.</summary>
    public static RichTextContent ApplyPlainText(RichTextContent original, string newText)
    {
        ArgumentNullException.ThrowIfNull(original);
        newText = (newText ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

        List<StyledChar> chars = Flatten(original, out List<Paragraph> paragraphs);
        string oldText = new(chars.Select(c => c.Char).ToArray());
        if (oldText == newText)
        {
            return original.Clone();
        }

        int prefix = 0;
        int max = Math.Min(oldText.Length, newText.Length);
        while (prefix < max && oldText[prefix] == newText[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < max - prefix &&
               oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix])
        {
            suffix++;
        }

        // What typed characters look like: the character before the caret, unless
        // that is a paragraph break (then the one after), so typing at the start
        // of a bold word stays bold.
        StyledChar template =
            prefix > 0 && chars[prefix - 1].Char != '\n' ? chars[prefix - 1]
            : prefix < chars.Count ? chars[prefix]
            : chars.Count > 0 ? chars[^1]
            : new StyledChar(' ', new TextRun(), paragraphs.Count > 0 ? paragraphs[0] : new Paragraph());

        var result = new List<StyledChar>(newText.Length);
        result.AddRange(chars.Take(prefix));
        foreach (char c in newText.AsSpan(prefix, newText.Length - prefix - suffix))
        {
            result.Add(template with { Char = c });
        }

        result.AddRange(chars.Skip(chars.Count - suffix));
        return Rebuild(result, template);
    }

    /// <summary>
    /// One character with the run it came from (used only as a formatting template)
    /// and the paragraph it belongs to. A '\n' belongs to the paragraph it ends.
    /// </summary>
    private readonly record struct StyledChar(char Char, TextRun Style, Paragraph Paragraph);

    private static List<StyledChar> Flatten(RichTextContent content, out List<Paragraph> paragraphs)
    {
        paragraphs = content.Paragraphs.Count > 0 ? content.Paragraphs : new List<Paragraph> { new() };
        var chars = new List<StyledChar>();

        for (int p = 0; p < paragraphs.Count; p++)
        {
            Paragraph paragraph = paragraphs[p];
            TextRun lastStyle = paragraph.Runs.LastOrDefault() ?? new TextRun();

            foreach (TextRun run in paragraph.Runs)
            {
                foreach (char c in run.Text)
                {
                    chars.Add(new StyledChar(c, run, paragraph));
                }
            }

            if (p < paragraphs.Count - 1)
            {
                chars.Add(new StyledChar('\n', lastStyle, paragraph));
            }
        }

        return chars;
    }

    private static RichTextContent Rebuild(List<StyledChar> chars, StyledChar fallback)
    {
        var content = new RichTextContent();
        Paragraph? current = null;
        TextRun? currentRun = null;
        StyledChar? paragraphSource = null;

        void StartParagraph(StyledChar source)
        {
            current = CloneParagraphProperties(source.Paragraph);
            currentRun = null;
            paragraphSource = source;
            content.Paragraphs.Add(current);
        }

        foreach (StyledChar sc in chars)
        {
            if (current is null)
            {
                StartParagraph(sc);
            }

            if (sc.Char == '\n')
            {
                // Keep an empty paragraph's formatting so the next typed character inherits it.
                if (current!.Runs.Count == 0)
                {
                    current.Runs.Add(CloneStyle(sc.Style));
                }

                current = null;
                continue;
            }

            if (currentRun is null || !SameStyle(currentRun, sc.Style))
            {
                currentRun = CloneStyle(sc.Style);
                current!.Runs.Add(currentRun);
            }

            currentRun.Text += sc.Char;
        }

        if (current is null)
        {
            // Text is empty, or ends with a paragraph break: there is one more (empty) paragraph.
            StyledChar source = chars.Count > 0 ? chars[^1] : fallback;
            StartParagraph(source);
        }

        if (current!.Runs.Count == 0)
        {
            current.Runs.Add(CloneStyle((paragraphSource ?? fallback).Style));
        }

        return content;
    }

    private static Paragraph CloneParagraphProperties(Paragraph p) => new()
    {
        Alignment = p.Alignment,
        LineSpacing = p.LineSpacing,
        SpaceBeforePt = p.SpaceBeforePt,
        SpaceAfterPt = p.SpaceAfterPt,
    };

    private static TextRun CloneStyle(TextRun r)
    {
        TextRun copy = r.Clone();
        copy.Text = string.Empty;
        return copy;
    }

    private static bool SameStyle(TextRun a, TextRun b) =>
        a.FontFamily == b.FontFamily &&
        a.FontSizePt == b.FontSizePt &&
        a.Bold == b.Bold &&
        a.Italic == b.Italic &&
        a.Underline == b.Underline &&
        a.Strikethrough == b.Strikethrough &&
        a.Foreground == b.Foreground &&
        a.Background == b.Background &&
        a.LetterSpacingEm == b.LetterSpacingEm;
}
