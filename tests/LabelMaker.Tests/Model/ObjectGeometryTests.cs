using FluentAssertions;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;
using LabelMaker.Tests.Support;
using Xunit;

namespace LabelMaker.Tests.Model;

public sealed class ObjectGeometryTests
{
    private static ShapeObject Box(double rotation = 0, double skewX = 0, double skewY = 0) => new()
    {
        Bounds = new RectMm(10, 10, 20, 10),
        RotationDegrees = rotation,
        SkewXDegrees = skewX,
        SkewYDegrees = skewY,
    };

    [Fact]
    public void Unrotated_object_bounds_are_its_own_box()
    {
        ObjectGeometry.Bounds(Box()).Should().Be(new RectMm(10, 10, 20, 10));
    }

    [Fact]
    public void Rotating_90_degrees_swaps_the_extent_about_the_centre()
    {
        RectMm b = ObjectGeometry.Bounds(Box(rotation: 90));

        b.Width.Should().BeApproximately(10, 1e-9);
        b.Height.Should().BeApproximately(20, 1e-9);
        b.Center.X.Should().BeApproximately(20, 1e-9);
        b.Center.Y.Should().BeApproximately(15, 1e-9);
    }

    [Fact]
    public void Rotation_is_clockwise_on_screen()
    {
        // The local top-left corner (-10, -5) rotated 90° clockwise lands at (+5, -10) from the centre.
        PointMm[] corners = ObjectGeometry.Corners(Box(rotation: 90));

        corners[0].X.Should().BeApproximately(20 + 5, 1e-9);
        corners[0].Y.Should().BeApproximately(15 - 10, 1e-9);
    }

    [Fact]
    public void Horizontal_skew_shifts_bottom_edge_right_and_top_edge_left()
    {
        PointMm[] c = ObjectGeometry.Corners(Box(skewX: 45));

        // tan 45° = 1: half-height 5 shifts x by ±5.
        c[0].X.Should().BeApproximately(10 - 5, 1e-9);   // top-left
        c[3].X.Should().BeApproximately(10 + 5, 1e-9);   // bottom-left
        ObjectGeometry.Bounds(Box(skewX: 45)).Width.Should().BeApproximately(30, 1e-9);
    }

    [Fact]
    public void Local_and_world_round_trip()
    {
        ShapeObject obj = Box(rotation: 33, skewX: 12, skewY: -7);
        var local = new PointMm(3.5, -2.25);

        PointMm back = ObjectGeometry.ToLocal(obj, ObjectGeometry.ToWorld(obj, local));

        back.X.Should().BeApproximately(local.X, 1e-9);
        back.Y.Should().BeApproximately(local.Y, 1e-9);
    }

    [Fact]
    public void Contains_follows_the_rotated_shape_not_its_bounding_box()
    {
        ShapeObject obj = Box(rotation: 45);
        RectMm aabb = ObjectGeometry.Bounds(obj);

        // The AABB's corner is empty space for a diamond-rotated box.
        ObjectGeometry.Contains(obj, new PointMm(aabb.Left + 0.5, aabb.Top + 0.5)).Should().BeFalse();
        ObjectGeometry.Contains(obj, obj.Bounds.Center).Should().BeTrue();
    }

    [Fact]
    public void Intersects_detects_overlap_and_separation_for_rotated_objects()
    {
        ShapeObject obj = Box(rotation: 45);
        RectMm aabb = ObjectGeometry.Bounds(obj);

        ObjectGeometry.Intersects(obj, new RectMm(aabb.Left - 1, aabb.Top - 1, 1.6, 1.6)).Should().BeFalse();
        ObjectGeometry.Intersects(obj, new RectMm(obj.Bounds.Center.X - 1, obj.Bounds.Center.Y - 1, 2, 2)).Should().BeTrue();
        ObjectGeometry.Intersects(obj, new RectMm(0, 0, 100, 100)).Should().BeTrue();
    }

    [Fact]
    public void Skewed_object_is_rendered_through_a_matrix()
    {
        var doc = new LabelDocument { SizeMm = new SizeMm(60, 30) };
        doc.Objects.Add(new ShapeObject { Bounds = new RectMm(5, 5, 20, 10), SkewXDegrees = 20, StrokeWidthMm = 0.2 });
        var surface = new RecordingSurface();

        new LabelRenderer().Render(doc, new ResolveContext(), surface);

        surface.Ops.Should().Contain(op => op.StartsWith("matrix(", StringComparison.Ordinal));
    }

    [Fact]
    public void Clone_keeps_skew()
    {
        LabelObject copy = Box(skewX: 10, skewY: -4).Clone();

        copy.SkewXDegrees.Should().Be(10);
        copy.SkewYDegrees.Should().Be(-4);
    }
}
