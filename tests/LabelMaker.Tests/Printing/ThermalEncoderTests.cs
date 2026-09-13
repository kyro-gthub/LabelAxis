using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Printing.Thermal;
using Xunit;

namespace LabelMaker.Tests.Printing;

public sealed class ThermalEncoderTests
{
    private static MonochromeImage Sample(int width, int height)
    {
        var image = new MonochromeImage(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // a border, a solid block, stripes and blank rows: every compression case
                bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                bool block = x > 10 && x < 60 && y > 5 && y < 20;
                bool stripes = y > 25 && y < 30 && x % 3 == 0;
                image[x, y] = border || block || stripes;
            }
        }

        return image;
    }

    [Fact]
    public void Zpl_graphic_decompresses_to_exactly_the_same_dots()
    {
        MonochromeImage image = Sample(203, 60);
        string zpl = Encoding.ASCII.GetString(new ZplEncoder().EncodeLabel(image, new ThermalPrintSettings(), new RollMedia(), 7.5, 1));

        Match field = Regex.Match(zpl, @"\^GFA,(\d+),(\d+),(\d+),([^\^]*)\^FS");
        field.Success.Should().BeTrue();
        int.Parse(field.Groups[1].Value).Should().Be(image.Bits.Length);
        int bytesPerRow = int.Parse(field.Groups[3].Value);
        bytesPerRow.Should().Be(26);

        byte[] decoded = ZplEncoder.Decompress(field.Groups[4].Value, bytesPerRow, image.Height);
        decoded.Should().Equal(image.Bits);
        field.Groups[4].Value.Length.Should().BeLessThan(image.Bits.Length, "the data is compressed");
    }

    [Fact]
    public void Long_runs_use_the_largest_repeat_letters()
    {
        var image = new MonochromeImage(8 * 300, 2); // 600 hex digits per row
        for (int x = 0; x < image.Width; x++)
        {
            image[x, 0] = x < 8 * 250; // 500 'F' then zeros
        }

        var sb = new StringBuilder();
        ZplEncoder.AppendCompressed(sb, image);
        string data = sb.ToString();

        ZplEncoder.Decompress(data, image.BytesPerRow, 2).Should().Equal(image.Bits);
        data.Should().StartWith("zYFjGF,"); // 419 + 81 = 500 F, then zeros to the end of the row
    }

    [Fact]
    public void Zpl_carries_the_printer_settings()
    {
        var settings = new ThermalPrintSettings
        {
            Darkness = 22,
            SpeedIps = 6,
            AfterPrint = ThermalAfterPrint.Cut,
            Rotate180 = true,
            OffsetXMm = 1,
            Dpi = 203,
        };
        string zpl = Encoding.ASCII.GetString(new ZplEncoder().EncodeLabel(
            Sample(400, 80), settings, new RollMedia { Sensing = MediaSensing.BlackMark }, 10, 3));

        zpl.Should().StartWith("^XA").And.EndWith("^XZ\n");
        zpl.Should().Contain("^PW400").And.Contain("~SD22").And.Contain("^PR6").And.Contain("^MMC")
            .And.Contain("^MNM").And.Contain("^POI").And.Contain("^FO8,0").And.Contain("^PQ3,");
    }

    [Fact]
    public void Tspl_bitmap_is_inverted_and_padding_stays_blank()
    {
        var image = new MonochromeImage(12, 2);   // 2 bytes per row, 4 spare bits
        image[0, 0] = true;
        byte[] job = new TsplEncoder().EncodeLabel(image, new ThermalPrintSettings(), new RollMedia { GapMm = 2 }, 5, 2);

        string text = Encoding.ASCII.GetString(job);
        text.Should().Contain("GAP 2 mm,0 mm").And.Contain("CLS").And.Contain("BITMAP 0,0,2,2,0,").And.EndWith("PRINT 1,2\r\n");

        int data = text.IndexOf("BITMAP 0,0,2,2,0,", StringComparison.Ordinal) + "BITMAP 0,0,2,2,0,".Length;
        job[data].Should().Be(0x7F, "the one printed dot is a 0 bit");
        job[data + 1].Should().Be(0xFF, "unused bits past the edge are white");
        job[data + 2].Should().Be(0xFF);
    }

    [Fact]
    public void Grey_pixels_threshold_or_diffuse()
    {
        byte[] midGrey = Enumerable.Repeat((byte)128, 100 * 100).ToArray();

        MonochromeImage threshold = MonochromeImage.FromGray(midGrey, 100, 100, DitherMode.Threshold, 150);
        threshold.Coverage.Should().Be(1.0);

        MonochromeImage diffused = MonochromeImage.FromGray(midGrey, 100, 100, DitherMode.Diffusion);
        diffused.Coverage.Should().BeInRange(0.4, 0.6, "half-grey diffuses to about half the dots");
    }

    [Fact]
    public void Rotating_twice_gives_the_original()
    {
        MonochromeImage image = Sample(50, 30);
        image.Rotated180().Rotated180().Bits.Should().Equal(image.Bits);
        image.Rotated180()[49, 29].Should().Be(image[0, 0]);
    }

    [Fact]
    public void Roll_templates_round_trip_through_json()
    {
        LabelTemplate roll = LabelTemplate.CreateRoll("roll-100x50", "100 × 50 roll", 100, 50, across: 2, columnGapMm: 3,
            sideMarginMm: 1.5, rollGapMm: 2.5, sensing: MediaSensing.BlackMark);

        roll.EffectivePage.WidthMm.Should().BeApproximately(206, 0.001);
        roll.EffectivePage.HeightMm.Should().Be(50);

        LabelTemplate back = TemplateJson.FromJson(TemplateJson.ToJson(roll));
        back.IsRoll.Should().BeTrue();
        back.Roll.Should().Be(new RollMedia { GapMm = 2.5, Sensing = MediaSensing.BlackMark });
        back.Grid.Should().Be(roll.Grid);
        TemplateValidator.Validate(back).HasErrors.Should().BeFalse();
    }
}
