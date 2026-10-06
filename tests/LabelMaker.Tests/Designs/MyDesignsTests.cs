using FluentAssertions;
using LabelMaker.Core.Designs;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Designs;

public sealed class MyDesignsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "labelaxis-mydesigns-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>A project of two pages: the second has a picture and a font of its own.</summary>
    private static ProjectDocument TwoPages()
    {
        ProjectModel project = ProjectModel.CreateNew(LabelTemplate.CreateRoll("roll-50x30", "50 × 30 roll", 50, 30), "Shop");
        var document = new ProjectDocument { Project = project };
        project.MasterLabel.Objects.Add(new TextObject { Content = RichTextContent.FromPlainText("Front"), FontFamily = "Arial" });

        string picture = document.Assets.Add(new byte[] { 1, 2, 3, 4 }, ".png");
        string unused = document.Assets.Add(new byte[] { 9, 9, 9 }, ".png");
        string font = document.Assets.Add(new byte[] { 5, 6, 7 }, ".ttf");
        project.Fonts.Add(new ProjectFont { Family = "Shop Sans", AssetId = font, FileName = "ShopSans.ttf" });
        project.Fonts.Add(new ProjectFont { Family = "Other Font", AssetId = unused, FileName = "Other.ttf" });
        var page = new LabelPage { Name = "Price tag", Document = new LabelDocument { Name = "Price tag", SizeMm = new SizeMm(50, 30) } };
        page.Document.Objects.Add(new TextObject { Content = RichTextContent.FromPlainText("{Price}"), FontFamily = "Shop Sans" });
        page.Document.Objects.Add(new ImageObject { AssetId = picture, Bounds = new RectMm(1, 1, 10, 10) });
        project.Pages.Add(page);
        project.Variables.Add(new CustomVariable { Name = "Price", Value = "1.99" });
        return document;
    }

    [Fact]
    public void A_saved_page_is_listed_with_its_sheet_and_opens_as_a_new_project()
    {
        ProjectDocument source = TwoPages();
        var mine = new MyDesigns(_folder);
        LabelDesign saved = mine.Save(source, source.Project.Pages[0].Document, "Bakery price tag", "Weekly", new byte[] { 0x89, 0x50 });

        saved.IsMine.Should().BeTrue();
        saved.Group.Should().Be(MyDesigns.Group);
        saved.Size.IsThermal.Should().BeTrue();
        saved.Size.WidthMm.Should().Be(50);
        mine.List().Should().ContainSingle().Which.Name.Should().Be("Bakery price tag");
        mine.List()[0].Description.Should().Be("Weekly");
        mine.List()[0].ThumbnailPng.Should().Equal(0x89, 0x50);

        ProjectDocument opened = MyDesigns.Open(saved);
        opened.FilePath.Should().BeNull("it is a new project, not the design file");
        opened.Project.Pages.Should().BeEmpty();
        opened.Project.MasterLabel.Objects.Should().HaveCount(2);
        opened.Project.MasterLabel.Objects.OfType<TextObject>().Single().Content.PlainText.Should().Be("{Price}");
        opened.Project.Template!.Id.Should().Be("roll-50x30");
        opened.Project.Variables.Should().ContainSingle(v => v.Name == "Price");
        opened.Project.Metadata.Id.Should().NotBe(source.Project.Metadata.Id);
    }

    [Fact]
    public void Only_the_pages_own_pictures_and_fonts_go_with_it()
    {
        ProjectDocument source = TwoPages();
        var mine = new MyDesigns(_folder);
        ProjectDocument opened = MyDesigns.Open(mine.Save(source, source.Project.Pages[0].Document, "Tag"));

        opened.Project.Fonts.Should().ContainSingle().Which.Family.Should().Be("Shop Sans");
        opened.Assets.All.Should().HaveCount(2, "the picture and the one font file, not the unused file");
    }

    [Fact]
    public void Saving_the_same_name_replaces_it_and_rename_and_delete_work()
    {
        ProjectDocument source = TwoPages();
        var mine = new MyDesigns(_folder);
        mine.Save(source, source.Project.MasterLabel, "Tag");
        LabelDesign second = mine.Save(source, source.Project.Pages[0].Document, "tag ");
        mine.List().Should().ContainSingle();
        MyDesigns.Open(second).Project.MasterLabel.Objects.Should().HaveCount(2, "the second save replaced the first");

        mine.Save(source, source.Project.MasterLabel, "Shelf / edge?");
        mine.List().Should().HaveCount(2, "a name with characters Windows refuses still saves");

        LabelDesign renamed = mine.Rename(mine.Find("Tag")!, "Price tag", "For the bakery");
        renamed.Name.Should().Be("Price tag");
        mine.Find("Price tag")!.Description.Should().Be("For the bakery");
        mine.Find("Tag").Should().BeNull();

        MyDesigns.Delete(renamed);
        mine.List().Should().ContainSingle().Which.Name.Should().Be("Shelf / edge?");
        new MyDesigns(Path.Combine(_folder, "missing")).List().Should().BeEmpty();
    }
}
