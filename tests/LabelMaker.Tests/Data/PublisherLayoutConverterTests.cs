using FluentAssertions;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Serialization;
using LabelMaker.Data.Publisher;
using Xunit;

namespace LabelMaker.Tests.Data;

public sealed class PublisherLayoutConverterTests
{
    private static readonly byte[] TinyPng =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
        0, 0, 0, 4, 0, 0, 0, 3, 8, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0, 0, 0, 0,
    };

    private static PubLayout TwoPageA4() => new("Flyer", 595.3, 841.9, new[]
    {
        new PubPage("Page 1", new[]
        {
            new PubShape
            {
                Kind = PubShapeKind.Text, Name = "Heading", LeftPt = 72, TopPt = 36, WidthPt = 288, HeightPt = 72, RotationDegrees = -10,
                Paragraphs = new[]
                {
                    new PubParagraph(new[] { new PubRun("Big Sale", "Arial Black", 36, true, false, false, new ColorRgba(200, 0, 0)) }, HorizontalAlignment.Center),
                    new PubParagraph(new[] { new PubRun("This weekend only", "Arial", 14, false, true, false, ColorRgba.Black) }, HorizontalAlignment.Center),
                },
                VerticalAlignment = VerticalAlignment.Middle,
                GroupKey = "g1",
            },
            new PubShape { Kind = PubShapeKind.Ellipse, LeftPt = 72, TopPt = 144, WidthPt = 144, HeightPt = 72, Fill = new ColorRgba(0, 128, 255), Line = ColorRgba.Black, LineWeightPt = 2, GroupKey = "g1" },
            new PubShape { Kind = PubShapeKind.Picture, Name = "Logo", LeftPt = 400, TopPt = 700, WidthPt = 100, HeightPt = 75, ImageBytes = TinyPng },
        }),
        new PubPage("Page 2", new[]
        {
            new PubShape { Kind = PubShapeKind.Line, LeftPt = 0, TopPt = 400, WidthPt = 595, HeightPt = 0, Line = ColorRgba.Black },
        }),
    });

    [Fact]
    public void Every_publisher_page_becomes_a_page_of_the_same_size()
    {
        ProjectDocument document = PublisherLayoutConverter.ToProject(TwoPageA4());
        ProjectModel project = document.Project;

        project.Pages.Should().HaveCount(2);
        project.Pages.Select(p => p.Name).Should().Equal("Page 1", "Page 2");
        project.Template!.Page.Name.Should().Be("A4");
        project.Template.Grid.LabelWidthMm.Should().BeApproximately(210, 0.1);
        project.Pages[1].Document.SizeMm.Height.Should().BeApproximately(297, 0.1);
        project.PrintSettings.Content.Should().Be(PrintContent.Pages);
        project.Metadata.Title.Should().Be("Flyer");
    }

    [Fact]
    public void Shapes_keep_their_position_size_rotation_and_look()
    {
        LabelDocument page = PublisherLayoutConverter.ToProject(TwoPageA4()).Project.Pages[0].Document;

        var heading = page.Objects.OfType<TextObject>().Single();
        heading.Bounds.X.Should().BeApproximately(25.4, 0.01);          // 72 pt = 1 inch
        heading.Bounds.Width.Should().BeApproximately(101.6, 0.01);
        heading.RotationDegrees.Should().Be(350);
        heading.FontFamily.Should().Be("Arial Black");
        heading.FontSizePt.Should().Be(36);
        heading.Foreground.Should().Be(new ColorRgba(200, 0, 0));
        heading.HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        heading.VerticalAlignment.Should().Be(VerticalAlignment.Middle);
        heading.Content.Paragraphs.Should().HaveCount(2);
        TextRun second = heading.Content.Paragraphs[1].Runs.Single();
        second.FontFamily.Should().Be("Arial");
        second.FontSizePt.Should().Be(14);
        second.Italic.Should().BeTrue();
        second.Bold.Should().BeFalse();

        var ellipse = page.Objects.OfType<ShapeObject>().Single();
        ellipse.Shape.Should().Be(ShapeKind.Ellipse);
        ellipse.Fill.Should().Be(new ColorRgba(0, 128, 255));
        ellipse.StrokeWidthMm.Should().BeApproximately(0.706, 0.001);

        heading.GroupId.Should().NotBeNull().And.Be(ellipse.GroupId, "they were grouped in Publisher");

        var logo = page.Objects.OfType<ImageObject>().Single();
        logo.FitMode.Should().Be(ImageFitMode.Stretch);
        logo.SourcePixelWidth.Should().Be(4);
    }

    [Fact]
    public void Pictures_are_stored_in_the_project_and_survive_saving()
    {
        ProjectDocument document = PublisherLayoutConverter.ToProject(TwoPageA4());
        string path = Path.Combine(Path.GetTempPath(), $"lm-pub-{Guid.NewGuid():N}.labelproj");
        try
        {
            ProjectFile.Save(document, path);
            ProjectDocument loaded = ProjectFile.Load(path);

            loaded.Project.Pages.Should().HaveCount(2);
            ImageObject logo = loaded.Project.Pages[0].Document.Objects.OfType<ImageObject>().Single();
            loaded.Assets.GetBytes(logo.AssetId).Should().Equal(TinyPng);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Odd_page_sizes_get_a_custom_sheet()
    {
        var layout = new PubLayout("Card", 252, 144, new[] { new PubPage("", Array.Empty<PubShape>()) });

        ProjectModel project = PublisherLayoutConverter.ToProject(layout).Project;

        project.Template!.Page.Name.Should().Be("Custom");
        project.Template.Grid.LabelWidthMm.Should().BeApproximately(88.9, 0.01);
        project.Pages[0].Name.Should().Be("Page 1");
    }

    /// <summary>A Publisher label publication: one 38 × 21.2 mm label per page, five pages.</summary>
    private static PubLayout MiniLabels(string? labelName = null) => new("Prices", 38 * 72 / 25.4, 21.2 * 72 / 25.4,
        Enumerable.Range(1, 5).Select(i => new PubPage($"Page {i}", Array.Empty<PubShape>())).ToList())
    {
        LabelName = labelName,
    };

    [Fact]
    public void Label_pages_go_on_a_label_sheet_with_labels_that_size()
    {
        TemplateStore store = TemplateStore.CreateWithBuiltIns();
        PubLayout layout = MiniLabels();
        (double w, double h) = PublisherLayoutConverter.PageSizeMm(layout);

        LabelTemplate sheet = PublisherSheet.Find(store, w, h)!;
        ProjectModel project = PublisherLayoutConverter.ToProject(layout, sheet).Project;

        sheet.Should().NotBeNull();
        sheet.IsRoll.Should().BeFalse();
        sheet.Grid.Count.Should().BeGreaterThan(1, "Publisher printed several of these pages on each sheet");
        sheet.Grid.LabelWidthMm.Should().BeApproximately(38, PublisherSheet.ToleranceMm);
        sheet.Grid.LabelHeightMm.Should().BeApproximately(21.2, PublisherSheet.ToleranceMm);
        project.Template.Should().Be(sheet);
        project.PrintSettings.Content.Should().Be(PrintContent.Pages);
        project.Pages.Should().HaveCount(5).And.OnlyContain(p => p.Document.SizeMm == sheet.LabelSizeMm);
    }

    [Fact]
    public void The_sheet_Publisher_names_wins_then_the_last_one_used()
    {
        TemplateStore store = TemplateStore.CreateWithBuiltIns();
        IReadOnlyList<LabelTemplate> sheets = PublisherSheet.Candidates(store, 38, 21.2);
        sheets.Should().HaveCountGreaterThan(1, "several brands make 38 × 21.2 mm labels");
        LabelTemplate other = sheets[^1];

        PublisherSheet.Find(store, 38, 21.2, $"{other.Brand} {other.ProductCode} Mini Address Labels").Should().Be(other);
        PublisherSheet.Find(store, 38, 21.2, null, other.Id).Should().Be(other);
        PublisherSheet.Find(store, 38, 21.2, "Some other label", null).Should().Be(sheets[0]);
    }

    [Theory]
    [InlineData(210, 297)] // an A4 flyer prints alone
    [InlineData(180, 250)] // so does a page too big to fit twice on A4
    [InlineData(500, 30)] // and a size no label sheet has
    public void Full_pages_and_unknown_sizes_keep_their_own_page(double w, double h) =>
        PublisherSheet.Find(TemplateStore.CreateWithBuiltIns(), w, h).Should().BeNull();
}
