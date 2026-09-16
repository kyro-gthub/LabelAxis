using FluentAssertions;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using Xunit;
using VAlign = LabelMaker.Core.Model.Text.VerticalAlignment;

namespace LabelMaker.Tests.Rendering;

public sealed class TextBoxFitTests
{
    /// <summary>Every character 0.6 em wide, lines 1.2 em, baseline at 0.8 of the line, capitals 0.7 em.</summary>
    private sealed class Measurer : ITextMeasurer
    {
        public double MeasureWidthMm(string text, string fontFamily, double fontSizePt, bool bold, bool italic) =>
            text.Length * UnitConverter.PointsToMm(fontSizePt) * 0.6;

        public double LineHeightMm(string fontFamily, double fontSizePt) => UnitConverter.PointsToMm(fontSizePt) * 1.2;
    }

    private static readonly Measurer M = new();

    private static TextBlock Block(string text, double sizePt = 10) =>
        TextLayouter.Layout(RichTextContent.FromPlainText(text), new TextObject { FontSizePt = sizePt }, new RectMm(0, 0, 100, 100), M);

    [Fact]
    public void Top_alignment_puts_the_capitals_against_the_top_edge()
    {
        TextBlock block = Block("Hello");
        double em = UnitConverter.PointsToMm(10);

        block.CapTopMm.Should().BeApproximately((em * 1.2 * 0.8) - (em * 0.7), 1e-9);
        (block.TopOffsetMm(VAlign.Top, 20) + block.CapTopMm).Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void Bottom_alignment_puts_the_descenders_against_the_bottom_edge()
    {
        TextBlock block = Block("Hello");
        (block.TopOffsetMm(VAlign.Bottom, 20) + block.TotalHeightMm).Should().BeApproximately(20, 1e-9);
    }

    [Fact]
    public void Middle_alignment_centres_the_letters_between_capital_tops_and_the_last_baseline()
    {
        TextBlock block = Block("One\nTwo");
        double offset = block.TopOffsetMm(VAlign.Middle, 30);
        double capTop = offset + block.CapTopMm;
        double lastBaseline = offset + block.LastBaselineMm;

        capTop.Should().BeApproximately(30 - lastBaseline, 1e-9);
    }

    [Fact]
    public void Text_taller_than_its_box_starts_at_the_top_whatever_the_alignment()
    {
        TextBlock block = Block("A\nB\nC\nD", 20);
        block.TopOffsetMm(VAlign.Bottom, 5).Should().BeApproximately(-block.CapTopMm, 1e-9);
        block.TopOffsetMm(VAlign.Middle, 5).Should().BeApproximately(-block.CapTopMm, 1e-9);
    }

    [Fact]
    public void A_grow_to_fit_box_that_is_too_short_grows_in_the_same_undo_step()
    {
        var doc = new LabelDocument();
        var text = new TextObject
        {
            Content = RichTextContent.FromPlainText("Product name"),
            FontSizePt = 10,
            Bounds = new RectMm(2, 2, 19, 3),
        };
        doc.Objects.Add(text);
        text.AutoFit.Should().Be(TextAutoFit.ResizeBox, "new text boxes grow with their text");

        var stack = new CommandStack();
        var bigger = new SetPropertyCommand<double>("Size", () => text.FontSizePt, v => text.FontSizePt = v, 12);
        stack.Push(TextBoxFit.ExecuteWithFit(bigger, doc, M));

        double needed = TextBoxFit.RequiredHeightMm(text, M);
        text.Bounds.Height.Should().BeApproximately(needed, 0.01);
        text.Bounds.Height.Should().BeGreaterThan(3);
        text.Bounds.Y.Should().Be(2);

        stack.Undo();
        text.FontSizePt.Should().Be(10);
        text.Bounds.Should().Be(new RectMm(2, 2, 19, 3));

        stack.Redo();
        text.Bounds.Height.Should().BeApproximately(needed, 0.01);
    }

    [Fact]
    public void Fixed_size_boxes_and_boxes_that_already_fit_are_left_alone()
    {
        var doc = new LabelDocument();
        var spill = new TextObject { AutoFit = TextAutoFit.None, Content = RichTextContent.FromPlainText("Long text here"), Bounds = new RectMm(0, 0, 10, 2) };
        var roomy = new TextObject { Content = RichTextContent.FromPlainText("Hi"), Bounds = new RectMm(0, 10, 40, 20) };
        doc.Objects.Add(spill);
        doc.Objects.Add(roomy);

        TextBoxFit.Grow(doc, M).Should().BeEmpty();
        spill.Bounds.Height.Should().Be(2);
        roomy.Bounds.Height.Should().Be(20);
    }
}
