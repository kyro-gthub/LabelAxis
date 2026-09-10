using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Data;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Preflight;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Preflight;

public sealed class PreflightTests
{
    private static ProjectModel NewProject()
    {
        LabelTemplate t = TemplateStore.CreateWithBuiltIns().Get("formtec-3200")!; // 38.1 x 21.2
        return ProjectModel.CreateNew(t, "Preflight");
    }

    [Fact]
    public void Clean_label_produces_no_issues()
    {
        ProjectModel project = NewProject();
        project.MasterLabel.Objects.Add(new TextObject { Bounds = new RectMm(2, 2, 30, 6), Content = RichTextContent.FromPlainText("Hello") });

        PreflightReport report = new PreflightChecker().Check(project);
        report.IsClean.Should().BeTrue();
    }

    [Fact]
    public void Object_past_the_label_edge_is_flagged()
    {
        ProjectModel project = NewProject();
        project.MasterLabel.Objects.Add(new TextObject { Bounds = new RectMm(20, 2, 40, 6), Content = RichTextContent.FromPlainText("Overflow") });

        PreflightReport report = new PreflightChecker().Check(project);
        report.Warnings.Should().Contain(i => i.Category == PreflightCategory.ObjectOutsideLabel);
    }

    [Fact]
    public void Missing_image_is_an_error_with_navigation_target()
    {
        ProjectModel project = NewProject();
        var image = new ImageObject { Bounds = new RectMm(2, 2, 10, 10), AssetId = "deadbeef", SourceFileName = "logo.png" };
        project.MasterLabel.Objects.Add(image);

        var env = new PreflightEnvironment { AssetExists = _ => false };
        PreflightReport report = new PreflightChecker(env).Check(project);

        PreflightIssue issue = report.Errors.Single(i => i.Category == PreflightCategory.MissingImage);
        issue.ObjectId.Should().Be(image.Id);
    }

    [Fact]
    public void Invalid_barcode_value_is_reported_per_record()
    {
        ProjectModel project = NewProject();
        project.MasterLabel.Objects.Add(new BarcodeObject
        {
            Bounds = new RectMm(2, 2, 30, 12),
            Spec = new BarcodeSpec { Symbology = BarcodeSymbology.Ean13, Data = "{Code}" },
        });

        DataSet data = DataSet.FromRows(new[] { "Code" }, new[]
        {
            new[] { "5901234123457" }, // valid
            new[] { "notanumber" },    // invalid
        });

        PreflightReport report = new PreflightChecker().Check(project, data);

        report.Errors.Should().Contain(i => i.Category == PreflightCategory.InvalidBarcode && i.RecordIndex == 1);
    }

    [Fact]
    public void Duplicate_barcodes_across_records_are_detected()
    {
        ProjectModel project = NewProject();
        project.MasterLabel.Objects.Add(new BarcodeObject
        {
            Bounds = new RectMm(2, 2, 30, 12),
            Spec = new BarcodeSpec { Symbology = BarcodeSymbology.Code128, Data = "{Code}" },
        });

        DataSet data = DataSet.FromRows(new[] { "Code" }, new[]
        {
            new[] { "ABC001258" }, new[] { "ABC999" }, new[] { "ABC001258" },
        });

        var env = new PreflightEnvironment { BarcodeDuplicatePolicy = DuplicatePolicy.Warn };
        PreflightReport report = new PreflightChecker(env).Check(project, data);

        report.Issues.Should().Contain(i =>
            i.Category == PreflightCategory.DuplicateBarcode && i.Message.Contains("ABC001258"));
    }

    [Fact]
    public void Empty_required_field_produces_a_warning()
    {
        ProjectModel project = NewProject();
        project.MasterLabel.Objects.Add(new TextObject
        {
            Bounds = new RectMm(2, 2, 30, 6),
            Content = RichTextContent.FromPlainText("{Name}"),
        });

        DataSet data = DataSet.FromRows(new[] { "Name" }, new[] { new[] { "" } });

        PreflightReport report = new PreflightChecker().Check(project, data);
        report.Warnings.Should().Contain(i => i.Category == PreflightCategory.EmptyRequiredField);
    }
}
