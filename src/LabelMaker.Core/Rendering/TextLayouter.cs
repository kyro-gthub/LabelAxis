using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Units;

namespace LabelMaker.Core.Rendering;

/// <summary>
/// Turns resolved rich text + a <see cref="TextObject"/>'s style into a laid-out
/// <see cref="TextBlock"/>: word wrap, line spacing, letter spacing and
/// shrink-to-fit. Uses an <see cref="ITextMeasurer"/> so the same code runs
/// headless in tests and against real WPF metrics in the app.
/// </summary>
public static class TextLayouter
{
    public static TextBlock Layout(
        RichTextContent content, TextObject style, RectMm contentRectMm, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(measurer);

        double maxWidth = Math.Max(1.0, contentRectMm.Width);
        double maxHeight = Math.Max(1.0, contentRectMm.Height);

        double scale = 1.0;
        double minScale = style.AutoFit == TextAutoFit.ShrinkToFit && style.FontSizePt > 0
            ? Math.Max(0.05, style.MinAutoFitPt / style.FontSizePt)
            : 1.0;

        TextBlock block = Build(content, style, maxWidth, measurer, scale);

        if (style.AutoFit == TextAutoFit.ShrinkToFit)
        {
            int guard = 0;
            while (guard++ < 60 && scale > minScale &&
                   (block.TotalHeightMm > maxHeight + 0.05 || Overflows(block, maxWidth)))
            {
                scale = Math.Max(minScale, scale - 0.04);
                block = Build(content, style, maxWidth, measurer, scale);
            }
        }

        return block;
    }

    private static bool Overflows(TextBlock block, double maxWidth) =>
        block.Lines.Any(l => l.WidthMm > maxWidth + 0.1);

    private static TextBlock Build(
        RichTextContent content, TextObject style, double maxWidth, ITextMeasurer measurer, double scale)
    {
        var lines = new List<TextLine>();
        double totalHeight = 0;

        foreach (Paragraph paragraph in content.Paragraphs)
        {
            HorizontalAlignment align = paragraph.Alignment ?? style.HorizontalAlignment;
            // A paragraph value only counts when it really differs from single spacing: every
            // paragraph used to carry 1.0, which silently overrode the text box's setting.
            double lineSpacing = paragraph.LineSpacing is { } own && own > 0 && Math.Abs(own - 1.0) > 0.001
                ? own
                : style.LineSpacing;
            if (lineSpacing <= 0)
            {
                lineSpacing = 1.0;
            }

            List<WordPiece> pieces = Flatten(paragraph, style, scale)
                .Select(p => p with
                {
                    WidthMm = measurer.MeasureWidthMm(p.Text, p.Font, p.SizePt, p.Bold, p.Italic) + LetterSpacingWidth(p),
                    LineHeightMm = measurer.LineHeightMm(p.Font, p.SizePt),
                })
                .ToList();

            if (pieces.Count == 0)
            {
                double emptyHeight = measurer.LineHeightMm(style.FontFamily, style.FontSizePt * scale) * lineSpacing;
                lines.Add(new TextLine
                {
                    Spans = Array.Empty<TextSpan>(),
                    WidthMm = 0,
                    HeightMm = emptyHeight,
                    Alignment = align,
                });
                totalHeight += emptyHeight;
                continue;
            }

            var spans = new List<TextSpan>();
            double width = 0;
            double height = 0;

            void Flush()
            {
                if (spans.Count == 0)
                {
                    return;
                }

                double h = height * lineSpacing;
                lines.Add(new TextLine
                {
                    Spans = Merge(spans),
                    WidthMm = width,
                    HeightMm = h,
                    BaselineOffsetMm = height * 0.8,
                    Alignment = align,
                });
                totalHeight += h;
                spans.Clear();
                width = 0;
                height = 0;
            }

            foreach (WordPiece piece in pieces)
            {
                bool hardBreak = piece.Text == "\n";
                if (hardBreak)
                {
                    if (spans.Count == 0)
                    {
                        height = piece.LineHeightMm;
                    }

                    Flush();
                    continue;
                }

                bool wrap = style.WordWrap && width > 0 && width + piece.WidthMm > maxWidth + 0.01;
                if (wrap)
                {
                    Flush();
                }

                spans.Add(piece.ToSpan());
                width += piece.WidthMm;
                height = Math.Max(height, piece.LineHeightMm);
            }

            Flush();
        }

        return new TextBlock { Lines = lines, TotalHeightMm = totalHeight };
    }

    private readonly record struct WordPiece(
        string Text, string Font, double SizePt, bool Bold, bool Italic, bool Underline, bool Strike,
        ColorRgba Fg, ColorRgba? Bg, double LetterSpacingEm, double WidthMm, double LineHeightMm)
    {
        public TextSpan ToSpan() => new()
        {
            Text = Text,
            FontFamily = Font,
            FontSizePt = SizePt,
            Bold = Bold,
            Italic = Italic,
            Underline = Underline,
            Strikethrough = Strike,
            Foreground = Fg,
            Background = Bg,
            LetterSpacingEm = LetterSpacingEm,
        };
    }

    private static List<WordPiece> Flatten(Paragraph paragraph, TextObject style, double scale)
    {
        var pieces = new List<WordPiece>();
        foreach (TextRun run in paragraph.Runs)
        {
            if (run.Text.Length == 0)
            {
                continue;
            }

            string font = run.FontFamily ?? style.FontFamily;
            double sizePt = (run.FontSizePt ?? style.FontSizePt) * scale;
            bool bold = run.Bold ?? style.Bold;
            bool italic = run.Italic ?? style.Italic;
            bool underline = run.Underline ?? style.Underline;
            bool strike = run.Strikethrough ?? style.Strikethrough;
            ColorRgba fg = run.Foreground ?? style.Foreground;
            ColorRgba? bg = run.Background ?? (style.Background.IsTransparent ? null : style.Background);
            double spacing = run.LetterSpacingEm ?? style.LetterSpacingEm;

            foreach (string token in Tokenize(run.Text))
            {
                pieces.Add(new WordPiece(token, font, sizePt, bold, italic, underline, strike, fg, bg, spacing, 0, 0));
            }
        }

        return pieces;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                if (i > start)
                {
                    yield return text[start..i];
                }

                yield return "\n";
                start = i + 1;
            }
            else if (c == ' ')
            {
                yield return text.Substring(start, i - start + 1);
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }

    private static IReadOnlyList<TextSpan> Merge(List<TextSpan> spans)
    {
        var merged = new List<TextSpan>();
        foreach (TextSpan span in spans)
        {
            if (merged.Count > 0 && SameStyle(merged[^1], span))
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + span.Text };
            }
            else
            {
                merged.Add(span);
            }
        }

        return merged;
    }

    private static bool SameStyle(TextSpan a, TextSpan b) =>
        a.FontFamily == b.FontFamily && Math.Abs(a.FontSizePt - b.FontSizePt) < 0.01 &&
        a.Bold == b.Bold && a.Italic == b.Italic && a.Underline == b.Underline &&
        a.Strikethrough == b.Strikethrough && a.Foreground == b.Foreground &&
        Nullable.Equals(a.Background, b.Background) &&
        Math.Abs(a.LetterSpacingEm - b.LetterSpacingEm) < 0.001;

    private static double LetterSpacingWidth(WordPiece p) =>
        p.LetterSpacingEm == 0 ? 0 : p.Text.Length * p.LetterSpacingEm * UnitConverter.PointsToMm(p.SizePt);
}
