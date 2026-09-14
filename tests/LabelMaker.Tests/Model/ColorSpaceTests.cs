using FluentAssertions;
using LabelMaker.Core.Model;
using Xunit;

namespace LabelMaker.Tests.Model;

public sealed class ColorSpaceTests
{
    [Theory]
    [InlineData("#FF0000", 0, 1, 1)]
    [InlineData("#00FF00", 120, 1, 1)]
    [InlineData("#0000FF", 240, 1, 1)]
    [InlineData("#808080", 0, 0, 0.50196)]
    [InlineData("#44076A", 277.0, 0.934, 0.4157)]
    public void Converts_to_hsv(string hex, double h, double s, double v)
    {
        (double H, double S, double V) hsv = ColorSpace.ToHsv(ColorRgba.Parse(hex));

        hsv.H.Should().BeApproximately(h, 0.5);
        hsv.S.Should().BeApproximately(s, 0.005);
        hsv.V.Should().BeApproximately(v, 0.005);
    }

    [Fact]
    public void Every_colour_survives_a_round_trip()
    {
        var random = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            var c = new ColorRgba((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            (double h, double s, double v) = ColorSpace.ToHsv(c);
            ColorSpace.FromHsv(h, s, v, c.A).Should().Be(c);
        }
    }
}
