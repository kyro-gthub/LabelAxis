using FluentAssertions;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Formtec;

/// <summary>The other makers' sheets (templates/sheets), read from Word's label database.</summary>
public sealed class MakerCatalogueTests
{
    private static readonly TemplateStore Store = TemplateStore.CreateWithBuiltIns();

    [Theory]
    [InlineData("Avery", 1000)]
    [InlineData("Avery Zweckform", 50)]
    [InlineData("HERMA", 500)]
    [InlineData("APLI", 200)]
    [InlineData("Agipa", 150)]
    [InlineData("Decadry", 50)]
    [InlineData("Sigel", 80)]
    [InlineData("OnlineLabels", 80)]
    [InlineData("TANEX", 100)]
    [InlineData("3M", 300)]
    [InlineData("Brother", 10)]
    [InlineData("DYMO", 10)]
    [InlineData("Formtec", 150)]
    public void Each_maker_has_its_sheets(string brand, int atLeast)
    {
        Store.All.Count(t => t.Brand == brand).Should().BeGreaterThanOrEqualTo(atLeast);
        Store.Brands().Should().Contain(brand);
    }

    [Fact]
    public void Avery_L7160_has_the_makers_own_figures_and_its_old_id_still_finds_it()
    {
        // It was an estimate before (7.25 mm in, 2.5 mm apart, square corners).
        LabelTemplate t = Store.Get("avery-L7160")!;
        t.Id.Should().Be("avery-l7160");
        t.Grid.Columns.Should().Be(3);
        t.Grid.Rows.Should().Be(7);
        t.Grid.MarginLeftMm.Should().BeApproximately(7.2, 1e-6);
        t.Grid.MarginTopMm.Should().BeApproximately(15.15, 1e-6);
        t.Grid.HGapMm.Should().BeApproximately(2.54, 1e-6);
        t.Grid.CornerRadiusMm.Should().BeApproximately(3, 1e-6);
        t.Grid.LabelShape.Should().Be(LabelShape.RoundRectangle);
    }

    [Fact]
    public void Sheets_in_inches_and_centimetres_come_out_in_millimetres()
    {
        // OnlineLabels OL100: 4" × 1.33", 14 to a Letter sheet.
        LabelTemplate ol = Store.Get("onlinelabels-ol100")!;
        ol.EffectivePage.WidthMm.Should().BeApproximately(215.9, 0.01);
        ol.Grid.LabelWidthMm.Should().BeApproximately(101.6, 0.01);
        ol.Grid.Count.Should().Be(14);

        // HERMA 4103: 63.5 × 38.1 mm, 21 to an A4 sheet, 2 mm corners.
        LabelTemplate herma = Store.Get("herma-4103")!;
        RectMm last = TemplateGeometry.SlotRect(herma, herma.Grid.Count - 1);
        last.Right.Should().BeApproximately(7.21 + (3 * 63.5) + (2 * 2.54), 1e-6);
        last.Bottom.Should().BeApproximately(15.15 + (7 * 38.1), 1e-6);
        herma.Grid.CornerRadiusMm.Should().BeApproximately(2, 1e-6);
    }

    [Fact]
    public void A_roll_label_is_a_page_of_its_own_size()
    {
        // DYMO 30252 / 30320 address label, 1.098" × 3.5".
        LabelTemplate t = Store.Get("dymo-30252-30320")!;
        t.Grid.Count.Should().Be(1);
        t.EffectivePage.WidthMm.Should().BeApproximately(t.Grid.LabelWidthMm, 1e-6);
        t.EffectivePage.HeightMm.Should().BeApproximately(88.9, 0.01);
    }

    [Fact]
    public void Codes_are_found_by_brand_and_code_and_ids_are_unique()
    {
        Store.All.Select(t => t.Id.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
        Store.All.Where(t => t.Brand == "3M").Should().NotContain(t => t.ProductCode.Contains('®'),
            "Post-it® names keep the mark in the notes, not in the code");
        Store.Search(new TemplateQuery { Text = "L7163" }).Should().Contain(t => t.Brand == "Avery" && t.ProductCode == "L7163");
    }
}
