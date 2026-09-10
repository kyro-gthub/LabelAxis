using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Data;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;
using LabelMaker.Export;
using LabelMaker.Export.Pdf;
using LabelMaker.Export.Svg;
using PdfSharp.Pdf.IO;
using Xunit;

namespace LabelMaker.Tests.Export;

public sealed class PdfExportTests
{
    private static ProjectModel MakeProject(string templateId)
    {
        LabelTemplate template = TemplateStore.CreateWithBuiltIns().Get(templateId)!;
        ProjectModel project = ProjectModel.CreateNew(template, "Export");
        project.MasterLabel.Objects.Add(new TextObject
        {
            Bounds = new RectMm(2, 2, template.Grid.LabelWidthMm - 4, 6),
            Content = RichTextContent.FromPlainText("{Product}"),
            FontSizePt = 8,
        });
        project.MasterLabel.Objects.Add(new BarcodeObject
        {
            Bounds = new RectMm(2, 9, template.Grid.LabelWidthMm - 4, 10),
            AutoSize = false,
            Spec = new BarcodeSpec { Symbology = BarcodeSymbology.Code128, Data = "{Barcode}", ModuleWidthMm = 0.25, BarHeightMm = 8 },
        });
        return project;
    }

    [Fact]
    public void Sheet_pdf_has_the_exact_physical_page_size()
    {
        ProjectModel project = MakeProject("formtec-3100"); // A4
        var request = new ExportRequest { Project = project, Scope = ExportScope.Sheet, FillSheetWhenNoData = true };

        byte[] pdf = new PdfExporter().Export(request, new AssetStore());

        using var read = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        read.PageCount.Should().Be(1);
        read.Pages[0].Width.Millimeter.Should().BeApproximately(210, 0.05);
        read.Pages[0].Height.Millimeter.Should().BeApproximately(297, 0.05);
    }

    [Fact]
    public void Individual_label_pdf_page_equals_label_size()
    {
        // A label defined 40 x 20 mm must export as a 40 x 20 mm page (spec §41).
        var grid = new LabelGrid { Columns = 4, Rows = 12, LabelWidthMm = 40, LabelHeightMm = 20, MarginLeftMm = 8, MarginTopMm = 12, HGapMm = 2, VGapMm = 2 };
        var template = new LabelTemplate { Id = "t40x20", Name = "40x20", Grid = grid };
        ProjectModel project = ProjectModel.CreateNew(template, "40x20");
        project.MasterLabel.SizeMm = new SizeMm(40, 20);
        project.MasterLabel.Objects.Add(new TextObject { Bounds = new RectMm(2, 2, 36, 6), Content = RichTextContent.FromPlainText("Hi") });

        DataSet data = DataSet.FromRows(new[] { "x" }, new[] { new[] { "1" }, new[] { "2" }, new[] { "3" } });
        var request = new ExportRequest { Project = project, Data = data, Scope = ExportScope.PerRecord };

        byte[] pdf = new PdfExporter().Export(request, new AssetStore());

        using var read = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        read.PageCount.Should().Be(3);
        read.Pages[0].Width.Millimeter.Should().BeApproximately(40, 0.05);
        read.Pages[0].Height.Millimeter.Should().BeApproximately(20, 0.05);
    }

    [Fact]
    public void Variable_data_produces_one_sheet_page_per_24_records()
    {
        ProjectModel project = MakeProject("formtec-3103"); // 24-up
        var rows = Enumerable.Range(1, 30).Select(i => new[] { $"Item {i}", $"ABC{i:000000}" }).ToList();
        DataSet data = DataSet.FromRows(new[] { "Product", "Barcode" }, rows);

        var request = new ExportRequest { Project = project, Data = data, Scope = ExportScope.Sheet };
        byte[] pdf = new PdfExporter().Export(request, new AssetStore());

        using var read = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        read.PageCount.Should().Be(2);
    }

    [Fact]
    public void Svg_label_export_declares_physical_size_in_millimetres()
    {
        ProjectModel project = MakeProject("formtec-3200");
        var ctx = new ResolveContext { Record = new Dictionary<string, string> { ["Product"] = "Widget", ["Barcode"] = "ABC001" } };

        string svg = new SvgExporter().ExportLabel(project.MasterLabel, ctx);

        svg.Should().StartWith("<svg");
        svg.Should().Contain("width=\"38.1mm\"");
        svg.Should().Contain("height=\"21.2mm\"");
        svg.Should().Contain("Widget");
    }

    [Fact]
    public void Standalone_barcode_svg_contains_bar_rectangles()
    {
        var spec = new BarcodeSpec { Symbology = BarcodeSymbology.Code128, Data = "ABC000001", ModuleWidthMm = 0.33, BarHeightMm = 15 };
        string svg = new SvgExporter().ExportBarcode(spec, spec.Data);

        svg.Should().Contain("<rect");
        svg.Should().Contain("ABC000001"); // HRI
        svg.Should().Contain("mm\"");
    }
}
