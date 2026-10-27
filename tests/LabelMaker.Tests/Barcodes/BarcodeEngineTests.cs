using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Units;
using LabelMaker.Tests.Support;
using Xunit;
using ZXing;

namespace LabelMaker.Tests.Barcodes;

public sealed class BarcodeEngineTests
{
    [Fact]
    public void Code128_family_is_always_supported()
    {
        BarcodeEngine.IsSupported(BarcodeSymbology.Code128).Should().BeTrue();
        BarcodeEngine.IsSupported(BarcodeSymbology.GS1_128).Should().BeTrue();
    }

    [Fact]
    public void Supported_symbologies_include_the_core_set()
    {
        var supported = BarcodeEngine.SupportedSymbologies();
        supported.Should().Contain(new[]
        {
            BarcodeSymbology.Code128, BarcodeSymbology.Code39, BarcodeSymbology.Ean13,
            BarcodeSymbology.QrCode, BarcodeSymbology.DataMatrix, BarcodeSymbology.Pdf417,
        });
    }

    [Fact]
    public void Qr_code_round_trips_through_zxing()
    {
        var spec = new BarcodeSpec { Symbology = BarcodeSymbology.QrCode, Data = "https://example.com/label/12345" };
        BarcodeSymbol symbol = BarcodeEngine.Encode(spec, spec.Data);

        symbol.Is2D.Should().BeTrue();
        BarcodeRaster.DecodeMatrix(symbol, BarcodeFormat.QR_CODE).Should().Be("https://example.com/label/12345");
    }

    [Fact]
    public void Ean13_round_trips_and_validates_check_digit()
    {
        var spec = new BarcodeSpec { Symbology = BarcodeSymbology.Ean13, Data = "5901234123457", ModuleWidthMm = 0.33 };
        BarcodeEngine.TryEncode(spec, spec.Data, out BarcodeSymbol? symbol, out BarcodeValidationResult validation)
            .Should().BeTrue();
        validation.IsValid.Should().BeTrue();

        BarcodeRaster.DecodeLinear(symbol!, BarcodeFormat.EAN_13).Should().Be("5901234123457");
    }

    [Fact]
    public void Ean13_with_bad_check_digit_is_rejected_before_printing()
    {
        var spec = new BarcodeSpec { Symbology = BarcodeSymbology.Ean13, Data = "5901234123456" };
        BarcodeEngine.TryEncode(spec, spec.Data, out _, out BarcodeValidationResult validation).Should().BeFalse();
        validation.Issues.Should().Contain(i => i.Message.Contains("check digit"));
    }

    [Fact]
    public void Code39_round_trips()
    {
        var spec = new BarcodeSpec { Symbology = BarcodeSymbology.Code39, Data = "LM-2026-001", ModuleWidthMm = 0.3 };
        BarcodeSymbol symbol = BarcodeEngine.Encode(spec, spec.Data);
        BarcodeRaster.DecodeLinear(symbol, BarcodeFormat.CODE_39).Should().Be("LM-2026-001");
    }

    [Fact]
    public void Empty_1d_value_fails_validation()
    {
        var spec = new BarcodeSpec { Symbology = BarcodeSymbology.Code128, Data = "" };
        BarcodeEngine.TryEncode(spec, "", out _, out BarcodeValidationResult v).Should().BeFalse();
        v.Issues.Should().Contain(i => i.Message.Contains("empty"));
    }

    [Fact]
    public void Natural_size_scales_with_module_width()
    {
        var spec = new BarcodeSpec { Symbology = BarcodeSymbology.Code128, Data = "12345678", ModuleWidthMm = 0.5, BarHeightMm = 12 };
        BarcodeSymbol symbol = BarcodeEngine.Encode(spec, spec.Data);
        var natural = BarcodeLayout.NaturalSize(symbol, spec);

        natural.Width.Should().BeApproximately(symbol.TotalWidthModules * 0.5, 1e-9);
        natural.Height.Should().BeGreaterThan(12); // includes HRI band
    }

    [Theory]
    [InlineData(BarcodeSymbology.Code128, "12345678")]
    [InlineData(BarcodeSymbology.UpcA, "036000291452")]
    [InlineData(BarcodeSymbology.Ean13, "5901234123457")]
    [InlineData(BarcodeSymbology.Code39, "ABC123")]
    [InlineData(BarcodeSymbology.QrCode, "https://example.com")]
    public void A_quiet_zone_of_0_puts_the_bars_on_the_edges_of_the_box(BarcodeSymbology symbology, string value)
    {
        // Code 128 was held to at least 10 modules and EAN / UPC to 9, so the bars stayed
        // inside their box with white space either side.
        var spec = new BarcodeSpec { Symbology = symbology, Data = value, ModuleWidthMm = 0.26, BarHeightMm = 10, QuietZoneModules = 0 };
        BarcodeSymbol symbol = BarcodeEngine.Encode(spec, value);
        PlacedBarcode placed = BarcodeLayout.PlaceNatural(symbol, spec);

        symbol.QuietZoneModules.Should().Be(0);
        placed.DarkRects.Min(r => r.X).Should().BeApproximately(0, 1e-9);
        placed.DarkRects.Max(r => r.Right).Should().BeApproximately(placed.NaturalSizeMm.Width, 1e-9);

        PlacedBarcode fitted = BarcodeLayout.PlaceFit(symbol, spec, new SizeMm(40, 40));
        fitted.DarkRects.Min(r => r.X).Should().BeApproximately(0, 1e-9);
        fitted.DarkRects.Max(r => r.Right).Should().BeApproximately(40, 1e-9);
    }

    [Fact]
    public void A_quiet_zone_is_used_as_set_and_ready_made_designs_start_from_the_standard_one()
    {
        var spec = new BarcodeSpec { Symbology = BarcodeSymbology.Ean13, Data = "5901234123457", QuietZoneModules = 3 };
        BarcodeEngine.Encode(spec, spec.Data).QuietZoneModules.Should().Be(3);

        BarcodeSymbology.Ean13.StandardQuietZone().Should().Be(9);
        BarcodeSymbology.QrCode.StandardQuietZone().Should().Be(4);
        BarcodeSymbology.Code128.StandardQuietZone().Should().Be(10);
    }
}
