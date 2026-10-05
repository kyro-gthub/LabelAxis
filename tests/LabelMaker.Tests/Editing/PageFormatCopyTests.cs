using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Editing;

public sealed class PageFormatCopyTests
{
    private static readonly HashSet<ObjectKind> Everything = Enum.GetValues<ObjectKind>().ToHashSet();

    private static ProjectModel Project(out LabelDocument current, out LabelDocument other)
    {
        current = new LabelDocument();
        current.Objects.Add(new TextObject
        {
            Name = "Title", Content = RichTextContent.FromPlainText("Apples"), Bounds = new RectMm(2, 2, 30, 6),
            FontFamily = "Tahoma", FontSizePt = 14, Bold = true, Foreground = new ColorRgba(200, 0, 0), AutoFit = TextAutoFit.None,
        });
        current.Objects.Add(new BarcodeObject
        {
            Name = "Code", Bounds = new RectMm(5, 12, 30, 12), RotationDegrees = 90, AutoSize = false,
            Spec = new BarcodeSpec { Data = "111", Symbology = BarcodeSymbology.Code39, BarHeightMm = 9, HumanReadable = HumanReadablePosition.None },
        });
        current.Objects.Add(new BarcodeObject
        {
            Name = "Link", Bounds = new RectMm(40, 2, 15, 15),
            Spec = new BarcodeSpec { Data = "https://example.com/a", Symbology = BarcodeSymbology.QrCode },
        });

        other = new LabelDocument();
        other.Objects.Add(new TextObject { Name = "Title", Content = RichTextContent.FromPlainText("Pears"), Bounds = new RectMm(9, 9, 20, 5), AutoFit = TextAutoFit.None });
        // Same kind, other names: matched by order when no name fits. The QR code comes first on
        // this page but must never be taken for the line barcode.
        other.Objects.Add(new BarcodeObject { Name = "QR", Bounds = new RectMm(1, 1, 10, 10), Spec = new BarcodeSpec { Data = "https://example.com/b", Symbology = BarcodeSymbology.QrCode } });
        other.Objects.Add(new BarcodeObject { Name = "Barcode 2", Bounds = new RectMm(1, 30, 20, 8), Spec = new BarcodeSpec { Data = "222" } });

        var project = new ProjectModel { MasterLabel = current };
        project.Pages.Add(new LabelPage { Name = "Page 2", Document = other });
        return project;
    }

    private static IUndoableCommand Build(ProjectModel p, LabelDocument current, LayoutMatch match, PageFormatParts parts)
    {
        var pairs = LayoutCopy.Pairs(p, current, current.Objects, new[] { 0, 1 }, Everything, match);
        var missing = LayoutCopy.Unmatched(p, current, current.Objects, new[] { 0, 1 }, Everything, match);
        return PageFormatCopy.Build(pairs, missing, PrintablePages.All(p).ToList(), parts, TextStyleParts.All, measurer: null)!;
    }

