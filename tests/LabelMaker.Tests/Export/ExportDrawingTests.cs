using FluentAssertions;
using LabelMaker.Core.Barcodes;
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
using LabelMaker.Tests.Support;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using Xunit;

namespace LabelMaker.Tests.Export;

/// <summary>
/// Every kind of object drawn into PDF and SVG: text with its styles, shapes with gradients and
/// dashes, pictures cropped and clipped, tables and barcodes, turned and see-through. A PDF
/// must open and hold what was drawn; an SVG must say it in its own words.
/// </summary>
public sealed class ExportDrawingTests
{
    private const double LabelW = 99.1;
    private const double LabelH = 67.7;

    private static (ProjectModel Project, AssetStore Assets) EverythingProject()
    {
        var assets = new AssetStore();
        string picture = assets.Add(TestImages.Png(8, 6), ".png");

        var grid = new LabelGrid { Columns = 1, Rows = 1, LabelWidthMm = LabelW, LabelHeightMm = LabelH };
        ProjectModel project = ProjectModel.CreateNew(new LabelTemplate { Id = "everything", Name = "Everything", Grid = grid }, "Everything");
        project.MasterLabel.SizeMm = new SizeMm(LabelW, LabelH);
        List<LabelObject> o = project.MasterLabel.Objects;

        o.Add(new TextObject
        {
            Bounds = new RectMm(2, 2, 60, 8),
            Content = new RichTextContent
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs =
                        {
                            new TextRun { Text = "Bold ", Bold = true },
                            new TextRun { Text = "italic ", Italic = true },
                            new TextRun { Text = "under ", Underline = true },
                            new TextRun { Text = "struck", Strikethrough = true, Foreground = new ColorRgba(200, 0, 0) },
                        },
                    },
                },
            },
            Background = new ColorRgba(255, 255, 0),
            BackgroundTo = new ColorRgba(255, 255, 255),
        });
        o.Add(new TextObject { Bounds = new RectMm(2, 11, 60, 6), Content = RichTextContent.FromPlainText("{Product}"), HorizontalAlignment = HorizontalAlignment.Right });
        o.Add(new TextObject { Bounds = new RectMm(64, 2, 30, 6), Content = RichTextContent.FromPlainText("السعر ١٢٫٥٠"), Direction = TextDirection.RightToLeft });
        o.Add(new ShapeObject { Shape = ShapeKind.RoundRectangle, Bounds = new RectMm(2, 19, 20, 10), Fill = new ColorRgba(44, 111, 232), GradientTo = new ColorRgba(111, 211, 247), GradientAngleDegrees = 45 });
        o.Add(new ShapeObject { Shape = ShapeKind.Ellipse, Bounds = new RectMm(24, 19, 14, 10), Fill = new ColorRgba(0, 160, 90), StrokeWidthMm = 0.5, DashStyle = DashStyle.Dash });
        o.Add(new ShapeObject { Shape = ShapeKind.Ellipse, Bounds = new RectMm(40, 19, 10, 10), Fill = ColorRgba.Black, GradientTo = ColorRgba.White });
        o.Add(new ShapeObject { Shape = ShapeKind.Polygon, Bounds = new RectMm(52, 19, 10, 10), PolygonPoints = 5, StarInnerRatio = 0.45, Fill = new ColorRgba(250, 200, 0), GradientTo = new ColorRgba(250, 120, 0) });
        o.Add(new ShapeObject { Shape = ShapeKind.Line, Bounds = new RectMm(2, 31, 30, 0.1), StrokeWidthMm = 0.4, DashStyle = DashStyle.Dot });
        o.Add(new ShapeObject { Shape = ShapeKind.Arrow, Bounds = new RectMm(34, 31, 20, 4), StrokeWidthMm = 0.4 });
        o.Add(new ShapeObject
        {
            Shape = ShapeKind.Custom,
            Bounds = new RectMm(64, 19, 12, 12),
            CustomPoints = { new PointMm(0, 1), new PointMm(0.5, 0), new PointMm(1, 1) },
            CurvedPoints = { false, true, false },
            Fill = new ColorRgba(120, 60, 200),
        });
        o.Add(new ShapeObject { Shape = ShapeKind.Rectangle, Bounds = new RectMm(78, 19, 12, 12), Fill = new ColorRgba(0, 0, 0, 90), RotationDegrees = 30, Opacity = 0.5 });
        o.Add(new ImageObject
        {
            AssetId = picture,
            Bounds = new RectMm(2, 37, 16, 12),
            BorderWidthMm = 0.4,
            BorderLines = ImageBorderLines.Double,
            Adjustments = new ImageAdjustments { CropX = 0.1, CropY = 0.1, CropWidth = 0.8, CropHeight = 0.8, FlipHorizontal = true },
        });
        o.Add(new ImageObject { AssetId = picture, Bounds = new RectMm(20, 37, 12, 12), ClipShape = ImageClipShape.Ellipse, Opacity = 0.6 });
        o.Add(new ImageObject { AssetId = picture, Bounds = new RectMm(34, 37, 12, 12), ClipShape = ImageClipShape.RoundedRectangle, FitMode = ImageFitMode.Stretch });
        o.Add(new ImageObject { AssetId = "missing", Bounds = new RectMm(48, 37, 8, 8) });
        o.Add(new TableObject
        {
            Bounds = new RectMm(58, 37, 38, 12),
            Rows = 2,
            Columns = 2,
            Cells = new List<string> { "Item", "Price", "Milk", "{Price}" },
            FirstRowIsHeader = true,
            HeaderFill = new ColorRgba(232, 241, 254),
            Bold = true,
        });
        o.Add(new BarcodeObject
        {
            Bounds = new RectMm(2, 51, 40, 14),
            AutoSize = false,
            Spec = new BarcodeSpec { Symbology = BarcodeSymbology.Code128, Data = "{Barcode}", ModuleWidthMm = 0.25, BarHeightMm = 10 },
        });
        o.Add(new BarcodeObject
        {
            Bounds = new RectMm(46, 51, 14, 14),
            Spec = new BarcodeSpec { Symbology = BarcodeSymbology.QrCode, Data = "https://example.com/{Barcode}", Foreground = new ColorRgba(20, 20, 120) },
        });
        return (project, assets);
    }

    private static ResolveContext Record() =>
        new() { Record = new Dictionary<string, string> { ["Product"] = "Milk 1 L", ["Price"] = "1.25", ["Barcode"] = "ABC001" } };

    private static List<string> Operators(PdfPage page) =>
        Flatten(ContentReader.ReadContent(page)).OfType<COperator>().Select(op => op.OpCode.Name).ToList();

    private static IEnumerable<CObject> Flatten(CSequence sequence)
    {
        foreach (CObject item in sequence)
        {
            yield return item;
            if (item is CSequence inner)
            {
                foreach (CObject nested in Flatten(inner))
                {
                    yield return nested;
                }
            }
        }
    }

    [Fact]
    public void A_label_with_every_kind_of_object_exports_to_a_pdf_that_holds_them()
    {
        (ProjectModel project, AssetStore assets) = EverythingProject();
        var data = Core.Data.DataSet.FromRows(new[] { "Product", "Price", "Barcode" }, new[] { new[] { "Milk 1 L", "1.25", "ABC001" } });
        var request = new ExportRequest { Project = project, Data = data, Scope = ExportScope.PerRecord };

        byte[] pdf = new PdfExporter().Export(request, assets);

        using PdfDocument read = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);
        read.PageCount.Should().Be(1);
        PdfPage page = read.Pages[0];
        page.Width.Millimeter.Should().BeApproximately(LabelW, 0.05);

        List<string> ops = Operators(page);
        ops.Should().Contain("Do", "the pictures are placed as images");
        ops.Should().Contain("re", "boxes and bars are drawn as rectangles");
        ops.Should().Contain("c", "ellipses, rounded corners and curved dots are curves");
        ops.Should().Contain(o => o == "W" || o == "W*", "pictures cropped to a shape are clipped");
        ops.Should().Contain("cm", "a turned object is drawn turned");
        ops.Should().Contain("d", "dashed lines keep their dashes");
        ops.Should().Contain(o => o == "Tj" || o == "TJ", "text is real, selectable text");

        PdfDictionary resources = page.Resources;
        resources.Elements.GetDictionary("/XObject").Should().NotBeNull();
        resources.Elements.GetDictionary("/Font").Should().NotBeNull();
        resources.Elements.GetDictionary("/ExtGState").Should().NotBeNull("see-through objects need a transparency setting");
        (resources.Elements.GetDictionary("/Shading") ?? resources.Elements.GetDictionary("/Pattern"))
            .Should().NotBeNull("gradients are real PDF gradients");
    }

    [Fact]
    public void The_same_label_in_svg_names_each_kind_of_drawing()
    {
        (ProjectModel project, AssetStore assets) = EverythingProject();

        string svg = new SvgExporter().ExportLabel(project.MasterLabel, Record(), assets);

        svg.Should().Contain("<linearGradient", "gradients");
        svg.Should().Contain("<clipPath", "clipped pictures");
        svg.Should().Contain("<image", "pictures");
        svg.Should().Contain("data:image/png;base64,", "pictures travel inside the file");
        svg.Should().Contain("<ellipse").And.Contain("<polygon").And.Contain("<line");
        svg.Should().Contain("stroke-dasharray", "dashed lines");
        svg.Should().Contain("opacity", "see-through objects");
        svg.Should().Contain("rotate(", "a turned object");
        svg.Should().Contain("Milk 1 L").And.Contain("1.25", "data fields are filled in");
        svg.Should().Contain("font-weight=\"bold\"").And.Contain("font-style=\"italic\"");
    }

    [Fact]
    public void Text_in_svg_is_escaped_so_the_file_stays_valid()
    {
        (ProjectModel project, _) = EverythingProject();
        var ctx = new ResolveContext { Record = new Dictionary<string, string> { ["Product"] = "Fish & <Chips> \"large\"" } };

        string svg = new SvgExporter().ExportLabel(project.MasterLabel, ctx);

        svg.Should().Contain("Fish &amp; &lt;Chips&gt;");
        svg.Should().NotContain("<Chips>");
        System.Xml.Linq.XDocument.Parse(svg).Root!.Name.LocalName.Should().Be("svg");
    }

    [Fact]
    public void A_label_with_rounded_corners_is_cut_to_its_shape_in_svg()
    {
        (ProjectModel project, _) = EverythingProject();

        string svg = new SvgExporter().ExportLabel(project.MasterLabel, Record(), shape: LabelShape.RoundRectangle, cornerRadiusMm: 3);

        svg.Should().Contain("<clipPath");
    }

    [Fact]
    public void A_barcode_on_its_own_makes_a_pdf_page_the_size_of_the_symbol()
    {
        var spec = new BarcodeSpec { Symbology = BarcodeSymbology.Code128, Data = "ABC000001", ModuleWidthMm = 0.33, BarHeightMm = 15 };

        byte[] natural = BarcodePdf.Render(spec, spec.Data);
        byte[] sized = BarcodePdf.Render(spec, spec.Data, 50, 20);

        using PdfDocument a = PdfReader.Open(new MemoryStream(natural), PdfDocumentOpenMode.Modify);
        PlacedBarcode placed = BarcodeLayout.PlaceNatural(BarcodeEngine.Encode(spec, spec.Data), spec);
        a.Pages[0].Width.Millimeter.Should().BeApproximately(placed.NaturalSizeMm.Width, 0.05);
        a.Pages[0].Height.Millimeter.Should().BeApproximately(placed.NaturalSizeMm.Height, 0.05);
        a.Info.Title.Should().Contain("ABC000001");
        Operators(a.Pages[0]).Should().Contain("re").And.Contain(o => o == "Tj" || o == "TJ", "the digits under the bars");

        using PdfDocument b = PdfReader.Open(new MemoryStream(sized), PdfDocumentOpenMode.Modify);
        b.Pages[0].Width.Millimeter.Should().BeApproximately(50, 0.05);
        b.Pages[0].Height.Millimeter.Should().BeApproximately(20, 0.05);
    }

    [Fact]
    public void A_qr_code_on_a_coloured_background_fills_the_page_behind_it()
    {
        var spec = new BarcodeSpec { Symbology = BarcodeSymbology.QrCode, Data = "hello", Background = new ColorRgba(255, 250, 200) };

        byte[] pdf = BarcodePdf.Render(spec, spec.Data);

        using PdfDocument read = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);
        read.PageCount.Should().Be(1);
        Operators(read.Pages[0]).Count(o => o == "re").Should().BeGreaterThan(10);
    }

    [Fact]
    public void Printer_pages_as_pictures_keep_each_sheet_at_its_paper_size()
    {
        byte[] sheet = TestImages.Png(21, 30);
        using var output = new MemoryStream();

        RasterPdf.Write(output, new[] { (sheet, 210.0, 297.0), (sheet, 100.0, 150.0) }, "Two sheets");

        output.CanWrite.Should().BeTrue("the caller's stream is left open");
        using PdfDocument read = PdfReader.Open(new MemoryStream(output.ToArray()), PdfDocumentOpenMode.Modify);
        read.PageCount.Should().Be(2);
        read.Info.Title.Should().Be("Two sheets");
        read.Pages[0].Width.Millimeter.Should().BeApproximately(210, 0.05);
        read.Pages[1].Height.Millimeter.Should().BeApproximately(150, 0.05);
        Operators(read.Pages[1]).Should().Contain("Do");
    }

    [Fact]
    public void Pictures_reach_svg_as_their_own_kind_of_file_with_their_size()
    {
        var assets = new AssetStore();
        string png = assets.Add(TestImages.Png(8, 6), ".png");
        string other = assets.Add(new byte[] { 1, 2, 3 }, ".xyz");
        var source = new ExportImageSource(assets);

        var picture = (SvgEmbeddedImage)source.Get(png)!;
        picture.PixelWidth.Should().Be(8);
        picture.PixelHeight.Should().Be(6);
        picture.DataUri.Should().StartWith("data:image/png;base64,");
        source.Get(png).Should().BeSameAs(picture, "each picture is read once");

        var unknown = (SvgEmbeddedImage)source.Get(other)!;
        unknown.DataUri.Should().StartWith("data:application/octet-stream;base64,");
        unknown.PixelWidth.Should().Be(1, "a picture whose size cannot be read still has a size");

        source.Get("not-there").Should().BeNull();
        source.Get(string.Empty).Should().BeNull();
    }
}
