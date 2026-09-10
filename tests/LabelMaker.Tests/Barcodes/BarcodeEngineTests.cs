using FluentAssertions;
using LabelMaker.Core.Barcodes;
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
}
