using System.Windows.Controls;
using FluentAssertions;
using LabelMaker.App.Views;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using Xunit;

namespace LabelMaker.App.Tests.Views;

/// <summary>The wording and choices shared by Apply page format and Remove from all pages.</summary>
public sealed class AcrossPagesTests
{
    [Fact]
    public void One_selected_object_is_named_by_its_name_or_else_its_kind()
    {
        AcrossPages.SelectionCaption(new LabelObject[] { new TextObject { Name = " Price " } })
            .Should().Be("The selected object (“Price”)");
        AcrossPages.SelectionCaption(new LabelObject[] { new BarcodeObject() })
            .Should().Be("The selected object (Barcode)");
    }

    [Fact]
    public void Several_selected_objects_are_counted() =>
        AcrossPages.SelectionCaption(new LabelObject[] { new TextObject(), new ShapeObject() })
            .Should().Be("The 2 selected objects");

    [Fact]
    public void A_page_line_counts_one_object_or_several()
    {
        AcrossPages.ObjectCount("2.  Bread", 1).Should().Be("2.  Bread  ·  1 object");
        AcrossPages.ObjectCount("2.  Bread", 0).Should().Be("2.  Bread  ·  0 objects");
    }

    [Fact]
    public void Matching_by_name_suggests_matching_by_order_when_nothing_matches()
    {
        AcrossPages.NothingMatches(LayoutMatch.SameName).Should().Contain("Try matching by order");
        AcrossPages.NothingMatches(LayoutMatch.SameOrder).Should().Be("Nothing to match on the ticked pages.");
    }

    [Fact]
    public void The_match_choice_reads_the_ticked_option() => Sta.Run(() =>
    {
        var sameOrder = new RadioButton();
        var sameName = new RadioButton();
        AcrossPages.Match(sameOrder, sameName).Should().Be(LayoutMatch.NameThenOrder);

        sameName.IsChecked = true;
        AcrossPages.Match(sameOrder, sameName).Should().Be(LayoutMatch.SameName);

        sameOrder.IsChecked = true;
        AcrossPages.Match(sameOrder, sameName).Should().Be(LayoutMatch.SameOrder);
    });
}
