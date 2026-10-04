using FluentAssertions;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using LabelMaker.Tests.Support;
using Xunit;

namespace LabelMaker.Tests.Rendering;

public sealed class BidiTests
{
    private const string Price = "السعر";      // "the price"
    private const string Riyal = "ريال";
    private const string Apples = "تفاح";

    [Fact]
    public void Arabic_and_hebrew_letters_are_right_to_left_and_digits_have_no_direction()
    {
        Bidi.FirstStrong(Price).Should().Be(TextFlow.RightToLeft);
        Bidi.FirstStrong("שלום").Should().Be(TextFlow.RightToLeft);
        Bidi.FirstStrong("Price").Should().Be(TextFlow.LeftToRight);
        Bidi.FirstStrong("25.00 ").Should().Be(TextFlow.Neutral);
        Bidi.FirstStrong("25 " + Riyal).Should().Be(TextFlow.RightToLeft);

        Bidi.NeedsShaping(Apples).Should().BeTrue();
        Bidi.NeedsShaping("שלום").Should().BeFalse("Hebrew letters do not join");
        Bidi.HasRightToLeft("שלום").Should().BeTrue();
    }

    [Fact]
    public void A_right_to_left_line_reads_from_the_right_but_keeps_english_in_its_own_order()
    {
        // Logical: [arabic1] [Model] [X200] [arabic2]
        string[] logical = { "a1", "Model", "X200", "a2" };
        static string Text(string s) => s.StartsWith('a') ? Apples : s;

        Bidi.VisualOrder(logical, Text, rightToLeft: true).Should().Equal("a2", "Model", "X200", "a1");
        Bidi.VisualOrder(logical, Text, rightToLeft: false).Should().Equal("a1", "Model", "X200", "a2");

        string[] arabicOnly = { "a1", "a2", "a3" };
        // An Arabic run inside an English line still reads right to left.
        Bidi.VisualOrder(arabicOnly, Text, rightToLeft: false).Should().Equal("a3", "a2", "a1");
    }

    [Fact]
    public void A_number_after_an_arabic_word_stays_with_it()
    {
        string[] logical = { "a1", "25", "a2" };
        static string Text(string s) => s.StartsWith('a') ? Riyal : s;

        Bidi.VisualOrder(logical, Text, rightToLeft: true).Should().Equal("a2", "25", "a1");
    }

    [Fact]
    public void Display_text_is_wrapped_in_a_right_to_left_embedding_only_in_right_to_left_paragraphs()
    {
        var rtl = new TextSpan { Text = Price, FontFamily = "Tajawal", FontSizePt = 12, RightToLeft = true };
        Bidi.ForDisplay(rtl).Should().Be("‫" + Price + "‬");
        Bidi.ForDisplay(rtl with { RightToLeft = false }).Should().Be(Price);
    }

    private static TextBlock Layout(string text, TextObject? style = null)
    {
        style ??= new TextObject { FontSizePt = 10 };
        return TextLayouter.Layout(RichTextContent.FromPlainText(text), style, new RectMm(0, 0, 100, 50), new RecordingSurface().TextMeasurer);
    }

    [Fact]
    public void An_arabic_paragraph_is_laid_out_right_to_left_on_its_own()
    {
        TextBlock block = Layout(Price + " 25 " + Riyal);

        block.Lines.Should().ContainSingle();
        TextLine line = block.Lines[0];
        line.RightToLeft.Should().BeTrue();
        line.Spans.Should().OnlyContain(s => s.RightToLeft);
        line.Spans.Last().Text.Should().NotEndWith(" ", "the space after the last word would push right-to-left text out of place");
    }

    [Fact]
    public void English_stays_left_to_right_unless_the_box_says_right_to_left()
    {
        Layout("Fresh apples").Lines[0].RightToLeft.Should().BeFalse();
        Layout("Fresh apples", new TextObject { FontSizePt = 10, Direction = TextDirection.RightToLeft }).Lines[0].RightToLeft.Should().BeTrue();
    }

    [Fact]
    public void Words_of_different_styles_in_an_arabic_line_come_out_in_reading_order()
    {
        var content = new RichTextContent();
        content.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new TextRun { Text = Price + " ", Bold = true },
                new TextRun { Text = Apples },
            },
        });

        TextBlock block = TextLayouter.Layout(content, new TextObject { FontSizePt = 10 }, new RectMm(0, 0, 100, 50), new RecordingSurface().TextMeasurer);

        // Drawn left to right: the second word first, the bold first word at the right.
        block.Lines[0].Spans.Select(s => s.Text.Trim()).Should().Equal(Apples, Price);
        block.Lines[0].Spans[1].Bold.Should().BeTrue();
    }

    [Fact]
    public void Arabic_is_never_letter_spaced_because_its_letters_join()
    {
        double plain = Layout(Apples).Lines[0].WidthMm;
        double spaced = Layout(Apples, new TextObject { FontSizePt = 10, LetterSpacingEm = 0.2 }).Lines[0].WidthMm;
        spaced.Should().Be(plain);

        double latinPlain = Layout("Apple").Lines[0].WidthMm;
        Layout("Apple", new TextObject { FontSizePt = 10, LetterSpacingEm = 0.2 }).Lines[0].WidthMm.Should().BeGreaterThan(latinPlain);
    }
}
