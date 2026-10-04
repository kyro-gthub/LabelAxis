namespace LabelMaker.Core.Rendering;

/// <summary>Which way a piece of text runs.</summary>
public enum TextFlow
{
    /// <summary>Only digits, spaces and punctuation: it takes the direction of what is around it.</summary>
    Neutral = 0,
    LeftToRight = 1,
    RightToLeft = 2,
}

/// <summary>
/// Just enough of the Unicode bidirectional rules for label text: Arabic and Hebrew paragraphs
/// run right to left, words keep their own direction, and a run of English inside Arabic
/// (or Arabic inside English) stays in its own reading order.
/// </summary>
public static class Bidi
{
    /// <summary>Right-to-left embedding and its end: wraps a span drawn in a right-to-left paragraph.</summary>
    public const char RightToLeftEmbedding = '‫';
    public const char PopDirectionalFormatting = '‬';

    public static bool IsRightToLeft(char c) =>
        c is >= '֐' and <= 'ࣿ'      // Hebrew, Arabic, Syriac, Thaana, NKo, Samaritan, Mandaic, Arabic Extended
          or >= 'יִ' and <= '﷿'       // Hebrew and Arabic presentation forms A
          or >= 'ﹰ' and <= '﻿';      // Arabic presentation forms B

    /// <summary>Arabic-script letters change shape and join their neighbours, so they must be drawn as whole words.</summary>
    public static bool NeedsShaping(string text) =>
        text.Any(c => c is >= '؀' and <= 'ۿ' or >= '܀' and <= 'ࣿ' or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿');

    /// <summary>True when the text holds any right-to-left letter (and so cannot be drawn as plain left-to-right glyphs).</summary>
    public static bool HasRightToLeft(string text) => text.Any(IsRightToLeft);

    /// <summary>The direction of the first letter that has one (digits and punctuation have none).</summary>
    public static TextFlow FirstStrong(string text)
    {
        foreach (char c in text)
        {
            if (IsRightToLeft(c))
            {
                return TextFlow.RightToLeft;
            }

            if (char.IsLetter(c))
            {
                return TextFlow.LeftToRight;
            }
        }

        return TextFlow.Neutral;
    }

    /// <summary>
    /// The pieces of one line in the order they appear from left to right. In a right-to-left
    /// line the order is reversed, except that a run of left-to-right pieces keeps its own
    /// order; in a left-to-right line only runs of right-to-left pieces are reversed. Digits and
    /// punctuation go with the piece before them.
    /// </summary>
    public static IReadOnlyList<T> VisualOrder<T>(IReadOnlyList<T> logical, Func<T, string> text, bool rightToLeft)
    {
        ArgumentNullException.ThrowIfNull(logical);
        ArgumentNullException.ThrowIfNull(text);
        if (logical.Count < 2)
        {
            return logical;
        }

        TextFlow baseFlow = rightToLeft ? TextFlow.RightToLeft : TextFlow.LeftToRight;
        TextFlow previous = baseFlow;
        var flows = new TextFlow[logical.Count];
        for (int i = 0; i < logical.Count; i++)
        {
            TextFlow f = FirstStrong(text(logical[i]));
            flows[i] = f == TextFlow.Neutral ? previous : f;
            previous = flows[i];
        }

        var result = new List<T>(logical.Count);
        if (!rightToLeft)
        {
            // Left-to-right line: reverse each run of right-to-left pieces in place.
            int i = 0;
            while (i < logical.Count)
            {
                int j = i;
                while (j < logical.Count && flows[j] == flows[i])
                {
                    j++;
                }

                IEnumerable<T> run = logical.Skip(i).Take(j - i);
                result.AddRange(flows[i] == TextFlow.RightToLeft ? run.Reverse() : run);
                i = j;
            }

            return result;
        }

        // Right-to-left line: the runs go right to left, and a left-to-right run keeps its order.
        var runs = new List<List<T>>();
        for (int i = 0; i < logical.Count;)
        {
            int j = i;
            while (j < logical.Count && flows[j] == flows[i])
            {
                j++;
            }

            List<T> run = logical.Skip(i).Take(j - i).ToList();
            if (flows[i] == TextFlow.RightToLeft)
            {
                run.Reverse();
            }

            runs.Add(run);
            i = j;
        }

        runs.Reverse();
        foreach (List<T> run in runs)
        {
            result.AddRange(run);
        }

        return result;
    }

    /// <summary>
    /// The characters of <paramref name="text"/> in the order they appear on the page, left to
    /// right: Arabic and Hebrew runs reversed (with brackets mirrored), English words and numbers
    /// kept in their own order, and in a right-to-left paragraph the runs themselves going right
    /// to left. PDF viewers expect right-to-left text stored like this and turn it back into
    /// reading order when it is selected, copied or searched.
    /// </summary>
    public static string VisualString(string text, bool rightToLeft)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || (!rightToLeft && !HasRightToLeft(text)))
        {
            return text;
        }

