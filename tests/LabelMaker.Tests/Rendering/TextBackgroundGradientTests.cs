using FluentAssertions;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;
using LabelMaker.Tests.Support;
using Xunit;

namespace LabelMaker.Tests.Rendering;

public sealed class TextBackgroundGradientTests
{
    private static TextObject Box() => new()
    {
        Bounds = new RectMm(2, 2, 40, 10),
        Content = RichTextContent.FromPlainText("Sale"),
        Background = new ColorRgba(0x2C, 0x6F, 0xE8),
        BackgroundTo = new ColorRgba(0x6F, 0xD3, 0xF7),
        BackgroundAngleDegrees = 45,
    };

    private static RecordingSurface Render(TextObject text)
    {
        var doc = new LabelDocument { SizeMm = new SizeMm(50, 20) };
        doc.Objects.Add(text);
        var surface = new RecordingSurface();
        new LabelRenderer().Render(doc, new ResolveContext(), surface);
        return surface;
    }

    [Fact]
    public void A_text_box_background_can_be_a_gradient()
    {
        RecordingSurface surface = Render(Box());
        surface.Ops.Should().Contain("gradientRect(45)").And.Contain("text");
        surface.Ops.IndexOf("gradientRect(45)").Should().BeLessThan(surface.Ops.IndexOf("text"), "the background goes under the words");
    }

    [Fact]
    public void A_plain_background_and_no_background_draw_as_before()
    {
        TextObject plain = Box();
        plain.BackgroundTo = null;
        Render(plain).Ops.Should().Contain("fillRect").And.NotContain(o => o.StartsWith("gradient", StringComparison.Ordinal));

        TextObject clear = Box();
        clear.Background = ColorRgba.Transparent;
        clear.BackgroundTo = ColorRgba.Transparent;
        Render(clear).Ops.Should().NotContain(o => o.StartsWith("gradient", StringComparison.Ordinal) || o == "fillRect");
    }

    [Fact]
    public void The_gradient_is_kept_through_copying_saving_and_copying_the_text_style()
    {
        TextObject box = Box();
        var copy = (TextObject)box.Clone();
        copy.BackgroundTo.Should().Be(box.BackgroundTo);
        copy.BackgroundAngleDegrees.Should().Be(45);

        string json = System.Text.Json.JsonSerializer.Serialize<LabelObject>(box, ProjectJson.Default);
        var back = (TextObject)System.Text.Json.JsonSerializer.Deserialize<LabelObject>(json, ProjectJson.Default)!;
        back.BackgroundTo.Should().Be(box.BackgroundTo);
        back.BackgroundAngleDegrees.Should().Be(45);

        var target = new TextObject { Bounds = new RectMm(0, 0, 20, 8) };
        IUndoableCommand command = TextStyleCopy.Build(box, new[] { target }, TextStyleParts.Colour, measurer: null)!;
        command.Do();
        target.BackgroundTo.Should().Be(box.BackgroundTo);
        target.BackgroundAngleDegrees.Should().Be(45);
        command.Undo();
        target.BackgroundTo.Should().BeNull();
        target.BackgroundAngleDegrees.Should().Be(90);
    }
}