    [Fact]
    public void Each_object_takes_place_size_and_look_but_keeps_its_own_words_and_value()
    {
        ProjectModel p = Project(out LabelDocument current, out LabelDocument other);
        string[] ids = other.Objects.Select(o => o.Id).ToArray();

        IUndoableCommand command = Build(p, current, LayoutMatch.NameThenOrder,
            PageFormatParts.Position | PageFormatParts.Size | PageFormatParts.Rotation | PageFormatParts.Look);
        command.Do();

        var title = (TextObject)other.Objects[0];
        title.Content.PlainText.Should().Be("Pears");
        title.Bounds.Should().Be(new RectMm(2, 2, 30, 6));
        (title.FontFamily, title.FontSizePt, title.Bold, title.Foreground).Should().Be(("Tahoma", 14, true, new ColorRgba(200, 0, 0)));

        var qr = (BarcodeObject)other.Objects[1];
        qr.Spec.Symbology.Should().Be(BarcodeSymbology.QrCode);
        qr.Spec.Data.Should().Be("https://example.com/b");
        qr.Bounds.Should().Be(new RectMm(40, 2, 15, 15), "the QR code matched the QR code, not the line barcode");

        var code = (BarcodeObject)other.Objects[2];
        code.Name.Should().Be("Barcode 2");
        code.Spec.Data.Should().Be("222");
        (code.Spec.Symbology, code.Spec.BarHeightMm, code.Spec.HumanReadable, code.RotationDegrees, code.AutoSize)
            .Should().Be((BarcodeSymbology.Code39, 9, HumanReadablePosition.None, 90, false));
        code.Bounds.Should().Be(new RectMm(5, 12, 30, 12));
        other.Objects.Select(o => o.Id).Should().Equal(ids, "the objects stay themselves, in the same order");

        command.Undo();
        ((TextObject)other.Objects[0]).FontFamily.Should().Be("Segoe UI");
        ((BarcodeObject)other.Objects[2]).Spec.Symbology.Should().Be(BarcodeSymbology.Code128);
        other.Objects[2].Bounds.Should().Be(new RectMm(1, 30, 20, 8));
    }

    [Fact]
    public void Only_the_position_when_only_that_is_ticked()
    {
        ProjectModel p = Project(out LabelDocument current, out LabelDocument other);
        Build(p, current, LayoutMatch.NameThenOrder, PageFormatParts.Position).Do();

        var title = (TextObject)other.Objects[0];
        title.Bounds.Should().Be(new RectMm(2, 2, 20, 5));
        title.FontFamily.Should().Be("Segoe UI");
        ((BarcodeObject)other.Objects[2]).Spec.Symbology.Should().Be(BarcodeSymbology.Code128);
    }

    [Fact]
    public void Matching_by_name_only_leaves_differently_named_objects_alone_and_can_add_them()
    {
        ProjectModel p = Project(out LabelDocument current, out LabelDocument other);
        LayoutCopy.Pairs(p, current, current.Objects, new[] { 1 }, Everything, LayoutMatch.SameName).Should().ContainSingle();

        IUndoableCommand command = Build(p, current, LayoutMatch.SameName, PageFormatParts.Position | PageFormatParts.AddMissing);
        command.Do();
        other.Objects.Should().HaveCount(5, "the barcode and QR code named differently were added");
        other.Objects.Skip(3).Select(o => o.Name).Should().Equal("Code", "Link");
        ((BarcodeObject)other.Objects[3]).Spec.Data.Should().Be("111", "an added object is a full copy");

        command.Undo();
        other.Objects.Should().HaveCount(3);
    }

    [Fact]
    public void A_barcode_type_that_cannot_hold_the_pages_value_is_not_forced_on_it()
    {
        var source = new BarcodeObject { Spec = new BarcodeSpec { Data = "5901234123457", Symbology = BarcodeSymbology.Ean13, BarHeightMm = 20 } };
        var target = new BarcodeObject { Spec = new BarcodeSpec { Data = "ABC-1", Symbology = BarcodeSymbology.Code128 } };

        var styled = (BarcodeObject)PageFormatCopy.Styled(source, target, PageFormatParts.Look, TextStyleParts.All, measurer: null);
        styled.Spec.Symbology.Should().Be(BarcodeSymbology.Code128);
        styled.Spec.BarHeightMm.Should().Be(20);
        styled.Spec.Data.Should().Be("ABC-1");
    }

    [Fact]
    public void Nothing_to_change_gives_no_undo_step()
    {
        ProjectModel p = Project(out LabelDocument current, out _);
        var pairs = LayoutCopy.Pairs(p, current, current.Objects, new[] { 0 }, Everything, LayoutMatch.NameThenOrder);
        PageFormatCopy.Build(pairs, Array.Empty<(LabelObject, LabelDocument)>(), PrintablePages.All(p).ToList(), PageFormatParts.Position, TextStyleParts.All, null)
            .Should().BeNull("only the page copied from was ticked");
    }
}
