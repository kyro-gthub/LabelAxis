using FluentAssertions;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Text;
using Xunit;

namespace LabelMaker.Tests.Model;

public sealed class RichTextEditingTests
{
    private static RichTextContent HelloBoldWorld() => new()
    {
        Paragraphs =
        {
            new Paragraph
            {
                Alignment = HorizontalAlignment.Center,
                Runs =
                {
                    new TextRun { Text = "Hello " },
                    new TextRun { Text = "World", Bold = true, Foreground = new ColorRgba(200, 0, 0) },
                },
            },
        },
    };

    [Fact]
    public void Editing_plain_text_keeps_formatting_of_untouched_words()
    {
        RichTextContent edited = RichTextEditing.ApplyPlainText(HelloBoldWorld(), "Hi World");

        Paragraph p = edited.Paragraphs.Single();
        p.PlainText.Should().Be("Hi World");
        p.Runs.Last().Text.Should().Be("World");
        p.Runs.Last().Bold.Should().BeTrue();
        p.Runs.Last().Foreground.Should().Be(new ColorRgba(200, 0, 0));
        p.Alignment.Should().Be(HorizontalAlignment.Center);
    }

    [Fact]
    public void Typed_characters_take_the_formatting_of_the_character_before_them()
    {
        RichTextContent edited = RichTextEditing.ApplyPlainText(HelloBoldWorld(), "Hello Worlds");

        TextRun bold = edited.Paragraphs.Single().Runs.Single(r => r.Bold == true);
        bold.Text.Should().Be("Worlds");
    }

    [Fact]
    public void Typing_at_the_start_of_a_paragraph_uses_the_first_character_style()
    {
        var content = new RichTextContent
        {
            Paragraphs = { new Paragraph { Runs = { new TextRun { Text = "Sale", Italic = true } } } },
        };

        RichTextContent edited = RichTextEditing.ApplyPlainText(content, "Big Sale");

        edited.Paragraphs.Single().Runs.Should().ContainSingle()
            .Which.Should().Match<TextRun>(r => r.Text == "Big Sale" && r.Italic == true);
    }

    [Fact]
    public void New_lines_split_paragraphs_and_keep_paragraph_properties()
    {
        RichTextContent edited = RichTextEditing.ApplyPlainText(HelloBoldWorld(), "Hello\nWorld");

        edited.Paragraphs.Should().HaveCount(2);
        edited.PlainText.Should().Be("Hello\nWorld");
        edited.Paragraphs.Should().OnlyContain(p => p.Alignment == HorizontalAlignment.Center);
        edited.Paragraphs[1].Runs.Single().Bold.Should().BeTrue();
    }

    [Fact]
    public void Deleting_a_paragraph_break_merges_paragraphs()
    {
        RichTextContent content = RichTextContent.FromPlainText("one\ntwo");

        RichTextContent edited = RichTextEditing.ApplyPlainText(content, "onetwo");

        edited.Paragraphs.Should().ContainSingle();
        edited.PlainText.Should().Be("onetwo");
    }

    [Fact]
    public void Clearing_everything_leaves_one_empty_paragraph_with_the_style()
    {
        RichTextContent edited = RichTextEditing.ApplyPlainText(HelloBoldWorld(), string.Empty);

        edited.Paragraphs.Should().ContainSingle();
        edited.PlainText.Should().BeEmpty();
        edited.Paragraphs[0].Runs.Should().NotBeEmpty();
    }

    [Fact]
    public void Unchanged_text_returns_an_equal_copy()
    {
        RichTextContent original = HelloBoldWorld();

        RichTextContent edited = RichTextEditing.ApplyPlainText(original, "Hello World");

        edited.Should().NotBeSameAs(original);
        edited.PlainText.Should().Be("Hello World");
        edited.Paragraphs[0].Runs[1].Bold.Should().BeTrue();
    }
}
