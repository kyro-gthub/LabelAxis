using FluentAssertions;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Editing;

public sealed class TextStyleCopyTests
{
    /// <summary>Every character 0.6 em wide, lines 1.2 em.</summary>
    private sealed class Measurer : ITextMeasurer
    {
        public double MeasureWidthMm(string text, string fontFamily, double fontSizePt, bool bold, bool italic) =>
            text.Length * UnitConverter.PointsToMm(fontSizePt) * 0.6;

        public double LineHeightMm(string fontFamily, double fontSizePt) => UnitConverter.PointsToMm(fontSizePt) * 1.2;
    }

    private static TextObject Text(string name, string text = "Price") => new()
    {
        Name = name,
        Content = RichTextContent.FromPlainText(text),
        Bounds = new RectMm(0, 0, 60, 6),
    };

    private static (ProjectModel Project, TextObject Source, TextObject OtherTitle, TextObject OtherPrice) Project()
    {
        var p = new ProjectModel();
        TextObject source = Text("Title");
        source.FontFamily = "Arial";
        source.FontSizePt = 14;
        source.Bold = true;
        source.Foreground = new ColorRgba(192, 0, 0);
        source.HorizontalAlignment = HorizontalAlignment.Center;
        p.MasterLabel.Objects.Add(source);

        TextObject otherTitle = Text("Title", "Other title");
        TextObject otherPrice = Text("Price");
        p.Pages.Add(new LabelPage { Name = "Page 1", Document = new LabelDocument { Objects = { otherTitle, otherPrice } } });
        return (p, source, otherTitle, otherPrice);
    }

    [Fact]
    public void Same_name_targets_only_the_matching_box_on_the_ticked_pages()
    {
        (ProjectModel p, TextObject source, TextObject otherTitle, _) = Project();

        TextStyleCopy.Targets(p, source, new[] { 0, 1 }, sameNameOnly: true).Should().Equal(otherTitle);
        TextStyleCopy.Targets(p, source, new[] { 0 }, sameNameOnly: true).Should().BeEmpty("the source itself is never a target");
    }

    [Fact]
    public void Every_text_box_targets_all_of_them_except_the_source_and_locked_ones()
    {
        (ProjectModel p, TextObject source, TextObject otherTitle, TextObject otherPrice) = Project();
        otherPrice.Locked = true;

        TextStyleCopy.Targets(p, source, new[] { 0, 1 }, sameNameOnly: false).Should().Equal(otherTitle);
    }

    [Fact]
    public void Chosen_parts_are_copied_the_words_stay_and_undo_puts_everything_back()
    {
        (_, TextObject source, TextObject target, _) = Project();

        IUndoableCommand? command = TextStyleCopy.Build(source, new[] { target },
            TextStyleParts.Font | TextStyleParts.Size | TextStyleParts.Style | TextStyleParts.Colour, measurer: null);
        command.Should().NotBeNull();

        target.FontFamily.Should().Be("Segoe UI", "nothing changes until the command runs");
        command!.Do();
        target.FontFamily.Should().Be("Arial");
        target.FontSizePt.Should().Be(14);
        target.Bold.Should().BeTrue();
        target.Foreground.Should().Be(source.Foreground);
        target.HorizontalAlignment.Should().Be(HorizontalAlignment.Left, "alignment was not ticked");
        target.Content.PlainText.Should().Be("Other title");

        command.Undo();
        target.FontFamily.Should().Be("Segoe UI");
        target.FontSizePt.Should().Be(10);
        target.Bold.Should().BeFalse();
        target.Foreground.Should().Be(ColorRgba.Black);
    }

    [Fact]
    public void Per_word_formatting_of_a_copied_part_is_cleared_so_the_new_look_shows()
    {
        (_, TextObject source, TextObject target, _) = Project();
        target.Content.Paragraphs[0].Runs[0].FontSizePt = 30;
        target.Content.Paragraphs[0].Runs[0].Italic = true;

        TextStyleCopy.Build(source, new[] { target }, TextStyleParts.Size, measurer: null)!.Do();

        target.Content.Paragraphs[0].Runs[0].FontSizePt.Should().BeNull();
        target.Content.Paragraphs[0].Runs[0].Italic.Should().BeTrue("style was not copied");
    }

    [Fact]
    public void A_grow_to_fit_box_grows_when_the_bigger_text_no_longer_fits()
    {
        (_, TextObject source, TextObject target, _) = Project();
        source.FontSizePt = 40;
        double before = target.Bounds.Height;

        IUndoableCommand command = TextStyleCopy.Build(source, new[] { target }, TextStyleParts.Size, new Measurer())!;
        command.Do();
        target.Bounds.Height.Should().BeGreaterThan(before);

        command.Undo();
        target.Bounds.Height.Should().Be(before);
    }

    [Fact]
    public void Nothing_to_change_gives_no_command()
    {
        (_, TextObject source, TextObject target, _) = Project();
        target.FontFamily = source.FontFamily;

        TextStyleCopy.Build(source, new[] { target }, TextStyleParts.Font, measurer: null).Should().BeNull();
    }
}
