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
                   (block.InkHeightMm > maxHeight + 0.05 || Overflows(block, maxWidth)))
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
        double lastExtra = 0;
        double? capTop = null;
        double lastBaseline = 0;

        foreach (Paragraph paragraph in content.Paragraphs)
        {
            HorizontalAlignment align = paragraph.Alignment ?? style.HorizontalAlignment;
            // Arabic and Hebrew paragraphs run right to left: when the text box says so, or when
            // the paragraph's first letter is right-to-left.
            bool rtl = style.Direction == TextDirection.RightToLeft ||
                       Bidi.FirstStrong(paragraph.PlainText) == TextFlow.RightToLeft;
            // A paragraph value only counts when it really differs from single spacing: every
            // paragraph used to carry 1.0, which silently overrode the text box's setting.
            double lineSpacing = paragraph.LineSpacing is { } own && own > 0 && Math.Abs(own - 1.0) > 0.001
                ? own
                : style.LineSpacing;
            if (lineSpacing <= 0)
            {
                lineSpacing = 1.0;
            }

            List<WordPiece> pieces = Flatten(paragraph, style, scale, rtl)
                .Select(p => p with
                {
                    WidthMm = measurer.MeasureWidthMm(p.Text, p.Font, p.SizePt, p.Bold, p.Italic) + LetterSpacingWidth(p),
                    LineHeightMm = measurer.LineHeightMm(p.Font, p.SizePt),
                    BaselineMm = measurer.BaselineMm(p.Font, p.SizePt),
                    CapHeightMm = measurer.CapHeightMm(p.Font, p.SizePt),
                    TrailingSpaceMm = TrailingSpaceWidth(p, measurer),
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
                lastExtra = emptyHeight - (emptyHeight / lineSpacing);
                continue;
            }

            var spans = new List<TextSpan>();
            double width = 0;
            double height = 0;
            double baseline = 0;
            double cap = 0;
            double trailing = 0;

            void Flush(bool endOfParagraph)
            {
                if (spans.Count == 0)
                {
                    return;
                }

                // Justified lines spread the spare width over the gaps between words; the
                // paragraph's last line stays left-aligned, as in any word processor.
                int gaps = spans.Take(spans.Count - 1).Count(s => s.Text.EndsWith(' '));
                double lineWidth = Math.Max(0, width - trailing);
                bool justify = align == HorizontalAlignment.Justify && !endOfParagraph && gaps > 0 && lineWidth < maxWidth;

                // Extra line spacing goes below each line; the last line's is taken off the
                // block height again so bottom and middle alignment sit on the text, not on
                // empty space after it. Trailing spaces do not count towards the width, or
                // centred and right-aligned lines stop short of where they should.
                // A right-to-left line ends at its left edge; the space after its last word would
                // push the text right, so it goes (it never shows anyway).
                if (rtl && spans.Count > 0)
                {
                    spans[^1] = spans[^1] with { Text = spans[^1].Text.TrimEnd(' ') };
                }

                IReadOnlyList<TextSpan> logical = (justify ? spans.ToList() : Merge(spans)).Where(s => s.Text.Length > 0).ToList();

                double h = height * lineSpacing;
                double lineBaseline = baseline > 0 ? baseline : height * 0.8;
                capTop ??= totalHeight + lineBaseline - cap;
                lastBaseline = totalHeight + lineBaseline;
                lines.Add(new TextLine
                {
                    Spans = Bidi.VisualOrder(logical, s => s.Text, rtl),
                    WidthMm = justify ? maxWidth : lineWidth,
                    WordGapMm = justify ? (maxWidth - lineWidth) / gaps : 0,
                    HeightMm = h,
                    BaselineOffsetMm = lineBaseline,
                    CapHeightMm = cap,
                    Alignment = !justify && rtl && align == HorizontalAlignment.Justify ? HorizontalAlignment.Right : align,
                    RightToLeft = rtl,
                });
                totalHeight += h;
                lastExtra = h - height;
                spans.Clear();
                width = 0;
                height = 0;
                baseline = 0;
                cap = 0;
                trailing = 0;
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

                    Flush(endOfParagraph: true);
                    continue;
                }

                bool wrap = style.WordWrap && width > 0 && width + piece.WidthMm > maxWidth + 0.01;
                if (wrap)
                {
                    Flush(endOfParagraph: false);
                }

                spans.Add(piece.ToSpan());
                width += piece.WidthMm;
                height = Math.Max(height, piece.LineHeightMm);
                baseline = Math.Max(baseline, piece.BaselineMm);
                cap = Math.Max(cap, piece.CapHeightMm);
                // A run of spaces arrives as several one-space pieces; they all trail.
                trailing = piece.Text.Trim().Length == 0 ? trailing + piece.WidthMm : piece.TrailingSpaceMm;
            }

            Flush(endOfParagraph: true);
        }

        double total = Math.Max(0, totalHeight - lastExtra);
        return new TextBlock
        {
            Lines = lines,
            TotalHeightMm = total,
            CapTopMm = Math.Clamp(capTop ?? 0, 0, total),
            LastBaselineMm = lines.Count > 0 ? Math.Min(lastBaseline, total) : 0,
        };
    }

    private readonly record struct WordPiece(
        string Text, string Font, double SizePt, bool Bold, bool Italic, bool Underline, bool Strike,
        ColorRgba Fg, ColorRgba? Bg, double LetterSpacingEm, double WidthMm, double LineHeightMm,
        double BaselineMm = 0, double TrailingSpaceMm = 0, double CapHeightMm = 0, bool Rtl = false)
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
            RightToLeft = Rtl,
        };
    }

    private static List<WordPiece> Flatten(Paragraph paragraph, TextObject style, double scale, bool rtl)
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
                pieces.Add(new WordPiece(token, font, sizePt, bold, italic, underline, strike, fg, bg, spacing, 0, 0, Rtl: rtl));
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

    /// <summary>Width of the spaces a word ends with (the layouter keeps them attached to the word).</summary>
    private static double TrailingSpaceWidth(WordPiece p, ITextMeasurer measurer)
    {
        string trimmed = p.Text.TrimEnd(' ');
        int spaces = p.Text.Length - trimmed.Length;
        if (spaces == 0)
        {
            return 0;
        }

        double full = measurer.MeasureWidthMm(p.Text, p.Font, p.SizePt, p.Bold, p.Italic);
        double word = trimmed.Length == 0 ? 0 : measurer.MeasureWidthMm(trimmed, p.Font, p.SizePt, p.Bold, p.Italic);
        return Math.Max(0, full - word) + (spaces * p.LetterSpacingEm * UnitConverter.PointsToMm(p.SizePt));
    }

    // Arabic letters join up, so they are never spread apart (as in any word processor).
    private static double LetterSpacingWidth(WordPiece p) =>
        p.LetterSpacingEm == 0 || Bidi.NeedsShaping(p.Text) ? 0 : p.Text.Length * p.LetterSpacingEm * UnitConverter.PointsToMm(p.SizePt);
}
