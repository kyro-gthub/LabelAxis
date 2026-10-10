using System.Globalization;
using System.Xml.Linq;
using FluentAssertions;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;
using LabelMaker.Export.Svg;
using Xunit;

namespace LabelMaker.Tests.Rendering;

/// <summary>Where the line through struck-through text runs, the same on screen, on paper, in PDF and SVG.</summary>
public sealed class StrikeLineTests
{
    [Theory]
    [InlineData("$23.99")]
    [InlineData("SALE")]
    [InlineData("ABC-0042")]
    [InlineData("12,50 €")]
    public void Digits_and_capitals_are_crossed_through_their_middle(string text) =>
        StrikeLine.HeightEm(text).Should().Be(0.35);

    [Theory]
    [InlineData("Was $23.99")]
    [InlineData("old price")]
    [InlineData("زيت زيتون")]
    public void Words_with_small_letters_are_crossed_through_the_middle_of_the_small_letters(string text) =>
        StrikeLine.HeightEm(text).Should().Be(0.27);

    [Fact]
    public void The_svg_line_runs_where_the_screen_and_pdf_put_it()
    {
        var label = new LabelDocument { SizeMm = new SizeMm(60, 20) };
        label.Objects.Add(new TextObject
        {
            Bounds = new RectMm(2, 2, 56, 10),
            Content = RichTextContent.FromPlainText("$23.99"),
            FontSizePt = 14,
            Strikethrough = true,
            Foreground = new ColorRgba(0x5E, 0x5E, 0x5E),
        });

        string svg = new SvgExporter().ExportLabel(label, new ResolveContext());

        svg.Should().NotContain("line-through", "the browser would choose its own height");
        XNamespace ns = "http://www.w3.org/2000/svg";
        XDocument doc = XDocument.Parse(svg);
        XElement text = doc.Descendants(ns + "text").Single(t => t.Value == "$23.99");
        XElement line = doc.Descendants(ns + "line").Single();
        double baseline = double.Parse(text.Attribute("y")!.Value, CultureInfo.InvariantCulture);
        double y = double.Parse(line.Attribute("y1")!.Value, CultureInfo.InvariantCulture);
        double em = UnitConverter.PointsToMm(14);

        (baseline - y).Should().BeApproximately(em * 0.35, 0.01);
        line.Attribute("y2")!.Value.Should().Be(line.Attribute("y1")!.Value, "the line is straight");
        double.Parse(line.Attribute("stroke-width")!.Value, CultureInfo.InvariantCulture).Should().BeApproximately(em * StrikeLine.ThicknessEm, 0.01);
        line.Attribute("stroke")!.Value.Should().Be("#5E5E5E");
    }
}
