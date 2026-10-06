using FluentAssertions;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Search;
using Xunit;

namespace LabelMaker.Tests.Search;

public sealed class FindPageNameTests
{
    private static ProjectModel Project()
    {
        var project = new ProjectModel();
        project.MasterLabel.Name = "Front label";
        project.MasterLabel.Objects.Add(new TextObject { Content = RichTextContent.FromPlainText("Olive oil") });
        project.Pages.Add(new LabelPage { Id = "p2", Name = "Back label", Document = new LabelDocument { Name = "Back label" } });
        project.Pages.Add(new LabelPage { Id = "p3", Name = "Shipping box", Document = new LabelDocument { Name = "Shipping box" } });
        return project;
    }

    [Fact]
    public void Page_names_are_found_with_no_object_and_the_page_to_open()
    {
        IReadOnlyList<FindMatch> matches = FindReplaceEngine.FindAll(Project(), null, new FindOptions { Query = "label" });

        matches.Should().HaveCount(2).And.OnlyContain(m => m.Field == FindField.PageName && m.Object == null);
        matches.Select(m => m.PageId).Should().Equal(null, "p2");
        matches[1].Hit.Should().Be("label");
        matches[1].Before.Should().Be("Back ");
    }

    [Fact]
    public void Page_names_can_be_left_out_and_the_current_page_scope_only_finds_its_own_name()
    {
        ProjectModel project = Project();
        FindReplaceEngine.FindAll(project, null, new FindOptions { Query = "box", Targets = FindTargets.Text }).Should().BeEmpty();

        var here = new FindOptions { Query = "label", Scope = FindScope.CurrentLabel };
        FindReplaceEngine.FindAll(project, "p2", here).Should().ContainSingle().Which.PageId.Should().Be("p2");
    }

    [Fact]
    public void Replacing_renames_the_matching_pages_but_never_to_an_empty_name()
    {
        ProjectModel project = Project();
        var options = new FindOptions { Query = "label" };

        FindReplaceEngine.PageRenames(project, null, options, "sticker", out int count)
            .Should().Equal((null, "Front sticker"), ("p2", "Back sticker"));
        count.Should().Be(2);

        FindReplaceEngine.PageRenames(project, null, new FindOptions { Query = "Shipping box" }, " ", out _).Should().BeEmpty();

        FindMatch one = FindReplaceEngine.FindAll(project, null, options)[1];
        FindReplaceEngine.RenamedPage(one, options, "tag").Should().Be("Back tag");
        FindReplaceEngine.BuildReplaceOne(one, options, "tag").Should().BeNull("a page name is not an object");
    }
}
