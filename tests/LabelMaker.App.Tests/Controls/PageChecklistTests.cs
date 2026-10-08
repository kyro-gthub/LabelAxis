using System.Windows.Controls;
using FluentAssertions;
using LabelMaker.App.Controls;
using LabelMaker.Core.Model;
using Xunit;

namespace LabelMaker.App.Tests.Controls;

/// <summary>The "Which pages" list shared by Apply page format, Apply text style and Remove from all pages.</summary>
public sealed class PageChecklistTests
{
    private static ProjectModel ThreePages()
    {
        var project = new ProjectModel();
        project.MasterLabel.Name = "Milk";
        project.Pages.Add(new LabelPage { Name = "Bread" });
        project.Pages.Add(new LabelPage { Name = "Eggs" });
        return project;
    }

    private static List<CheckBox> Rows(PageChecklist list) =>
        ((StackPanel)((StackPanel)list.Content).Children[0]).Children.Cast<CheckBox>().ToList();

    [Fact]
    public void Pages_are_numbered_as_in_the_pages_panel()
    {
        ProjectModel project = ThreePages();

        PageChecklist.Caption(project, 0).Should().Be("1.  Milk");
        PageChecklist.Caption(project, 2).Should().Be("3.  Eggs");
    }

    [Fact]
    public void A_hidden_current_page_is_left_out_and_the_others_start_ticked() => Sta.Run(() =>
    {
        var list = new PageChecklist();

        list.Fill(ThreePages(), current: 1, CurrentPageRow.Hidden);

        Rows(list).Select(r => r.Content).Should().Equal("1.  Milk", "3.  Eggs");
        list.Ticked.Should().Equal(0, 2);
    });

    [Fact]
    public void A_fixed_current_page_is_shown_greyed_out_and_never_counted_as_ticked() => Sta.Run(() =>
    {
        var list = new PageChecklist();

        list.Fill(ThreePages(), current: 0, CurrentPageRow.Fixed, caption => caption + " (this page)");

        List<CheckBox> rows = Rows(list);
        rows.Should().HaveCount(3);
        rows[0].IsEnabled.Should().BeFalse();
        rows[0].Content.Should().Be("1.  Milk (this page)");
        list.Ticked.Should().Equal(1, 2);
    });

    [Fact]
    public void An_optional_current_page_starts_unticked_and_can_be_ticked() => Sta.Run(() =>
    {
        var list = new PageChecklist();
        list.Fill(ThreePages(), current: 2, CurrentPageRow.Optional);

        list.Ticked.Should().Equal(0, 1);
        Rows(list)[2].IsChecked = true;
        list.Ticked.Should().Equal(0, 1, 2);
    });

    [Fact]
    public void Each_page_line_says_what_will_happen_there_except_the_greyed_out_one() => Sta.Run(() =>
    {
        var list = new PageChecklist();
        list.Fill(ThreePages(), current: 0, CurrentPageRow.Fixed);

        list.Describe((position, caption) => $"{caption}  ·  {position * 10} objects");

        Rows(list).Select(r => r.Content).Should().Equal("1.  Milk", "2.  Bread  ·  10 objects", "3.  Eggs  ·  20 objects");
    });

    [Fact]
    public void Ticking_a_page_tells_the_window() => Sta.Run(() =>
    {
        var list = new PageChecklist();
        list.Fill(ThreePages(), current: 0, CurrentPageRow.Hidden);
        int changes = 0;
        list.Changed += (_, _) => changes++;

        CheckBox first = Rows(list)[0];
        first.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, first));

        changes.Should().Be(1);
    });
}
