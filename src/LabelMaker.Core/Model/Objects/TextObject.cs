using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Variables;

namespace LabelMaker.Core.Model.Objects;

/// <summary>
/// A text box. Content stays editable text at all times. Runs may contain
/// <c>{Expression}</c> tokens; a box whose entire content is one token is still
/// a <see cref="TextObject"/> — <see cref="IsPureField"/> just reports that shape
/// so the UI can badge it and search can index it as a field.
/// </summary>
public sealed class TextObject : LabelObject
{
    public override ObjectKind Kind => ObjectKind.Text;

    public RichTextContent Content { get; set; } = RichTextContent.FromPlainText("Text");

    // Object-level defaults (a run overrides per-span).
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSizePt { get; set; } = 10.0;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strikethrough { get; set; }
    public ColorRgba Foreground { get; set; } = ColorRgba.Black;
    public ColorRgba Background { get; set; } = ColorRgba.Transparent;

    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Top;
    public TextDirection Direction { get; set; } = TextDirection.LeftToRight;

    public bool WordWrap { get; set; } = true;
    public TextAutoFit AutoFit { get; set; } = TextAutoFit.None;

    /// <summary>Padding inside the box, millimetres, in T/R/B/L order.</summary>
    public Thickness PaddingMm { get; set; } = new(0.5, 0.5, 0.5, 0.5);

    public double LineSpacing { get; set; } = 1.0;
    public double LetterSpacingEm { get; set; }

    /// <summary>Minimum point size used by <see cref="TextAutoFit.ShrinkToFit"/>.</summary>
    public double MinAutoFitPt { get; set; } = 4.0;

    public bool IsPureField
    {
        get
        {
            string t = Content.PlainText.Trim();
            return t.StartsWith('{') && t.EndsWith('}') && t.IndexOf('{', 1) < 0;
        }
    }

    public override IEnumerable<string> GetSearchableStrings()
    {
        yield return Content.PlainText;
    }

    public override IEnumerable<string> GetReferencedFields() =>
        TokenScanner.ExtractFieldNames(Content.PlainText);

    public override LabelObject Clone()
    {
        var copy = new TextObject
        {
            Content = Content.Clone(),
            FontFamily = FontFamily,
            FontSizePt = FontSizePt,
            Bold = Bold,
            Italic = Italic,
            Underline = Underline,
            Strikethrough = Strikethrough,
            Foreground = Foreground,
            Background = Background,
            HorizontalAlignment = HorizontalAlignment,
            VerticalAlignment = VerticalAlignment,
            Direction = Direction,
            WordWrap = WordWrap,
            AutoFit = AutoFit,
            PaddingMm = PaddingMm,
            LineSpacing = LineSpacing,
            LetterSpacingEm = LetterSpacingEm,
            MinAutoFitPt = MinAutoFitPt,
        };
        CopyBaseTo(copy);
        return copy;
    }
}

/// <summary>Millimetre insets, top / right / bottom / left.</summary>
public readonly record struct Thickness(double Top, double Right, double Bottom, double Left)
{
    public static readonly Thickness Zero = new(0, 0, 0, 0);

    public Thickness(double uniform) : this(uniform, uniform, uniform, uniform)
    {
    }

    public double Horizontal => Left + Right;
    public double Vertical => Top + Bottom;
}
