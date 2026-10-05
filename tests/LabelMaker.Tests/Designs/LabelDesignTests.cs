using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Designs;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Serialization;
using Xunit;

namespace LabelMaker.Tests.Designs;

public sealed class LabelDesignTests
{
    private static readonly TemplateStore Store = TemplateStore.CreateWithBuiltIns();

    public static IEnumerable<object[]> Ids => DesignCatalog.BuiltIn.Select(d => new object[] { d.Id });

    private static LabelDesign Design(string id) => DesignCatalog.BuiltIn.Single(d => d.Id == id);

    [Fact]
    public void The_gallery_has_its_66_designs_with_names_groups_and_descriptions()
    {
        DesignCatalog.BuiltIn.Should().HaveCount(66);
        DesignCatalog.BuiltIn.Select(d => d.Id).Should().OnlyHaveUniqueItems();
        DesignCatalog.BuiltIn.Should().OnlyContain(d => d.Name.Length > 0 && d.Group.Length > 0 && d.Description.Length > 0);
        DesignCatalog.Groups(DesignCatalog.BuiltIn).Should().HaveCount(10);
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Each_design_builds_on_its_own_sheet_or_roll(string id)
    {
        LabelDesign design = Design(id);
        ProjectDocument document = DesignBuilder.Build(design, Store);
        LabelTemplate template = document.Project.Template!;

        template.Grid.LabelWidthMm.Should().BeApproximately(design.Size.WidthMm, 0.05);
        template.Grid.LabelHeightMm.Should().BeApproximately(design.Size.HeightMm, 0.05);
        if (design.Size.IsThermal)
        {
            template.IsRoll.Should().BeTrue();
        }
        else
        {
            template.IsRoll.Should().BeFalse($"{design.Size.Sheet} is a sheet LabelAxis knows");
            $"{template.Brand} {template.ProductCode}".Should().Be(design.Size.Sheet);
        }

        document.Project.MasterLabel.Objects.Should().HaveCount(design.Objects.Count);
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Every_barcode_holds_its_sample_value(string id)
    {
        foreach (BarcodeObject barcode in DesignBuilder.Build(Design(id), Store).Project.MasterLabel.Objects.OfType<BarcodeObject>())
        {
            BarcodeValidator.Validate(barcode.Spec, barcode.Spec.Data).IsValid.Should().BeTrue($"{id} {barcode.Name} = {barcode.Spec.Data}");
        }
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Everything_stays_on_the_label(string id)
    {
        LabelDesign design = Design(id);
        foreach (LabelObject o in DesignBuilder.Build(design, Store).Project.MasterLabel.Objects)
        {
            var b = o.RotatedBounds;
            b.Left.Should().BeGreaterThanOrEqualTo(-0.6, $"{id} {o.Name}");
            b.Top.Should().BeGreaterThanOrEqualTo(-0.6, $"{id} {o.Name}");
            b.Right.Should().BeLessThanOrEqualTo(design.Size.WidthMm + 0.6, $"{id} {o.Name}");
            b.Bottom.Should().BeLessThanOrEqualTo(design.Size.HeightMm + 0.6, $"{id} {o.Name}");
        }
    }

    [Fact]
    public void Sample_words_go_straight_onto_the_label_unless_data_fields_are_kept()
    {
        LabelDesign design = Design("retail-01");

        ProjectModel typed = DesignBuilder.Build(design, Store).Project;
        TextOf(typed, "Product").Should().Be("Notebook A5, 100 sheets");
        typed.Variables.Should().BeEmpty();

        ProjectModel fields = DesignBuilder.Build(design, Store, new DesignBuildOptions { UseDataFields = true }).Project;
        TextOf(fields, "Product").Should().Be("{Product}");
        fields.Variables.Single(v => v.Name == "Product").Value.Should().Be("Notebook A5, 100 sheets");
        design.Fields.Should().Contain(new[] { "Product", "Price", "Code", "Date" });
    }

    [Fact]
    public void A_field_inside_words_takes_only_its_own_part_of_the_sample()
    {
        ProjectModel project = DesignBuilder.Build(Design("product-05"), Store, new DesignBuildOptions { UseDataFields = true }).Project;
        TextOf(project, "Date").Should().Be("Produced {Date}");
        project.Variables.Single(v => v.Name == "Date").Value.Should().Be("05/10/2026");
    }

    [Fact]
    public void An_upright_line_is_a_level_line_turned_a_quarter_round()
    {
        ShapeObject fold = DesignBuilder.Build(Design("retail-05"), Store).Project.MasterLabel.Objects.OfType<ShapeObject>().Single(s => s.Name == "Fold");
        fold.Shape.Should().Be(ShapeKind.Line);
        fold.RotationDegrees.Should().Be(90);
        fold.RotatedBounds.Center.X.Should().BeApproximately(12.7, 0.01);
        fold.RotatedBounds.Height.Should().BeApproximately(7, 0.01);
    }

    [Fact]
    public void A_design_survives_saving_and_opening()
    {
        ProjectDocument document = DesignBuilder.Build(Design("shipping-01"), Store, new DesignBuildOptions { UseDataFields = true });
        string path = Path.Combine(Path.GetTempPath(), $"design-{Guid.NewGuid():N}.labelproj");
        try
        {
            ProjectFile.Save(document, path);
            ProjectDocument back = ProjectFile.Load(path);
            back.Project.MasterLabel.Objects.Select(o => o.Name).Should().Equal(document.Project.MasterLabel.Objects.Select(o => o.Name));
            back.Project.Variables.Should().HaveCount(document.Project.Variables.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TextOf(ProjectModel project, string name) =>
        project.MasterLabel.Objects.OfType<TextObject>().Single(t => t.Name == name).Content.PlainText;
}
