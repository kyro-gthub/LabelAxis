using FluentAssertions;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using Xunit;

namespace LabelMaker.App.Tests.ViewModels;

/// <summary>
/// The main window's commands, run on the view model without a window. Copy and paste are left
/// out: they use the real Windows clipboard, which the tests must not touch.
/// </summary>
public sealed class MainViewModelTests
{
    [Fact]
    public void Adding_a_text_box_selects_it_and_undo_takes_it_away() => AppHarness.Run(h =>
    {
        int before = h.Session.ActiveLabel.Objects.Count;

        h.ViewModel.AddTextCommand.Execute(null);

        h.Session.ActiveLabel.Objects.Should().HaveCount(before + 1);
        h.Session.Selection.Should().ContainSingle().Which.Should().BeOfType<TextObject>();
        h.ViewModel.ObjectList.Should().Contain(row => row.Object == h.Session.Selection[0]);

        h.ViewModel.UndoCommand.Execute(null);
        h.Session.ActiveLabel.Objects.Should().HaveCount(before);
    });

    [Fact]
    public void Delete_removes_the_selection_and_redo_removes_it_again() => AppHarness.Run(h =>
    {
        h.ViewModel.AddBarcodeCommand.Execute(null);
        LabelObject barcode = h.Session.Selection.Single();

        h.ViewModel.DeleteCommand.Execute(null);
        h.Session.ActiveLabel.Objects.Should().NotContain(barcode);

        h.ViewModel.UndoCommand.Execute(null);
        h.Session.ActiveLabel.Objects.Should().Contain(barcode);

        h.ViewModel.RedoCommand.Execute(null);
        h.Session.ActiveLabel.Objects.Should().NotContain(barcode);
    });

    [Fact]
    public void Delete_is_offered_only_while_something_is_selected() => AppHarness.Run(h =>
    {
        h.Session.ClearSelection();
        h.ViewModel.DeleteCommand.CanExecute(null).Should().BeFalse();

        h.ViewModel.AddTextCommand.Execute(null);
        h.ViewModel.DeleteCommand.CanExecute(null).Should().BeTrue();
    });

    [Fact]
    public void Remove_from_all_pages_on_a_one_page_project_deletes_without_asking() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        LabelObject text = h.Session.Selection.Single();

        h.ViewModel.RemoveFromAllPagesCommand.Execute(null);

        h.Shell.RemoveFromPagesShown.Should().Be(0);
        h.Session.ActiveLabel.Objects.Should().NotContain(text);
    });

    [Fact]
    public void Remove_from_all_pages_asks_which_pages_when_there_are_several() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        h.ViewModel.DuplicatePageCommand.Execute(null);
        h.Session.SetSelection(new[] { h.Session.ActiveLabel.Objects[^1] });
        h.Shell.RemoveFromPages = () => 2;

        h.ViewModel.RemoveFromAllPagesCommand.Execute(null);

        h.Shell.RemoveFromPagesShown.Should().Be(1);
        h.ViewModel.StatusText.Should().Be("Removed 2 object(s) from the pages.");
    });

    [Fact]
    public void Duplicating_a_page_copies_its_objects_and_opens_the_copy() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        h.ViewModel.AddBarcodeCommand.Execute(null);
        int objects = h.Session.ActiveLabel.Objects.Count;

        h.ViewModel.DuplicatePageCommand.Execute(null);

        h.Session.Project.Pages.Should().ContainSingle();
        h.Session.ActivePagePosition.Should().Be(1);
        h.Session.ActiveLabel.Objects.Should().HaveCount(objects);
        h.Session.ActiveLabel.Objects.Should().NotIntersectWith(h.Session.Project.MasterLabel.Objects, "the copy has objects of its own");
        h.ViewModel.Pages.Should().HaveCount(2);
    });

    [Fact]
    public void Grouping_gives_the_selection_one_group_and_ungrouping_takes_it_off() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        LabelObject first = h.Session.Selection.Single();
        h.ViewModel.AddRectangleCommand.Execute(null);
        LabelObject second = h.Session.Selection.Single();
        h.Session.SetSelection(new[] { first, second });

        h.ViewModel.GroupCommand.Execute(null);
        first.GroupId.Should().NotBeNull().And.Be(second.GroupId);

        h.ViewModel.UngroupCommand.Execute(null);
        first.GroupId.Should().BeNull();
        second.GroupId.Should().BeNull();
    });

    [Fact]
    public void Renaming_an_object_from_the_list_can_be_undone() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        LabelObject text = h.Session.Selection.Single();
        var row = h.ViewModel.ObjectList.Single(r => r.Object == text);

        h.ViewModel.RenameObject(row, "  Price  ");

        text.Name.Should().Be("Price");
        h.ViewModel.StatusText.Should().Be("Renamed to \"Price\"");
        h.ViewModel.UndoCommand.Execute(null);
        text.Name.Should().NotBe("Price");
    });

    [Fact]
    public void Help_opens_on_the_page_about_what_is_selected() => AppHarness.Run(h =>
    {
        h.ViewModel.AddBarcodeCommand.Execute(null);
        h.ViewModel.HelpTopicForSelection().Should().Be("barcodes");

        h.ViewModel.AddTextCommand.Execute(null);
        h.ViewModel.HelpTopicForSelection().Should().Be("text");

        h.Session.Selection[0].Should().BeOfType<TextObject>().Which.Content = RichTextContent.FromPlainText("سعر");
        h.ViewModel.HelpTopicForSelection().Should().Be("arabic");
    });

    [Fact]
    public void Removing_the_master_page_moves_the_next_page_up_and_undo_brings_it_back() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        string masterName = h.Session.Project.MasterLabel.Name;
        LabelPage added = h.Session.AddPage("Second");
        h.Session.SelectPage(null);

        h.Session.RemoveActivePage().Should().BeTrue();

        h.Session.Project.Pages.Should().BeEmpty();
        h.Session.Project.MasterLabel.Should().BeSameAs(added.Document);
        h.ViewModel.UndoCommand.Execute(null);
        h.Session.Project.Pages.Should().ContainSingle();
        h.Session.Project.MasterLabel.Name.Should().Be(masterName);
    });
}
