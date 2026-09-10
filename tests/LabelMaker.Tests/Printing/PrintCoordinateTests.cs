using FluentAssertions;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Printing;

public sealed class PrintCoordinateTests
{
    private static readonly SheetMedia A4WithBorder = new()
    {
        PageWidthMm = 210,
        PageHeightMm = 297,
        ImageableOriginXMm = 4.2,
        ImageableOriginYMm = 4.2,
        ImageableWidthMm = 201.6,
        ImageableHeightMm = 288.6,
    };

    [Fact]
    public void Without_calibration_only_the_hardware_origin_is_removed()
    {
        var mapper = new PrintCoordinateMapper(A4WithBorder);

        PointMm mapped = mapper.PageToCanvas(new PointMm(20, 30));

        mapped.X.Should().BeApproximately(20 - 4.2, 1e-9);
        mapped.Y.Should().BeApproximately(30 - 4.2, 1e-9);
    }

    [Fact]
    public void Calibration_offset_shifts_the_artwork()
    {
        var calibration = new PrinterCalibration { PrinterName = "Test", OffsetXMm = 0.8, OffsetYMm = -1.2 };
        var mapper = new PrintCoordinateMapper(A4WithBorder, calibration);

        PointMm mapped = mapper.PageToCanvas(new PointMm(20, 30));

        mapped.X.Should().BeApproximately(20 + 0.8 - 4.2, 1e-9);
        mapped.Y.Should().BeApproximately(30 - 1.2 - 4.2, 1e-9);
    }

    [Fact]
    public void Scale_correction_pivots_about_the_page_centre()
    {
        var calibration = new PrinterCalibration { PrinterName = "Test", ScaleX = 1.01, ScaleY = 0.99 };
        var mapper = new PrintCoordinateMapper(SheetMedia.FullBleed(200, 300), calibration);

        // The centre point is unmoved by a centre-pivoted scale.
        PointMm centre = mapper.PageToCanvas(new PointMm(100, 150));
        centre.X.Should().BeApproximately(100, 1e-9);
        centre.Y.Should().BeApproximately(150, 1e-9);

        PointMm corner = mapper.PageToCanvas(new PointMm(0, 0));
        corner.X.Should().BeApproximately(100 - (100 * 1.01), 1e-9);
        corner.Y.Should().BeApproximately(150 - (150 * 0.99), 1e-9);
    }

    [Fact]
    public void User_scale_defaults_to_100_percent_and_is_never_fit_to_page()
    {
        var mapper = new PrintCoordinateMapper(SheetMedia.FullBleed(210, 297));
        mapper.UserScale.Should().Be(1.0);

        RectMm label = new(10, 10, 99.1, 38.1);
        RectMm mapped = mapper.PageToCanvas(label);
        mapped.Width.Should().BeApproximately(99.1, 1e-9);
        mapped.Height.Should().BeApproximately(38.1, 1e-9);
    }

    [Fact]
    public void Fits_imageable_area_detects_a_label_in_the_unprintable_border()
    {
        var mapper = new PrintCoordinateMapper(A4WithBorder);

        mapper.FitsImageableArea(new RectMm(10, 10, 50, 20)).Should().BeTrue();
        mapper.FitsImageableArea(new RectMm(1, 1, 50, 20)).Should().BeFalse(); // starts inside the 4.2 mm border
    }

    [Fact]
    public void Template_label_size_survives_the_full_print_transform()
    {
        // A 40 x 20 mm label defined in a template must still be 40 x 20 mm after mapping (spec §41).
        var grid = new LabelGrid
        {
            Columns = 4, Rows = 12,
            LabelWidthMm = 40, LabelHeightMm = 20,
            MarginLeftMm = 8, MarginTopMm = 12, HGapMm = 2, VGapMm = 2,
        };
        var template = new LabelTemplate { Id = "t40x20", Name = "40x20", Grid = grid };
        RectMm slot = TemplateGeometry.SlotRect(template, 0);

        var mapper = new PrintCoordinateMapper(SheetMedia.FullBleed(210, 297));
        RectMm mapped = mapper.PageToCanvas(slot);

        mapped.Width.Should().BeApproximately(40, 1e-6);
        mapped.Height.Should().BeApproximately(20, 1e-6);
        UnitConverter.MmToDevicePixels(mapped.Width, 300).Should().BeApproximately(472.440945, 1e-4);
    }
}
