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
    public void A_removed_object_does_not_stay_selected_after_undo_or_a_jump_in_the_history() => AppHarness.Run(h =>
    {
        h.ViewModel.AddShapeCommand.Execute("ellipse");
        LabelObject added = h.Session.ActiveLabel.Objects[^1];
        h.Session.Selection.Should().Contain(added);

        h.Session.Undo();
        h.Session.Selection.Should().NotContain(added);

        h.Session.Redo();
        h.Session.SetSelection(new[] { added });
        h.ViewModel.GoToHistoryStep(h.ViewModel.HistorySteps[0]);
        h.Session.Selection.Should().BeEmpty();
    });

    [Fact]
    public void A_change_in_properties_is_named_in_the_history_by_the_panels_words()
    {
        StepNames.For("FillHex").Should().Be("Fill");
        StepNames.For("ArrowHeadSizeMm").Should().Be("Arrow head size");
        StepNames.For("Reset crop").Should().Be("Reset crop", "words already given are the name");
        StepNames.For("SomeNewSettingMm").Should().Be("Some new setting", "a setting with no name here is spelled out");
    }

    [Fact]
    public void Every_step_name_has_a_translation_in_every_language()
    {
        foreach (LabelMaker.App.Localization.AppLanguage language in LabelMaker.App.Localization.Localizer.Languages.Where(l => l.Code != "en"))
        {
            Core.Localization.Translator translator = LabelMaker.App.Localization.Localizer.Load(language);
            foreach (string name in StepNames.All.Append("GS1 barcode").Append("Use label sheet").Append("Apply formatting to {0} objects"))
            {
                translator.Knows(name).Should().BeTrue($"{language.EnglishName} needs: {name}");
            }
        }
    }

    [Fact]
    public void The_properties_button_opens_apply_formatting_set_to_the_selections_look() => AppHarness.Run(h =>
    {
        h.Session.AddPage("Second");
        h.Session.SelectPage(null);
        h.Session.SetSelection(h.Session.ActiveLabel.Objects.Take(1));

        h.ViewModel.ApplySelectionFormatCommand.Execute(null);
        h.Shell.ApplyLayoutLookOfSelection.Should().BeTrue();

        h.ViewModel.ApplyLayoutCommand.Execute(null);
        h.Shell.ApplyLayoutLookOfSelection.Should().BeFalse();
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
