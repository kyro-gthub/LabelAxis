using FluentAssertions;
using LabelMaker.Core.Formtec;
using Xunit;

namespace LabelMaker.Tests.Formtec;

public sealed class CustomSheetTests
{
    [Fact]
    public void Fits_as_many_stickers_as_the_paper_holds_inside_the_margins()
    {
        // A4 minus 10 mm margins is 190 × 277 mm; 50 × 25 mm stickers with 2 mm gaps.
        var spec = new CustomSheetSpec { LabelWidthMm = 50, LabelHeightMm = 25 };

        CustomSheet.Fit(spec).Should().Be((3, 10));

        LabelTemplate t = CustomSheet.Build(spec, "custom-1");
        t.Grid.Columns.Should().Be(3);
        t.Grid.Rows.Should().Be(10);
        t.Grid.MarginLeftMm.Should().Be(10);
        t.Grid.MarginTopMm.Should().Be(10);
        t.IsUserDefined.Should().BeTrue();
        t.Brand.Should().Be(CustomSheet.Brand);
        TemplateValidator.Validate(t).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Centring_puts_the_block_of_stickers_in_the_middle_of_the_paper()
    {
        var spec = new CustomSheetSpec { LabelWidthMm = 50, LabelHeightMm = 25, Columns = 2, Rows = 2, CenterOnPaper = true };

        LabelTemplate t = CustomSheet.Build(spec, "custom-2");

        // 2 × 50 + 2 = 102 mm wide on 210 mm paper; 2 × 25 + 2 = 52 mm tall on 297 mm.
        t.Grid.MarginLeftMm.Should().BeApproximately(54, 0.01);
        t.Grid.MarginTopMm.Should().BeApproximately(122.5, 0.01);
    }

    [Fact]
    public void Landscape_turns_the_paper_before_fitting()
    {
        var spec = new CustomSheetSpec { LabelWidthMm = 60, LabelHeightMm = 40, Orientation = PageOrientation.Landscape };

        // Landscape A4 is 297 × 210 mm, so 277 × 190 mm inside the margins.
        CustomSheet.Fit(spec).Should().Be((4, 4));
    }

    [Fact]
    public void A_sticker_too_big_for_the_paper_or_a_grid_that_runs_off_it_is_refused_with_a_reason()
    {
        Action tooBig = () => CustomSheet.Build(new CustomSheetSpec { LabelWidthMm = 300, LabelHeightMm = 20 }, "x");
        tooBig.Should().Throw<InvalidOperationException>().WithMessage("*does not fit*");

        Action offPaper = () => CustomSheet.Build(new CustomSheetSpec { LabelWidthMm = 50, LabelHeightMm = 25, Columns = 5 }, "x");
        offPaper.Should().Throw<InvalidOperationException>().WithMessage("*runs off*");
    }

    [Fact]
    public void A_round_cornered_sheet_reads_back_into_the_form_that_made_it()
    {
        var spec = new CustomSheetSpec
        {
            Name = "Jar lids", LabelWidthMm = 40, LabelHeightMm = 30, Shape = LabelShape.RoundRectangle, CornerRadiusMm = 4,
            Paper = PageSize.Letter, MarginTopMm = 12, MarginLeftMm = 8, HGapMm = 3, VGapMm = 3, Columns = 4, Rows = 6,
        };

        CustomSheetSpec back = CustomSheet.ToSpec(CustomSheet.Build(spec, "custom-3"));

        back.Name.Should().Be("Jar lids");
        back.LabelWidthMm.Should().Be(40);
        back.Shape.Should().Be(LabelShape.RoundRectangle);
        back.CornerRadiusMm.Should().Be(4);
        back.Paper.Should().Be(PageSize.Letter);
        back.MarginTopMm.Should().Be(12);
        back.MarginLeftMm.Should().Be(8);
        back.Columns.Should().Be(4);
        back.Rows.Should().Be(6);
        CustomSheet.Build(back, "custom-3").Grid.Should().Be(CustomSheet.Build(spec, "custom-3").Grid);
    }

    [Fact]
    public void New_ids_never_clash_with_existing_templates()
    {
        var taken = new HashSet<string>();
        for (int i = 0; i < 50; i++)
        {
            string id = CustomSheet.NewId(taken.Contains);
            taken.Add(id).Should().BeTrue();
            id.Should().StartWith("custom-");
        }
    }
}
