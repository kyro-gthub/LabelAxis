using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Data;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Search;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Search;

public sealed class ProjectSearchTests
{
    private static (ProjectModel Project, DataSet Data) BuildProject(int recordCount)
    {
        LabelTemplate template = TemplateStore.CreateWithBuiltIns().Get("formtec-gs-1065")!;
        ProjectModel project = ProjectModel.CreateNew(template, "Search project");

        project.MasterLabel.Objects.Add(new BarcodeObject
        {
            Name = "code",
            Spec = new BarcodeSpec { Symbology = BarcodeSymbology.Code128, Data = "{Barcode}" },
        });

        var page = new LabelPage { Name = "Front label", Tags = { "packaging", "retail" } };
        page.Document.Objects.Add(new TextObject { Content = RichTextContent.FromPlainText("Premium Coffee Beans") });
        project.Pages.Add(page);

        var rows = new List<string[]>();
        for (int i = 1; i <= recordCount; i++)
        {
            rows.Add(new[] { $"Product {i}", $"ABC{i:000000}" });
        }

        DataSet data = DataSet.FromRows(new[] { "Name", "Barcode" }, rows);
        return (project, data);
    }

    [Fact]
    public void Finds_a_page_by_partial_name_and_tag()
    {
        (ProjectModel project, DataSet data) = BuildProject(10);
        var index = new ProjectSearchIndex();
        index.Rebuild(project, data);

        index.Search("front").Should().Contain(r => r.Kind == SearchResultKind.Page && r.Title == "Front label");
        index.Search("packaging").Should().Contain(r => r.PageId == project.Pages[0].Id);
        index.Search("coffee").Should().Contain(r => r.Kind == SearchResultKind.MasterObject || r.Kind == SearchResultKind.Page);
    }

    [Fact]
    public void Finds_a_specific_barcode_among_five_thousand_records()
    {
        (ProjectModel project, DataSet data) = BuildProject(5000);
        var index = new ProjectSearchIndex();
        index.Rebuild(project, data);

        var hits = index.FindRecords(data, "ABC001459", exact: true);
        hits.Should().ContainSingle().Which.Should().Be(1458);

        var results = index.Search("ABC004999");
        results.Should().Contain(r => r.Kind == SearchResultKind.DataRecord && r.RecordIndex == 4998);
    }

    [Fact]
    public void Search_over_large_dataset_completes_quickly()
    {
        (ProjectModel project, DataSet data) = BuildProject(10_000);
        var index = new ProjectSearchIndex();
        index.Rebuild(project, data);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var results = index.Search("Product 9999");
        sw.Stop();

        results.Should().NotBeEmpty();
        sw.ElapsedMilliseconds.Should().BeLessThan(500);
    }

    [Fact]
    public void Template_and_sequence_names_are_searchable()
    {
        (ProjectModel project, DataSet data) = BuildProject(3);
        project.Sequences.Add(new LabelMaker.Core.Sequences.SequenceDefinition { Name = "Asset tag serial" });
        var index = new ProjectSearchIndex();
        index.Rebuild(project, data);

        index.Search("1065").Should().Contain(r => r.Kind == SearchResultKind.Template);
        index.Search("asset tag").Should().Contain(r => r.Kind == SearchResultKind.Sequence);
    }
}
