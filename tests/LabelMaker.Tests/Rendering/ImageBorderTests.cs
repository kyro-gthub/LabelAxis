using FluentAssertions;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;
using LabelMaker.Tests.Support;
using Xunit;

namespace LabelMaker.Tests.Rendering;

public sealed class ImageBorderTests
{
    // The renderer draws each object about its centre, so a 20 × 10 box is (-10, -5) to (10, 5).
    private static RecordingSurface Render(ImageObject image)
    {
        var doc = new LabelDocument { SizeMm = new SizeMm(60, 30) };
        image.AssetId = "asset";
        image.Bounds = new RectMm(5, 5, 20, 10);
        doc.Objects.Add(image);

        var surface = new RecordingSurface();
        surface.UseStubImage(200, 100);
        new LabelRenderer().Render(doc, new ResolveContext(), surface);
        return surface;
    }

    [Fact]
    public void No_border_by_default()
    {
        Render(new ImageObject()).Strokes.Should().BeEmpty();
    }

    [Fact]
    public void A_single_border_sits_just_inside_the_box_on_top_of_the_picture()
    {
        RecordingSurface s = Render(new ImageObject { BorderWidthMm = 1, BorderDash = DashStyle.Dash });

        s.Ops.Should().ContainInOrder("image", "strokeRect");
        s.Strokes.Should().ContainSingle();
        (string kind, RectMm rect, double width, DashStyle dash, _) = s.Strokes[0];
        kind.Should().Be("rect");
        width.Should().Be(1);
        dash.Should().Be(DashStyle.Dash);
        // Centre line half the thickness in, so the outer edge of the line meets the box edge.
        rect.X.Should().BeApproximately(-9.5, 1e-9);
        rect.Y.Should().BeApproximately(-4.5, 1e-9);
        rect.Width.Should().BeApproximately(19, 1e-9);
        rect.Height.Should().BeApproximately(9, 1e-9);
    }

    [Fact]
    public void A_double_border_is_two_lines_a_third_as_thick_with_a_third_open_between()
    {
        RecordingSurface s = Render(new ImageObject { BorderWidthMm = 1.5, BorderLines = ImageBorderLines.Double });

        s.Strokes.Should().HaveCount(2);
        s.Strokes.Should().OnlyContain(st => Math.Abs(st.Width - 0.5) < 1e-9);
        s.Strokes[0].Rect.X.Should().BeApproximately(-10 + 0.25, 1e-9);
        s.Strokes[1].Rect.X.Should().BeApproximately(-10 + 1.25, 1e-9);
    }

    [Fact]
    public void The_border_follows_a_circle_or_rounded_crop()
    {
        Render(new ImageObject { BorderWidthMm = 1, ClipShape = ImageClipShape.Ellipse }).Strokes.Single().Kind.Should().Be("ellipse");

        var rounded = Render(new ImageObject { BorderWidthMm = 1, ClipShape = ImageClipShape.RoundedRectangle, ClipCornerRadiusMm = 3 });
        rounded.Strokes.Single().Radius.Should().BeApproximately(2.5, 1e-9, "the radius shrinks by the inset so the line hugs the clip");
    }

    [Fact]
    public void A_border_survives_copy_and_a_transparent_colour_draws_nothing()
    {
        var image = new ImageObject { BorderWidthMm = 2, BorderColor = new ColorRgba(200, 0, 0), BorderDash = DashStyle.Dot, BorderLines = ImageBorderLines.Double };
        var copy = (ImageObject)image.Clone();
        copy.BorderWidthMm.Should().Be(2);
        copy.BorderColor.Should().Be(new ColorRgba(200, 0, 0));
        copy.BorderDash.Should().Be(DashStyle.Dot);
        copy.BorderLines.Should().Be(ImageBorderLines.Double);

        Render(new ImageObject { BorderWidthMm = 1, BorderColor = ColorRgba.Transparent }).Strokes.Should().BeEmpty();
    }
}
