using FluentAssertions;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Printing;
using Xunit;

namespace LabelMaker.Tests.Printing;

public sealed class PageChoiceTests
{
    private static (ProjectModel Project, LabelPage A, LabelPage B) Project()
    {
        var p = new ProjectModel();
        var a = new LabelPage { Name = "A", Document = new LabelDocument { Name = "A" } };
        var b = new LabelPage { Name = "B", Document = new LabelDocument { Name = "B" } };
        p.Pages.Add(a);
        p.Pages.Add(b);
        return (p, a, b);
    }

    [Fact]
    public void Current_page_prints_only_the_page_that_was_on_screen()
    {
        (ProjectModel p, LabelPage a, LabelPage b) = Project();

        PrintablePages.For(p, new PrintSettings { PagesToPrint = PagesToPrint.Current, CurrentPage = 2 })
            .Should().Equal(b.Document);
        PrintablePages.For(p, new PrintSettings { PagesToPrint = PagesToPrint.Current, CurrentPage = 1, CopiesPerPage = 3 })
            .Should().Equal(a.Document, a.Document, a.Document);
    }

    [Fact]
    public void Current_page_can_be_the_master_label_even_when_it_is_empty()
    {
        (ProjectModel p, _, _) = Project();

        PrintablePages.For(p, new PrintSettings { PagesToPrint = PagesToPrint.Current, CurrentPage = 0 })
            .Should().Equal(p.MasterLabel);
    }

    [Fact]
    public void Selected_pages_print_in_page_list_order_whatever_order_they_were_ticked()
    {
        (ProjectModel p, _, LabelPage b) = Project();
        p.MasterLabel.Objects.Add(new ShapeObject());

        PrintablePages.For(p, new PrintSettings { PagesToPrint = PagesToPrint.Chosen, ChosenPages = { 2, 0 } })
            .Should().Equal(p.MasterLabel, b.Document);
        PrintablePages.For(p, new PrintSettings { PagesToPrint = PagesToPrint.Chosen })
            .Should().BeEmpty();
    }

    [Fact]
    public void A_page_number_past_the_end_prints_nothing_rather_than_the_wrong_page()
    {
        (ProjectModel p, _, _) = Project();

        PrintablePages.For(p, new PrintSettings { PagesToPrint = PagesToPrint.Current, CurrentPage = 7 }).Should().BeEmpty();
        PrintablePages.For(p, new PrintSettings { PagesToPrint = PagesToPrint.Chosen, ChosenPages = { 9 } }).Should().BeEmpty();
    }

    [Fact]
    public void The_page_choice_is_for_one_job_and_is_not_saved_with_the_project()
    {
        var s = new PrintSettings { PagesToPrint = PagesToPrint.Chosen, CurrentPage = 2, ChosenPages = { 1, 2 } };

        string json = System.Text.Json.JsonSerializer.Serialize(s);
        json.Should().NotContain(nameof(PrintSettings.PagesToPrint)).And.NotContain(nameof(PrintSettings.ChosenPages));

        PrintSettings copy = s.Clone();
        copy.PagesToPrint.Should().Be(PagesToPrint.Chosen);
        copy.ChosenPages.Should().Equal(1, 2);
        copy.ChosenPages.Should().NotBeSameAs(s.ChosenPages);
    }
}
