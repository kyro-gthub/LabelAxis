using FluentAssertions;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Units;

public sealed class UnitConversionTests
{
    [Theory]
    [InlineData(1.0, Unit.Inch, 25.4)]
    [InlineData(1.0, Unit.Cm, 10.0)]
    [InlineData(72.0, Unit.Point, 25.4)]
    [InlineData(1.0, Unit.Mm, 1.0)]
    public void Converts_to_millimetres(double value, Unit unit, double expectedMm)
    {
        UnitConverter.ToMillimetres(value, unit).Should().BeApproximately(expectedMm, 1e-9);
    }

    [Fact]
    public void Pixels_require_a_dpi()
    {
        Action act = () => UnitConverter.ToMillimetres(96, Unit.Pixel);
        act.Should().Throw<InvalidOperationException>();

        UnitConverter.ToMillimetres(300, Unit.Pixel, dpi: 300).Should().BeApproximately(25.4, 1e-9);
    }

    [Fact]
    public void Mm_to_device_pixels_is_exact_at_300_dpi()
    {
        UnitConverter.MmToDevicePixels(25.4, 300).Should().BeApproximately(300, 1e-9);
        UnitConverter.MmToDevicePixels(40.0, 600).Should().BeApproximately(944.881889, 1e-4);
    }

    [Fact]
    public void Mm_to_diu_uses_96_per_inch()
    {
        UnitConverter.MmToDiu(25.4).Should().BeApproximately(96.0, 1e-9);
    }

    [Theory]
    [InlineData("40 mm", 40.0)]
    [InlineData("1.5in", 38.1)]
    [InlineData("12 pt", 4.2333333)]
    [InlineData("2cm", 20.0)]
    [InlineData("15", 15.0)]
    public void Measure_parses_common_forms(string text, double expectedMm)
    {
        Measure.TryParse(text, Unit.Mm, out Measure m).Should().BeTrue();
        m.Millimetres.Should().BeApproximately(expectedMm, 1e-4);
    }

    [Fact]
    public void Measure_round_trips_through_invariant_string()
    {
        var m = Measure.FromInches(1.5);
        string s = m.ToInvariantString(Unit.Mm);
        Measure.TryParse(s, Unit.Mm, out Measure back).Should().BeTrue();
        back.Millimetres.Should().BeApproximately(38.1, 1e-4);
    }

    [Fact]
    public void Measure_equality_uses_micron_tolerance()
    {
        Measure.FromMm(40.0).Should().Be(Measure.FromMm(40.0004));
        (Measure.FromMm(40.0) == Measure.FromMm(40.01)).Should().BeFalse();
    }
}
