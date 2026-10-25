using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;

namespace LabelMaker.Core.Editing;

/// <summary>The parts of a text box's look that can be copied to text boxes on other pages.</summary>
[Flags]
public enum TextStyleParts
{
    None = 0,
    Font = 1,
    Size = 2,

    /// <summary>Bold, italic, underline and strikethrough.</summary>
    Style = 4,

    /// <summary>Text colour and highlight.</summary>
    Colour = 8,

    /// <summary>Horizontal and vertical alignment.</summary>
    Alignment = 16,

    /// <summary>Line spacing and letter spacing.</summary>
    Spacing = 32,

    All = Font | Size | Style | Colour | Alignment | Spacing,
}

/// <summary>
/// Gives a text box the chosen parts of another's look (see <see cref="PageFormatCopy"/>, which
/// does this for "Apply formatting to other pages"). The words stay as they are.
/// </summary>
public static class TextStyleCopy
{
    internal static void Apply(TextObject source, TextObject target, TextStyleParts parts)
    {
        IEnumerable<TextRun> runs = target.Content.Paragraphs.SelectMany(p => p.Runs).ToList();
        if (parts.HasFlag(TextStyleParts.Font))
        {
            target.FontFamily = source.FontFamily;
            foreach (TextRun r in runs)
            {
                r.FontFamily = null;
            }
        }

        if (parts.HasFlag(TextStyleParts.Size))
        {
            target.FontSizePt = source.FontSizePt;
            target.MinAutoFitPt = source.MinAutoFitPt;
            foreach (TextRun r in runs)
            {
                r.FontSizePt = null;
            }
        }

        if (parts.HasFlag(TextStyleParts.Style))
        {
            target.Bold = source.Bold;
            target.Italic = source.Italic;
            target.Underline = source.Underline;
            target.Strikethrough = source.Strikethrough;
            foreach (TextRun r in runs)
            {
                r.Bold = r.Italic = r.Underline = r.Strikethrough = null;
            }
        }

        if (parts.HasFlag(TextStyleParts.Colour))
        {
            target.Foreground = source.Foreground;
            target.Background = source.Background;
            target.BackgroundTo = source.BackgroundTo;
            target.BackgroundAngleDegrees = source.BackgroundAngleDegrees;
            foreach (TextRun r in runs)
            {
                r.Foreground = r.Background = null;
            }
        }

        if (parts.HasFlag(TextStyleParts.Alignment))
        {
            target.HorizontalAlignment = source.HorizontalAlignment;
            target.VerticalAlignment = source.VerticalAlignment;
            foreach (Paragraph p in target.Content.Paragraphs)
            {
                p.Alignment = null;
            }
        }

        if (parts.HasFlag(TextStyleParts.Spacing))
        {
            target.LineSpacing = source.LineSpacing;
            target.LetterSpacingEm = source.LetterSpacingEm;
            foreach (Paragraph p in target.Content.Paragraphs)
            {
                p.LineSpacing = null;
            }

            foreach (TextRun r in runs)
            {
                r.LetterSpacingEm = null;
            }
        }
    }
}
