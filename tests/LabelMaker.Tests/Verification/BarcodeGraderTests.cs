using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Verification;
using Xunit;
using Xunit.Abstractions;

namespace LabelMaker.Tests.Verification;

/// <summary>Pictures of barcodes drawn here, clean and with typical printing faults, then graded.</summary>
public sealed class BarcodeGraderTests(ITestOutputHelper output)
{
    /// <summary>Draws a barcode with bars as a picture: module size, how dark and light, ink spread, blur.</summary>
    private static GrayImage Bars(BarcodeSymbol symbol, double module = 4, byte bar = 20, byte space = 235, double spread = 0, int blur = 0,
        double? blockAtModules = null)
    {
        int quietPx = 40, width = (int)(symbol.TotalWidthModules * module) + (2 * quietPx), height = 140;
        var image = new double[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                image[x, y] = space;
            }
        }

        void Fill(double from, double to)
        {
            for (int x = (int)Math.Floor(from); x < Math.Ceiling(to); x++)
            {
                double cover = Math.Clamp(Math.Min(to, x + 1) - Math.Max(from, x), 0, 1);
                for (int y = 20; y < height - 20; y++)
                {
                    image[x, y] -= (image[x, y] - bar) * cover;
                }
            }
        }

        foreach (BarModule m in symbol.Bars.Where(b => b.IsBar))
        {
            double from = quietPx + (m.StartModule * module) - (spread / 2), to = quietPx + ((m.StartModule + m.WidthModules) * module) + (spread / 2);
            Fill(from, to);
        }

        if (blockAtModules is { } q)
        {
            // Something printed too close to the first bar.
            double firstBar = quietPx + (symbol.Bars.First(b => b.IsBar).StartModule * module);
            Fill(firstBar - (q * module) - (3 * module), firstBar - (q * module));
        }

