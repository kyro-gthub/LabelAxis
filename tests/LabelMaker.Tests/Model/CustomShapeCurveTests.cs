using FluentAssertions;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Model;

public sealed class CustomShapeCurveTests
{
    private static readonly PointMm[] Square = { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };

    [Fact]
    public void Without_curved_dots_the_outline_is_just_the_dots()
    {
        CustomShape.Outline(Square, curved: null, closed: true).Should().Equal(Square);
        CustomShape.Outline(Square, new[] { false, false, false, false }, closed: false).Should().Equal(Square);
    }

    [Fact]
    public void A_curved_dot_is_passed_through_smoothly_and_its_neighbours_stay_corners()
    {
        List<PointMm> outline = CustomShape.Outline(Square, new[] { false, true, false, false }, closed: true);

        outline.Should().Contain(new PointMm(10, 0), "the outline still runs through the curved dot");
        outline.Should().Contain(Square[0]).And.Contain(Square[2]).And.Contain(Square[3]);
        outline.Count.Should().Be(4 + ((CustomShape.CurveSteps - 1) * 2), "the two lines meeting at the curved dot are drawn as curves");

        // Smooth at the curved dot: the outline bulges outside the square's corner on both sides of it.
        int at = outline.IndexOf(new PointMm(10, 0));
        outline[at - 1].Y.Should().BeLessThan(0, "coming in, the curve swings above the top edge");
        outline[at + 1].X.Should().BeGreaterThan(10, "going on, it swings outside the right edge");
    }

    [Fact]
    public void A_closed_outline_does_not_repeat_its_first_dot()
    {
        List<PointMm> outline = CustomShape.Outline(Square, new[] { true, true, true, true }, closed: true);
        outline[0].Should().Be(Square[0]);
        outline[^1].Should().NotBe(Square[0]);
        outline.Should().HaveCount(4 * CustomShape.CurveSteps);
    }

    [Fact]
    public void Curved_dots_are_kept_through_copying_and_saving()
    {
        ShapeObject shape = CustomShape.Create(Square);
        shape.CurvedPoints = new List<bool> { true, false, true, false };

        ((ShapeObject)shape.Clone()).CurvedPoints.Should().Equal(true, false, true, false);
        string json = System.Text.Json.JsonSerializer.Serialize<LabelObject>(shape, LabelMaker.Core.Serialization.ProjectJson.Default);
        var back = (ShapeObject)System.Text.Json.JsonSerializer.Deserialize<LabelObject>(json, LabelMaker.Core.Serialization.ProjectJson.Default)!;
        back.CurvedPoints.Should().Equal(true, false, true, false);
        CustomShape.CurvedFlags(new[] { true }, 3).Should().Equal(true, false, false);
    }
}
