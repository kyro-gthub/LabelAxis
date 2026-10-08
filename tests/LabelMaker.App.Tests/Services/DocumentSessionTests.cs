using FluentAssertions;
using LabelMaker.App.Services;
using LabelMaker.Core.Model;
using Xunit;

namespace LabelMaker.App.Tests.Services;

/// <summary>The open project's pages, guides and undo, as the main window drives them.</summary>
public sealed class DocumentSessionTests
{
    [Fact]
    public void The_page_on_screen_is_counted_as_in_the_pages_panel() => AppHarness.Run(h =>
    {
        DocumentSession s = h.Session;
        s.ActivePagePosition.Should().Be(0, "the master label is the first row");

        LabelPage second = s.AddPage();
        s.AddPage();
        s.ActivePagePosition.Should().Be(2);

        s.SelectPage(second.Id);
        s.ActivePagePosition.Should().Be(1);

        s.SelectPage(null);
        s.ActivePagePosition.Should().Be(0);
    });

    [Fact]
    public void A_new_page_is_named_by_its_number_and_has_the_size_of_the_page_on_screen() => AppHarness.Run(h =>
    {
        LabelPage page = h.Session.AddPage();

        page.Name.Should().Be("Page 2");
        page.Document.SizeMm.Should().Be(h.Session.Project.MasterLabel.SizeMm);
        h.Session.ActivePageId.Should().Be(page.Id);
    });

    [Fact]
    public void Duplicating_a_page_puts_the_copy_right_after_it() => AppHarness.Run(h =>
    {
        LabelPage first = h.Session.AddPage("A");
        h.Session.AddPage("B");
        h.Session.SelectPage(first.Id);

        LabelPage? copy = h.Session.DuplicateActivePage();

        h.Session.Project.Pages.Select(p => p.Name).Should().Equal("A", "A copy", "B");
        h.Session.ActivePageId.Should().Be(copy!.Id);
    });

    [Fact]
    public void The_last_page_left_cannot_be_removed() => AppHarness.Run(h =>
    {
        h.Session.RemoveActivePage().Should().BeFalse();
        h.Session.History.CanUndo.Should().BeFalse();
    });

    [Fact]
    public void Removing_a_page_opens_the_one_after_it() => AppHarness.Run(h =>
    {
        LabelPage a = h.Session.AddPage("A");
        LabelPage b = h.Session.AddPage("B");
        h.Session.SelectPage(a.Id);

        h.Session.RemoveActivePage().Should().BeTrue();

        h.Session.ActivePageId.Should().Be(b.Id);
    });

    [Fact]
    public void Turning_shared_guides_off_gives_every_page_its_own_copy() => AppHarness.Run(h =>
    {
        DocumentSession s = h.Session;
        s.AddPage();
        s.SetSharedGuides(true);
        s.ActiveGuides.VerticalMm.Add(12.5);

        s.SetSharedGuides(false);

        s.Project.SharedGuides.Should().BeFalse();
        s.Project.Pages[0].Document.Guides.VerticalMm.Should().Contain(12.5, "no page loses the guides it was showing");
        s.Undo();
        s.Project.SharedGuides.Should().BeTrue();
    });

    [Fact]
    public void Renaming_the_page_on_screen_can_be_undone() => AppHarness.Run(h =>
    {
        LabelPage page = h.Session.AddPage("Old");

        h.Session.RenameActivePage("New");
        page.Name.Should().Be("New");

        h.Session.Undo();
        page.Name.Should().Be("Old");
    });
}
