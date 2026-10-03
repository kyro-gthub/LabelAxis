using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;

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
/// Copies one text box's formatting onto text boxes on other pages: either the boxes with the
/// same name (a page duplicated from another keeps its objects' names) or every text box.
/// The words in each box stay as they are; only the look changes.
/// </summary>
public static class TextStyleCopy
{
    /// <summary>
    /// The text boxes on the given pages (Pages panel positions, 0 = the master label) that
    /// would take <paramref name="source"/>'s formatting. The source itself is never one.
    /// </summary>
    public static IReadOnlyList<TextObject> Targets(ProjectModel project, TextObject source, IEnumerable<int> pagePositions, bool sameNameOnly)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pagePositions);

        var wanted = new HashSet<int>(pagePositions);
        return PrintablePages.All(project)
            .Where((_, i) => wanted.Contains(i))
            .SelectMany(d => d.Objects.OfType<TextObject>())
            .Where(t => !ReferenceEquals(t, source) && !t.Locked)
            .Where(t => !sameNameOnly || SameName(t, source))
            .ToList();
    }

    /// <summary>
    /// One undo step that gives every target <paramref name="source"/>'s chosen
    /// <paramref name="parts"/>, or null when that would change nothing. Per-word formatting
    /// of those parts is cleared so the new look shows on every word, and grow-to-fit boxes
    /// are made tall enough for their text when <paramref name="measurer"/> is given.
    /// </summary>
    public static IUndoableCommand? Build(TextObject source, IEnumerable<TextObject> targets, TextStyleParts parts, ITextMeasurer? measurer)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targets);

        var changes = new List<(TextObject Target, TextStyleState Before, TextStyleState After)>();
        foreach (TextObject target in targets.Distinct())
        {
            TextStyleState before = TextStyleState.Capture(target);
            Apply(source, target, parts);
            if (measurer is not null && target.AutoFit == TextAutoFit.ResizeBox)
            {
                double needed = Math.Round(TextBoxFit.RequiredHeightMm(target, measurer), 2);
                if (needed > target.Bounds.Height + 0.01)
                {
                    target.Bounds = target.Bounds with { Height = needed };
                }
            }

            TextStyleState after = TextStyleState.Capture(target);
            before.Restore(target);
            if (!after.SameAs(before))
            {
                changes.Add((target, before, after));
            }
        }

        return changes.Count == 0 ? null : new CopyTextStyleCommand(changes);
    }

    private static bool SameName(TextObject a, TextObject b) =>
        !string.IsNullOrWhiteSpace(a.Name) && string.Equals(a.Name.Trim(), b.Name.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void Apply(TextObject source, TextObject target, TextStyleParts parts)
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

    /// <summary>Everything about a text box this copy can change, so undo puts it all back.</summary>
    private sealed record TextStyleState(
        string FontFamily, double FontSizePt, double MinAutoFitPt, bool Bold, bool Italic, bool Underline, bool Strikethrough,
        ColorRgba Foreground, ColorRgba Background, HorizontalAlignment HorizontalAlignment, VerticalAlignment VerticalAlignment,
        double LineSpacing, double LetterSpacingEm, RichTextContent Content, RectMm Bounds)
    {
        public static TextStyleState Capture(TextObject t) => new(
            t.FontFamily, t.FontSizePt, t.MinAutoFitPt, t.Bold, t.Italic, t.Underline, t.Strikethrough,
            t.Foreground, t.Background, t.HorizontalAlignment, t.VerticalAlignment,
            t.LineSpacing, t.LetterSpacingEm, t.Content.Clone(), t.Bounds);

        public void Restore(TextObject t)
        {
            t.FontFamily = FontFamily;
            t.FontSizePt = FontSizePt;
            t.MinAutoFitPt = MinAutoFitPt;
            t.Bold = Bold;
            t.Italic = Italic;
            t.Underline = Underline;
            t.Strikethrough = Strikethrough;
            t.Foreground = Foreground;
            t.Background = Background;
            t.HorizontalAlignment = HorizontalAlignment;
            t.VerticalAlignment = VerticalAlignment;
            t.LineSpacing = LineSpacing;
            t.LetterSpacingEm = LetterSpacingEm;
            t.Content = Content.Clone();
            t.Bounds = Bounds;
        }

        public bool SameAs(TextStyleState other) =>
            this with { Content = other.Content } == other && SameContent(Content, other.Content);

        private static bool SameContent(RichTextContent a, RichTextContent b) =>
            a.Paragraphs.Count == b.Paragraphs.Count && a.Paragraphs.Zip(b.Paragraphs).All(p =>
                p.First.Alignment == p.Second.Alignment && p.First.LineSpacing == p.Second.LineSpacing &&
                p.First.Runs.Count == p.Second.Runs.Count && p.First.Runs.Zip(p.Second.Runs).All(r =>
                    r.First.Text == r.Second.Text && r.First.FontFamily == r.Second.FontFamily &&
                    r.First.FontSizePt == r.Second.FontSizePt && r.First.Bold == r.Second.Bold &&
                    r.First.Italic == r.Second.Italic && r.First.Underline == r.Second.Underline &&
                    r.First.Strikethrough == r.Second.Strikethrough && r.First.Foreground == r.Second.Foreground &&
                    r.First.Background == r.Second.Background && r.First.LetterSpacingEm == r.Second.LetterSpacingEm));
    }

    private sealed class CopyTextStyleCommand : IUndoableCommand
    {
        private readonly List<(TextObject Target, TextStyleState Before, TextStyleState After)> _changes;

        public CopyTextStyleCommand(List<(TextObject, TextStyleState, TextStyleState)> changes) => _changes = changes;

        public string Label => _changes.Count == 1 ? "Apply formatting to 1 text box" : $"Apply formatting to {_changes.Count} text boxes";

        public void Do()
        {
            foreach ((TextObject target, _, TextStyleState after) in _changes)
            {
                after.Restore(target);
            }
        }

        public void Undo()
        {
            for (int i = _changes.Count - 1; i >= 0; i--)
            {
                _changes[i].Before.Restore(_changes[i].Target);
            }
        }
    }
}
