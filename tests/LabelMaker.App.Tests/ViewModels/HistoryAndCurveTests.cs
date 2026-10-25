using FluentAssertions;
using LabelMaker.App.ViewModels;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using Xunit;

namespace LabelMaker.App.Tests.ViewModels;

/// <summary>The History tab (every undo step, to go straight to one) and the Lines menu's curve.</summary>
public sealed class HistoryAndCurveTests
{
    [Fact]
    public void The_history_lists_each_step_and_choosing_one_goes_back_or_forward_to_it() => AppHarness.Run(h =>
    {
        h.Session.History.Clear();
        int before = h.Session.ActiveLabel.Objects.Count;
        h.ViewModel.AddShapeCommand.Execute("line");
        h.ViewModel.AddShapeCommand.Execute("ellipse");
        h.ViewModel.AddShapeCommand.Execute("curve");

        h.ViewModel.HistorySteps.Should().HaveCount(4, "the project as opened, then three steps");
        h.ViewModel.HistorySteps[0].DoneCount.Should().Be(0);
        h.ViewModel.HistorySteps[^1].IsCurrent.Should().BeTrue();

        h.ViewModel.GoToHistoryStep(h.ViewModel.HistorySteps[1]);

        h.Session.ActiveLabel.Objects.Should().HaveCount(before + 1, "only the first step is still done");
        h.ViewModel.HistorySteps.Should().HaveCount(4, "undone steps stay listed to redo");
        h.ViewModel.HistorySteps[1].IsCurrent.Should().BeTrue();
        h.ViewModel.HistorySteps.Skip(2).Should().OnlyContain(s => s.IsUndone);

        h.ViewModel.GoToHistoryStep(h.ViewModel.HistorySteps[3]);
        h.Session.ActiveLabel.Objects.Should().HaveCount(before + 3);

        h.ViewModel.GoToHistoryStep(h.ViewModel.HistorySteps[0]);
        h.Session.ActiveLabel.Objects.Should().HaveCount(before);
        h.ViewModel.HistorySteps[0].IsCurrent.Should().BeTrue();
    });

    [Fact]
    public void Undo_history_in_the_edit_menu_opens_the_history_tab() => AppHarness.Run(h =>
    {
        h.ViewModel.ShowHistoryCommand.Execute(null);

        h.ViewModel.SelectedRightTab.Should().Be(MainViewModel.HistoryTab);
    });

    [Fact]
    public void A_curve_is_an_open_line_through_three_dots_bent_at_the_middle_one() => AppHarness.Run(h =>
    {
        h.ViewModel.AddShapeCommand.Execute("curve");

        ShapeObject curve = h.Session.ActiveLabel.Objects.OfType<ShapeObject>().Last();
        curve.Shape.Should().Be(ShapeKind.Custom);
        curve.ClosedPath.Should().BeFalse();
        curve.CustomPoints.Should().HaveCount(3);
        curve.CurvedPoints.Should().Equal(false, true, false);

        List<Core.Units.PointMm> outline = CustomShape.OutlineIn(curve, new Core.Units.RectMm(0, 0, 40, 12));
        outline.Count.Should().BeGreaterThan(10, "the bend is drawn smooth, not as two straight lines");
        outline.Min(p => p.Y).Should().BeApproximately(0, 0.001, "the middle dot is the top of the arch");
    });
}
