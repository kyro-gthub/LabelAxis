using FluentAssertions;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Editing;

public sealed class SnapCalculatorTests
{
    [Fact]
    public void Snaps_left_edge_to_a_vertical_guide_within_tolerance()
    {
        var moving = new RectMm(9.4, 20, 30, 10);       // left edge at 9.4
        var result = SnapCalculator.SnapMove(moving, new[] { 10.0, 50.0 }, Array.Empty<double>(), toleranceMm: 1.0);

        result.Snapped.Should().BeTrue();
        result.Dx.Should().BeApproximately(0.6, 1e-9);   // 10.0 - 9.4
        result.Dy.Should().Be(0);
        result.Lines.Should().ContainSingle(l => l.Vertical && Math.Abs(l.PositionMm - 10.0) < 1e-9);
    }

    [Fact]
    public void Snaps_centre_to_the_label_centre()
    {
        // label 100 wide -> centre line at 50. Object 40 wide sitting at x=29.6 -> centre 49.6.
        var moving = new RectMm(29.6, 10, 40, 10);
        var result = SnapCalculator.SnapMove(moving, new[] { 0.0, 50.0, 100.0 }, Array.Empty<double>(), 1.0);

        result.Dx.Should().BeApproximately(0.4, 1e-9);   // centre 49.6 -> 50
    }

    [Fact]
    public void No_snap_when_outside_tolerance()
    {
        var moving = new RectMm(7.0, 20, 30, 10);
        var result = SnapCalculator.SnapMove(moving, new[] { 10.0 }, Array.Empty<double>(), toleranceMm: 1.0);

        result.Snapped.Should().BeFalse();
        result.Dx.Should().Be(0);
    }

    [Fact]
    public void Both_axes_can_snap_independently()
    {
        var moving = new RectMm(9.6, 19.7, 20, 20);
        var result = SnapCalculator.SnapMove(moving, new[] { 10.0 }, new[] { 20.0 }, 1.0);

        result.Dx.Should().BeApproximately(0.4, 1e-9);
        result.Dy.Should().BeApproximately(0.3, 1e-9);
        result.Lines.Should().HaveCount(2);
    }

    [Fact]
    public void SnapValue_pulls_a_resize_edge_onto_the_closest_target()
    {
        double v = SnapCalculator.SnapValue(40.3, new[] { 20.0, 40.0, 60.0 }, 1.0, out bool snapped);
        snapped.Should().BeTrue();
        v.Should().Be(40.0);

        _ = SnapCalculator.SnapValue(45.0, new[] { 20.0, 40.0 }, 1.0, out bool far);
        far.Should().BeFalse();
    }

    [Fact]
    public void Guide_edit_command_is_undoable()
    {
        var doc = new LabelDocument();
        var stack = new CommandStack();

        stack.Execute(new GuideEditCommand(doc.Guides, "Add guide", () => doc.Guides.VerticalMm.Add(12.5)));
        doc.Guides.VerticalMm.Should().Equal(12.5);

        stack.Execute(new GuideEditCommand(doc.Guides, "Add guide", () => doc.Guides.HorizontalMm.Add(30)));
        doc.Guides.HorizontalMm.Should().Equal(30);

        stack.Undo();
        doc.Guides.HorizontalMm.Should().BeEmpty();
        doc.Guides.VerticalMm.Should().Equal(12.5);

        stack.Undo();
        doc.Guides.VerticalMm.Should().BeEmpty();
    }

    [Fact]
    public void Guides_survive_a_document_clone()
    {
        var doc = new LabelDocument();
        doc.Guides.VerticalMm.Add(10);
        doc.Guides.HorizontalMm.Add(20);

        LabelDocument copy = doc.Clone();
        copy.Guides.VerticalMm.Should().Equal(10);
        copy.Guides.HorizontalMm.Should().Equal(20);

        copy.Guides.VerticalMm.Add(99);
        doc.Guides.VerticalMm.Should().Equal(10); // independent copy
    }
}