        return ToImage(Blur(image, blur), width, height);
    }

    private static GrayImage Matrix(BarcodeSymbol symbol, double module = 6, int blur = 0)
    {
        int quiet = 6, rows = symbol.MatrixRows, cols = symbol.MatrixColumns;
        int width = (int)((cols + (2 * quiet)) * module), height = (int)((rows + (2 * quiet)) * module);
        var image = new double[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int c = (int)(x / module) - quiet, r = (int)(y / module) - quiet;
                image[x, y] = r >= 0 && r < rows && c >= 0 && c < cols && symbol.Matrix![r, c] ? 25 : 230;
            }
        }

        return ToImage(Blur(image, blur), width, height);
    }

    private static double[,] Blur(double[,] image, int radius)
    {
        if (radius <= 0)
        {
            return image;
        }

        int w = image.GetLength(0), h = image.GetLength(1);
        var result = new double[w, h];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                double sum = 0;
                int n = 0;
                for (int o = -radius; o <= radius; o++)
                {
                    for (int p = -radius; p <= radius; p++)
                    {
                        int xx = Math.Clamp(x + o, 0, w - 1), yy = Math.Clamp(y + p, 0, h - 1);
                        sum += image[xx, yy];
                        n++;
                    }
                }

                result[x, y] = sum / n;
            }
        }

        return result;
    }

    private static GrayImage ToImage(double[,] image, int width, int height)
    {
        var pixels = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = (byte)Math.Clamp(Math.Round(image[x, y]), 0, 255);
            }
        }

        return new GrayImage(pixels, width, height);
    }

    private static BarcodeSymbol Encode(BarcodeSymbology symbology, string data) =>
        BarcodeEngine.Encode(new BarcodeSpec { Symbology = symbology, Data = data }, data);

    private BarcodeQualityReport Check(GrayImage image)
    {
        BarcodeQualityReport report = BarcodeGrader.Check(image);
        output.WriteLine($"{report.Symbology} “{report.Text}”: {report.Grade} ({report.Score})");
        foreach (QualityParameter p in report.Parameters)
        {
            output.WriteLine($"   {p.Name}: {p.Value} {p.Grade}");
        }

        foreach (string a in report.Advice)
        {
            output.WriteLine("   → " + a);
        }

        return report;
    }

    [Theory]
    [InlineData(BarcodeSymbology.Code128, "ABC-12345")]
    [InlineData(BarcodeSymbology.Ean13, "590123412345")]
    [InlineData(BarcodeSymbology.Code39, "LABEL42")]
    public void A_clean_barcode_gets_an_A(BarcodeSymbology symbology, string data)
    {
        BarcodeQualityReport report = Check(Bars(Encode(symbology, data)));
        report.Found.Should().BeTrue();
        report.Text.Should().StartWith(data);
        report.Grade.Should().Be(QualityGrade.A);
        report.Advice.Should().ContainSingle().Which.Should().Contain("prints well");
    }

    [Fact]
    public void Low_contrast_lowers_the_grade_and_says_so()
    {
        BarcodeQualityReport report = Check(Bars(Encode(BarcodeSymbology.Code128, "ABC-12345"), bar: 120, space: 200));
        ((int)report.Grade).Should().BeLessThanOrEqualTo((int)QualityGrade.D);
        ((int)report.Parameters.Single(p => p.Name == "Symbol contrast").Grade!.Value).Should().BeLessThanOrEqualTo((int)QualityGrade.D);
        report.Advice.Should().Contain(a => a.Contains("Low contrast", StringComparison.Ordinal));
    }

    [Fact]
    public void Ink_spread_is_measured_and_named()
    {
        BarcodeQualityReport report = Check(Bars(Encode(BarcodeSymbology.Code128, "ABC-12345"), module: 5, spread: 2));
        report.Found.Should().BeTrue();
        report.Parameters.Single(p => p.Name == "Bar width").Value.Should().Contain("too wide");
        report.Advice.Should().Contain(a => a.Contains("too wide", StringComparison.Ordinal));
        report.Parameters.Single(p => p.Name == "Module size in the picture").Value.Should().StartWith("5.0", "edge-to-edge measuring is not fooled by the wider bars");
    }

    [Fact]
    public void Printing_too_close_to_the_bars_fails_the_quiet_zone()
    {
        BarcodeQualityReport report = Check(Bars(Encode(BarcodeSymbology.Code128, "ABC-12345"), blockAtModules: 7));
        report.Parameters.Single(p => p.Name == "Quiet zones").Grade.Should().Be(QualityGrade.F);
        report.Advice.Should().Contain(a => a.Contains("blank space", StringComparison.Ordinal));
    }

    [Fact]
    public void Nothing_in_the_picture_is_said_plainly()
    {
        var blank = new GrayImage(Enumerable.Repeat((byte)200, 100 * 80).ToArray(), 100, 80);
        BarcodeGrader.Check(blank).Found.Should().BeFalse();
    }

    [Theory]
    [InlineData(BarcodeSymbology.QrCode, "https://example.com/p/123")]
    [InlineData(BarcodeSymbology.DataMatrix, "LOT-2026-0042")]
    public void A_clean_square_code_gets_an_A_and_blur_lowers_it(BarcodeSymbology symbology, string data)
    {
        BarcodeSymbol symbol = Encode(symbology, data);
        BarcodeQualityReport clean = Check(Matrix(symbol));
        clean.Found.Should().BeTrue();
        clean.Text.Should().Be(data);
        clean.Grade.Should().Be(QualityGrade.A);
        clean.ModulePixels.Should().BeApproximately(6, 0.6);

        BarcodeQualityReport blurred = Check(Matrix(symbol, module: 6, blur: 3));
        if (blurred.Found)
        {
            ((int)blurred.Parameters.Single(p => p.Name == "Modulation").Grade!.Value).Should().BeLessThan((int)clean.Parameters.Single(p => p.Name == "Modulation").Grade!.Value);
        }
    }

    [Fact]
    public void A_spot_in_a_space_is_a_defect()
    {
        BarcodeSymbol symbol = Encode(BarcodeSymbology.Code128, "ABC-12345");
        GrayImage clean = Bars(symbol, module: 5);
        // A grey spot in the middle of the widest space, over a third of the bar height.
        BarModule space = symbol.Bars.Where(b => !b.IsBar && b.StartModule > symbol.QuietZoneModules &&
                                                 b.StartModule + b.WidthModules < symbol.TotalWidthModules - symbol.QuietZoneModules).MaxBy(b => b.WidthModules);
        int centre = 40 + (int)((space.StartModule + (space.WidthModules / 2.0)) * 5);
        byte[] pixels = (byte[])clean.Pixels.Clone();
        for (int y = 70; y < 110; y++)
        {
            for (int x = centre - 2; x <= centre + 2; x++)
            {
                pixels[(y * clean.Width) + x] = 180;
            }
        }

        BarcodeQualityReport report = Check(new GrayImage(pixels, clean.Width, clean.Height));
        ((int)report.Parameters.Single(p => p.Name == "Defects").Grade!.Value).Should().BeLessThan((int)QualityGrade.A);
        report.Advice.Should().Contain(a => a.Contains("Spots", StringComparison.Ordinal));
    }
}
