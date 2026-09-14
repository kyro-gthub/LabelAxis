using FluentAssertions;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Editing;

public sealed class PastePlacementTests
{
    private static List<LabelObject> Design()
    {
        const string group = "g1";
        return new List<LabelObject>
        {
            new ShapeObject { Bounds = new RectMm(1, 1, 60, 30) },
            new TextObject { Bounds = new RectMm(4, 3, 40, 8), FontSizePt = 14, GroupId = group, Content = RichTextContent.FromPlainText("Title") },
            new TextObject { Bounds = new RectMm(4, 12, 20, 8), FontSizePt = 12, GroupId = group, Content = RichTextContent.FromPlainText("Price") },
        };
    }

    [Fact]
    public void Pasting_onto_another_label_keeps_positions_and_regroups_the_copies()
    {
        List<LabelObject> source = Design();
        var target = new LabelDocument { SizeMm = new SizeMm(99, 38) };

        IReadOnlyList<LabelObject> pasted = PastePlacement.Place(source, target);

        pasted.Select(o => o.Bounds).Should().Equal(source.Select(o => o.Bounds));
        pasted.Select(o => o.Id).Should().NotIntersectWith(source.Select(o => o.Id));
        pasted[1].GroupId.Should().NotBeNull().And.Be(pasted[2].GroupId).And.NotBe("g1");
        pasted[0].GroupId.Should().BeNull();
    }

    [Fact]
    public void Pasting_onto_the_same_label_steps_off_the_originals()
    {
        List<LabelObject> source = Design();
        var target = new LabelDocument { SizeMm = new SizeMm(99, 38), Objects = source };

        IReadOnlyList<LabelObject> pasted = PastePlacement.Place(source, target);

        pasted[1].Bounds.X.Should().BeApproximately(source[1].Bounds.X + PastePlacement.CascadeMm, 1e-9);
        (pasted[2].Bounds.Y - pasted[1].Bounds.Y).Should().BeApproximately(source[2].Bounds.Y - source[1].Bounds.Y, 1e-9);
    }

    [Fact]
    public void A_block_bigger_than_the_label_shrinks_as_a_whole_and_stays_in_proportion()
    {
        List<LabelObject> source = Design();
        var target = new LabelDocument { SizeMm = new SizeMm(38, 21.15) };

        IReadOnlyList<LabelObject> pasted = PastePlacement.Place(source, target);

        RectMm frame = pasted[0].Bounds;
        frame.Left.Should().BeGreaterThanOrEqualTo(-1e-9);
        frame.Right.Should().BeLessThanOrEqualTo(38 + 1e-9);
        frame.Bottom.Should().BeLessThanOrEqualTo(21.15 + 1e-9);

        double scale = frame.Width / 60;
        pasted[1].Bounds.Width.Should().BeApproximately(40 * scale, 1e-9);
        ((TextObject)pasted[1]).FontSizePt.Should().BeApproximately(14 * scale, 1e-9);
        (pasted[2].Bounds.Y - pasted[1].Bounds.Y).Should().BeApproximately(9 * scale, 1e-9);
    }
}
