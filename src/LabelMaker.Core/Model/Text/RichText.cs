namespace LabelMaker.Core.Model.Text;

public enum HorizontalAlignment
{
    Left = 0,
    Center = 1,
    Right = 2,
    Justify = 3,
}

public enum VerticalAlignment
{
    Top = 0,
    Middle = 1,
    Bottom = 2,
}

public enum TextDirection
{
    LeftToRight = 0,
    RightToLeft = 1,
}

public enum TextAutoFit
{
    /// <summary>Text keeps its point size; overflow is clipped (and flagged by preflight).</summary>
    None = 0,

    /// <summary>Shrink the font until the text fits the box (never grows past the authored size).</summary>
    ShrinkToFit = 1,

    /// <summary>Grow the box height to fit the text (width fixed).</summary>
    ResizeBox = 2,
}

/// <summary>
/// A contiguous span of characters sharing formatting. Any of the nullable
/// members left null inherits from the owning <see cref="TextObject"/> default.
/// Run text may contain <c>{Expression}</c> tokens resolved at render time.
/// </summary>
public sealed class TextRun
{
    public string Text { get; set; } = string.Empty;
    public string? FontFamily { get; set; }
    public double? FontSizePt { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Underline { get; set; }
    public bool? Strikethrough { get; set; }
    public ColorRgba? Foreground { get; set; }
    public ColorRgba? Background { get; set; }

    /// <summary>Extra tracking in per-mille of an em (0 = normal).</summary>
    public double? LetterSpacingEm { get; set; }

    public TextRun Clone() => (TextRun)MemberwiseClone();
}

public sealed class Paragraph
{
    public List<TextRun> Runs { get; set; } = new();
    public HorizontalAlignment? Alignment { get; set; }

    /// <summary>
    /// Line height multiplier for this paragraph only; null (or 1.0, which older files
    /// stored on every paragraph) uses the text box's own line spacing.
    /// </summary>
    public double? LineSpacing { get; set; }

    public double SpaceBeforePt { get; set; }
    public double SpaceAfterPt { get; set; }

    public string PlainText => string.Concat(Runs.Select(r => r.Text));

    public Paragraph Clone() => new()
    {
        Alignment = Alignment,
        LineSpacing = LineSpacing,
        SpaceBeforePt = SpaceBeforePt,
        SpaceAfterPt = SpaceAfterPt,
        Runs = Runs.Select(r => r.Clone()).ToList(),
    };
}

/// <summary>A minimal block-of-paragraphs rich text model. Not a flow document.</summary>
public sealed class RichTextContent
{
    public List<Paragraph> Paragraphs { get; set; } = new();

    public string PlainText => string.Join('\n', Paragraphs.Select(p => p.PlainText));

    public bool IsEmpty => Paragraphs.Count == 0 || Paragraphs.All(p => p.PlainText.Length == 0);

    public static RichTextContent FromPlainText(string text)
    {
        var content = new RichTextContent();
        foreach (string line in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            content.Paragraphs.Add(new Paragraph { Runs = { new TextRun { Text = line } } });
        }

        if (content.Paragraphs.Count == 0)
        {
            content.Paragraphs.Add(new Paragraph());
        }

        return content;
    }

    public RichTextContent Clone() => new()
    {
        Paragraphs = Paragraphs.Select(p => p.Clone()).ToList(),
    };
}
