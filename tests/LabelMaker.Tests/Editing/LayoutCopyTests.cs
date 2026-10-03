using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Editing;

public sealed class LayoutCopyTests
{
    private static readonly HashSet<ObjectKind> Everything = Enum.GetValues<ObjectKind>().ToHashSet();

    private sealed class Measurer : ITextMeasurer
    {
        public double MeasureWidthMm(string text, string fontFamily, double fontSizePt, bool bold, bool italic) =>
            text.Length * UnitConverter.PointsToMm(fontSizePt) * 0.6;

        public double LineHeightMm(string fontFamily, double fontSizePt) => UnitConverter.PointsToMm(fontSizePt) * 1.2;
    }

    private static LabelDocument Page(double shift)
    {
        var doc = new LabelDocument();
        doc.Objects.Add(new TextObject { Name = "Title", Content = RichTextContent.FromPlainText("Apples"), Bounds = new RectMm(2 + shift, 2, 30, 6) });
        doc.Objects.Add(new TextObject { Name = "Price", Content = RichTextContent.FromPlainText("2.50"), Bounds = new RectMm(2 + shift, 10, 15, 6) });
        doc.Objects.Add(new BarcodeObject { Name = "Code", Spec = new BarcodeSpec { Data = "123" }, Bounds = new RectMm(20 + shift, 10, 18, 9) });
        return doc;
    }

    private static (ProjectModel Project, LabelDocument Current, LabelDocument Other) Project()
    {
        var p = new ProjectModel { MasterLabel = Page(0) };
        LabelDocument other = Page(5);
        p.Pages.Add(new LabelPage { Name = "Page 1", Document = other });
        return (p, p.MasterLabel, other);
    }

    [Fact]
    public void Same_name_moves_each_object_to_where_its_namesake_is_on_this_page()
    {
        (ProjectModel p, LabelDocument current, LabelDocument other) = Project();

        var pairs = LayoutCopy.Pairs(p, current, current.Objects, new[] { 0, 1 }, Everything, LayoutMatch.SameName);
        pairs.Should().HaveCount(3, "the source page itself is skipped");

        IUndoableCommand command = LayoutCopy.Build(pairs, LayoutParts.Position, measurer: null)!;
        command.Do();
        other.Objects.Select(o => o.Bounds.TopLeft).Should().Equal(current.Objects.Select(o => o.Bounds.TopLeft));

        command.Undo();
        other.Objects[0].Bounds.X.Should().Be(7);
    }

    [Fact]
    public void Only_the_chosen_kinds_and_only_the_position_unless_size_is_ticked()
    {
        (ProjectModel p, LabelDocument current, LabelDocument other) = Project();
        other.Objects[2].Bounds = new RectMm(0, 0, 30, 15);

        var barcodes = LayoutCopy.Pairs(p, current, current.Objects, new[] { 1 }, new HashSet<ObjectKind> { ObjectKind.Barcode }, LayoutMatch.SameName);
        barcodes.Should().ContainSingle().Which.Target.Should().BeSameAs(other.Objects[2]);

        LayoutCopy.Build(barcodes, LayoutParts.Position, null)!.Do();
        other.Objects[2].Bounds.Should().Be(new RectMm(20, 10, 30, 15));
        other.Objects[0].Bounds.X.Should().Be(7, "text was not chosen");

        LayoutCopy.Build(barcodes, LayoutParts.Size, null)!.Do();
        other.Objects[2].Bounds.Should().Be(new RectMm(20, 10, 18, 9));
    }

    [Fact]
    public void Same_order_matches_the_first_text_to_the_first_text_whatever_the_names()
    {
        (ProjectModel p, LabelDocument current, LabelDocument other) = Project();
        other.Objects[0].Name = "Heading";
        other.Objects[1].Name = "Cost";

        LayoutCopy.Pairs(p, current, current.Objects, new[] { 1 }, Everything, LayoutMatch.SameName).Should().ContainSingle("only the barcode keeps its name");

        var byOrder = LayoutCopy.Pairs(p, current, current.Objects, new[] { 1 }, Everything, LayoutMatch.SameOrder);
        byOrder.Select(x => x.Target).Should().Equal(other.Objects);
    }

    [Fact]
    public void Only_the_selected_objects_move_and_locked_ones_never_do()
    {
        (ProjectModel p, LabelDocument current, LabelDocument other) = Project();
        other.Objects[1].Locked = true;

        LayoutCopy.Pairs(p, current, new[] { current.Objects[0], current.Objects[1] }, new[] { 1 }, Everything, LayoutMatch.SameName)
            .Select(x => x.Target).Should().Equal(other.Objects[0]);
    }

    [Fact]
    public void Rotation_is_copied_when_asked_and_nothing_to_change_gives_no_command()
    {
        (ProjectModel p, LabelDocument current, LabelDocument other) = Project();
        current.Objects[2].RotationDegrees = 90;
        var pairs = LayoutCopy.Pairs(p, current, new[] { current.Objects[2] }, new[] { 1 }, Everything, LayoutMatch.SameName);

        LayoutCopy.Build(pairs, LayoutParts.Rotation, null)!.Do();
        other.Objects[2].RotationDegrees.Should().Be(90);
        other.Objects[2].Bounds.X.Should().Be(25, "only the rotation was asked for");

        LayoutCopy.Build(pairs, LayoutParts.Rotation, null).Should().BeNull();
    }

    [Fact]
    public void A_grow_to_fit_text_box_made_narrower_grows_taller_to_keep_its_text()
    {
        (ProjectModel p, LabelDocument current, LabelDocument other) = Project();
        current.Objects[0].Bounds = new RectMm(2, 2, 6, 6);
        ((TextObject)other.Objects[0]).Content = RichTextContent.FromPlainText("Fresh red apples");
        var pairs = LayoutCopy.Pairs(p, current, new[] { current.Objects[0] }, new[] { 1 }, Everything, LayoutMatch.SameName);

        LayoutCopy.Build(pairs, LayoutParts.Position | LayoutParts.Size, new Measurer())!.Do();

        other.Objects[0].Bounds.Width.Should().Be(6);
        other.Objects[0].Bounds.Height.Should().BeGreaterThan(6);
    }
}
