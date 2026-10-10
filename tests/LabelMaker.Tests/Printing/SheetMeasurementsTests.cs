using FluentAssertions;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Printing;
using Xunit;

namespace LabelMaker.Tests.Printing;

public sealed class SheetMeasurementsTests
{
    private static readonly TemplateStore Store = TemplateStore.CreateWithBuiltIns();

    [Fact]
    public void Butted_sheet_reports_margins_on_all_four_sides_and_no_gaps()
    {
        SheetMeasurements m = SheetMeasurements.From(Store.Get("formtec-gs-1065")!);

        m.LabelWidthMm.Should().BeApproximately(38, 1e-6);
        m.LabelHeightMm.Should().BeApproximately(21.15, 1e-6);
        m.MarginLeftMm.Should().BeApproximately(10, 1e-6);
        m.MarginTopMm.Should().BeApproximately(11, 1e-6);
        m.MarginRightMm.Should().BeApproximately(10, 1e-6);
        m.MarginBottomMm.Should().BeApproximately(297 - 11 - (13 * 21.15), 1e-6);
        m.HGapMm.Should().BeApproximately(0, 1e-6);
        m.VGapMm.Should().BeApproximately(0, 1e-6);
        m.Describe().Should().Contain("no gaps");
    }

    [Fact]
    public void Gaps_and_pitch_are_measured_between_neighbouring_labels()
    {
        SheetMeasurements m = SheetMeasurements.From(Store.Get("formtec-gs-1189")!);

        m.HGapMm.Should().BeApproximately(3, 1e-6);
        m.HPitchMm.Should().BeApproximately(28, 1e-6);
        m.MarginRightMm.Should().BeApproximately(210 - 8.5 - (7 * 25) - (6 * 3), 1e-6);
        m.Describe().Should().Contain("gaps 3 across");
    }

    [Fact]
    public void Calibration_and_scale_are_included_so_the_numbers_describe_the_printed_sheet()
    {
        LabelTemplate t = Store.Get("formtec-gs-1065")!;
        var calibration = new PrinterCalibration { PrinterName = "Office", OffsetXMm = 1.5, OffsetYMm = -0.5 };
        var mapper = new PrintCoordinateMapper(SheetMedia.FullBleed(210, 297), calibration);

        SheetMeasurements m = SheetMeasurements.From(t, mapper);

        m.MarginLeftMm.Should().BeApproximately(11.5, 1e-6);
        m.MarginRightMm.Should().BeApproximately(8.5, 1e-6);
        m.MarginTopMm.Should().BeApproximately(10.5, 1e-6);
    }

    [Theory]
    [InlineData(-0.0001, "0")]
    [InlineData(-0.004, "0")]
    [InlineData(0.004, "0")]
    [InlineData(2.5, "2.5")]
    [InlineData(-1.25, "-1.25")]
    public void A_length_that_rounds_to_nothing_reads_0_not_minus_0(double mm, string shown) =>
        SheetMeasurements.Mm(mm).Should().Be(shown);
}