        // Each character's direction; spaces and punctuation take their neighbours' when both
        // agree, otherwise the paragraph's. Digits read left to right, as numbers do in Arabic.
        var flows = new TextFlow[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            flows[i] = IsRightToLeft(c) ? TextFlow.RightToLeft : char.IsLetterOrDigit(c) ? TextFlow.LeftToRight : TextFlow.Neutral;
        }

        TextFlow baseFlow = rightToLeft ? TextFlow.RightToLeft : TextFlow.LeftToRight;
        for (int i = 0; i < text.Length; i++)
        {
            if (flows[i] != TextFlow.Neutral)
            {
                continue;
            }

            int j = i;
            while (j < text.Length && flows[j] == TextFlow.Neutral)
            {
                j++;
            }

            TextFlow before = i > 0 ? flows[i - 1] : baseFlow;
            TextFlow after = j < text.Length ? flows[j] : baseFlow;
            TextFlow resolved = before == after ? before : baseFlow;
            for (int k = i; k < j; k++)
            {
                flows[k] = resolved;
            }

            i = j - 1;
        }

        var runs = new List<string>();
        for (int i = 0; i < text.Length;)
        {
            int j = i;
            while (j < text.Length && flows[j] == flows[i])
            {
                j++;
            }

            string run = text[i..j];
            runs.Add(flows[i] == TextFlow.RightToLeft ? new string(run.Reverse().Select(Mirror).ToArray()) : run);
            i = j;
        }

        if (rightToLeft)
        {
            runs.Reverse();
        }

        return string.Concat(runs);
    }

    private static char Mirror(char c) => c switch
    {
        '(' => ')',
        ')' => '(',
        '[' => ']',
        ']' => '[',
        '{' => '}',
        '}' => '{',
        '<' => '>',
        '>' => '<',
        '«' => '»',
        '»' => '«',
        _ => c,
    };

    /// <summary>
    /// The text to hand to a text engine that applies the Unicode bidi rules itself (WPF, SVG
    /// viewers): in a right-to-left paragraph it is wrapped in a right-to-left embedding so
    /// numbers and punctuation sit where an Arabic reader expects them.
    /// </summary>
    public static string ForDisplay(TextSpan span)
    {
        ArgumentNullException.ThrowIfNull(span);
        return span.RightToLeft && span.Text.Length > 0
            ? RightToLeftEmbedding + span.Text + PopDirectionalFormatting
            : span.Text;
    }
}

/// <summary>
/// Gives the outline of a piece of text, shaped and ordered by a full text engine. Surfaces whose
/// own text drawing cannot join Arabic letters (PDF) fill these outlines instead.
/// </summary>
public interface ITextOutliner
{
    /// <summary>
    /// Closed outlines of <paramref name="span"/> drawn with its left end at x = 0 and its baseline
    /// at y = 0, in millimetres (y grows downwards). Filled with the non-zero winding rule.
    /// </summary>
    IReadOnlyList<IReadOnlyList<Units.PointMm>> Outline(TextSpan span);

    /// <summary>Width of the shaped text, which for joined Arabic differs from adding up single letters.</summary>
    double MeasureWidthMm(string text, string fontFamily, double fontSizePt, bool bold, bool italic);
}
