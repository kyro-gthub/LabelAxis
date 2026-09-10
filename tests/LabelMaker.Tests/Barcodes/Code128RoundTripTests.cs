using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Barcodes.Code128;
using LabelMaker.Tests.Support;
using Xunit;
using ZXing;

namespace LabelMaker.Tests.Barcodes;

/// <summary>
/// The safety net for the first-party Code 128 encoder: encode a broad corpus,
/// rasterise, and decode every symbol back with ZXing — the decoded text must
/// equal the input (spec §6, §40). Guards the pattern table and the A/B/C
/// automatic set-selection algorithm.
/// </summary>
public sealed class Code128RoundTripTests
{
    public static TheoryData<string> Corpus()
    {
        var data = new TheoryData<string>();
        foreach (string s in new[]
                 {
                     "ABC-12345",
                     "12345678",
                     "1234567",              // odd digit run
                     "0",
                     "HELLO WORLD",
                     "abcDEF123",
                     "Order #98765 / 2026",
                     "PRD-000001-IRQ",
                     "BOX-A-00001",
                     "ITEM2026-000001",
                     "Tab\tSeparated",
                     "~!@#$%^&*()_+={}[]|;:',.<>/?",
                     "MixedCASE-with-123-and-456-and-789",
                     new string('9', 40),
                     "A1B2C3D4E5F6G7H8I9J0",
                     "CODE128 test: 4004630000001",
                 })
        {
            data.Add(s);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Auto_encode_then_zxing_decode_returns_input(string value)
    {
        BarcodeSymbol symbol = Code128Encoder.Encode(value, BarcodeSymbology.Code128);

        string? decoded = BarcodeRaster.DecodeLinear(symbol, BarcodeFormat.CODE_128);

        decoded.Should().Be(value, $"'{value}' must survive a Code 128 encode/decode round trip");
    }

    [Theory]
    [InlineData("SET-B-lowercase-99", BarcodeSymbology.Code128B)]
    [InlineData("UPPER ONLY 123", BarcodeSymbology.Code128A)]
    [InlineData("00112233445566", BarcodeSymbology.Code128C)]
    public void Forced_sets_round_trip(string value, BarcodeSymbology symbology)
    {
        BarcodeSymbol symbol = Code128Encoder.Encode(value, symbology);
        string? decoded = BarcodeRaster.DecodeLinear(symbol, BarcodeFormat.CODE_128);
        decoded.Should().Be(value);
    }

    [Fact]
    public void Checksum_matches_reference_for_known_value()
    {
        // "CODE128" in Code B: Start B (104), C(35) O(47) D(36) E(37) 1(17) 2(18) 8(24)
        // check = (104 + 1*35 + 2*47 + 3*36 + 4*37 + 5*17 + 6*18 + 7*24) % 103
        BarcodeSymbol symbol = Code128Encoder.Encode("CODE128", BarcodeSymbology.Code128B);
        string? decoded = BarcodeRaster.DecodeLinear(symbol, BarcodeFormat.CODE_128);
        decoded.Should().Be("CODE128");
    }

    [Fact]
    public void Auto_uses_code_c_for_long_digit_runs_and_stays_compact()
    {
        BarcodeSymbol digits = Code128Encoder.Encode("1234567890123456", BarcodeSymbology.Code128);
        BarcodeSymbol asText = Code128Encoder.Encode("1234567890123456", BarcodeSymbology.Code128B);

        // Set C packs two digits per symbol, so the auto encoding must be narrower.
        digits.TotalWidthModules.Should().BeLessThan(asText.TotalWidthModules);
    }

    [Fact]
    public void Gs1_128_round_trips_application_identifiers()
    {
        var spec = new BarcodeSpec { Symbology = BarcodeSymbology.GS1_128, Data = "(01)09501101020917(10)ABC123", Gs1Mode = true };
        BarcodeSymbol symbol = BarcodeEngine.Encode(spec, spec.Data);

        string? decoded = BarcodeRaster.DecodeLinear(symbol, BarcodeFormat.CODE_128);

        // ZXing returns GS1 data with FNC1 rendered; strip the group separator for comparison.
        decoded.Should().NotBeNull();
        decoded!.Replace("", string.Empty)
            .Should().Contain("0109501101020917").And.Contain("10ABC123");
    }
}
