using FluentAssertions;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using Xunit;

namespace LabelMaker.App.Tests.ViewModels;

/// <summary>Lining objects up, stacking them and the object list, through the main view model.</summary>
public sealed class ArrangeTests
{
    [Fact]
    public void One_object_aligns_to_the_label_edge() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        LabelObject text = h.Session.Selection.Single();
        text.Bounds = text.Bounds with { X = 10, Y = 7 };

        h.ViewModel.AlignLeftCommand.Execute(null);
        h.ViewModel.AlignTopCommand.Execute(null);

        text.Bounds.X.Should().BeApproximately(0, 1e-9);
        text.Bounds.Y.Should().BeApproximately(0, 1e-9);
    });

    [Fact]
    public void Several_objects_align_to_each_other_and_locked_ones_stay_put() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        LabelObject a = h.Session.Selection.Single();
        h.ViewModel.AddRectangleCommand.Execute(null);
        LabelObject b = h.Session.Selection.Single();
        h.ViewModel.AddEllipseCommand.Execute(null);
        LabelObject locked = h.Session.Selection.Single();
        a.Bounds = a.Bounds with { X = 5 };
        b.Bounds = b.Bounds with { X = 12 };
        locked.Bounds = locked.Bounds with { X = 3 };
        locked.Locked = true;
        h.Session.SetSelection(new[] { a, b, locked });

        h.ViewModel.AlignLeftCommand.Execute(null);

        a.Bounds.X.Should().BeApproximately(3, 1e-9, "the left edge of the whole selection");
        b.Bounds.X.Should().BeApproximately(3, 1e-9);
        locked.Bounds.X.Should().Be(3);
    });

    [Fact]
    public void Centring_puts_the_object_in_the_middle_of_the_label() => AppHarness.Run(h =>
    {
        h.ViewModel.AddRectangleCommand.Execute(null);
        LabelObject shape = h.Session.Selection.Single();

        h.ViewModel.CenterHorizontalCommand.Execute(null);
        h.ViewModel.CenterVerticalCommand.Execute(null);

        shape.Bounds.Center.X.Should().BeApproximately(h.Session.ActiveLabel.SizeMm.Width / 2, 1e-9);
        shape.Bounds.Center.Y.Should().BeApproximately(h.Session.ActiveLabel.SizeMm.Height / 2, 1e-9);
    });

    [Fact]
    public void Bring_to_front_and_send_to_back_change_the_stacking_order() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        LabelObject first = h.Session.Selection.Single();
        h.ViewModel.AddRectangleCommand.Execute(null);
        List<LabelObject> objects = h.Session.ActiveLabel.Objects;

        h.Session.SetSelection(new[] { first });
        h.ViewModel.BringToFrontCommand.Execute(null);
        objects[^1].Should().BeSameAs(first, "the last object is drawn on top");

        h.ViewModel.SendToBackCommand.Execute(null);
        objects[0].Should().BeSameAs(first);
    });

    [Fact]
    public void Duplicating_makes_a_copy_beside_the_original_and_selects_it() => AppHarness.Run(h =>
    {
        h.ViewModel.AddBarcodeCommand.Execute(null);
        LabelObject original = h.Session.Selection.Single();

        h.ViewModel.DuplicateCommand.Execute(null);

        LabelObject copy = h.Session.Selection.Single();
        copy.Should().NotBeSameAs(original);
        copy.Id.Should().NotBe(original.Id);
        copy.Should().BeOfType<BarcodeObject>();
        h.Session.ActiveLabel.Objects.Should().Contain(new[] { original, copy });
    });

    [Fact]
    public void Dragging_a_row_in_the_object_list_restacks_the_object() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        LabelObject bottom = h.Session.Selection.Single();
        h.ViewModel.AddRectangleCommand.Execute(null);
        LabelObject top = h.Session.Selection.Single();
        var rows = h.ViewModel.ObjectList;
        var bottomRow = rows.Single(r => r.Object == bottom);
        var topRow = rows.Single(r => r.Object == top);

        h.ViewModel.MoveObjectsInList(new[] { bottomRow }, topRow, below: false);

        h.Session.ActiveLabel.Objects[^1].Should().BeSameAs(bottom, "higher in the list is further forward");
    });

    [Fact]
    public void A_data_field_and_a_date_field_are_text_boxes_with_their_token() => AppHarness.Run(h =>
    {
        h.Shell.PromptAnswer = "Product";
        h.ViewModel.AddDataFieldCommand.Execute(null);
        h.ViewModel.AddDateFieldCommand.Execute(null);

        var texts = h.Session.ActiveLabel.Objects.OfType<TextObject>().Select(t => t.Content.PlainText).ToList();
        texts.Should().Contain("{CurrentDate}");
    });
}
