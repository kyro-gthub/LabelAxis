using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Barcodes.Gs1;
using LabelMaker.Tests.Support;
using Xunit;
using ZXing;

namespace LabelMaker.Tests.Barcodes;

public sealed class Gs1Tests
{
    private static readonly DateTime Today = new(2026, 10, 7);

    [Theory]
    [InlineData("0950600013435", 2)] // GS1's own example GTIN 09506000134352
    [InlineData("400638133393", 1)] // EAN-13 4006381333931
    [InlineData("10614141123456789", 7)] // GS1 example SSCC 106141411234567897
    public void Check_digits_follow_the_GS1_weights(string digits, int check)
    {
        Gs1.CheckDigit(digits).Should().Be(check);
        Gs1.HasValidCheckDigit(digits + check).Should().BeTrue();
        Gs1.HasValidCheckDigit(digits + ((check + 1) % 10)).Should().BeFalse();
    }

    [Fact]
    public void Shorter_product_numbers_are_padded_to_14_digits()
    {
        Gs1.AsGtin14("9506000134352").Should().Be("09506000134352");
        Gs1.AsGtin14("96385074").Should().Be("00000096385074");
        Gs1.AsGtin14("12345").Should().BeNull();
        Gs1.Find("3103")!.Code.Should().Be("310n");
        Gs1.Find("17")!.Title.Should().Be("Expiry date");
    }

    [Fact]
    public void Dates_are_read_with_day_zero_as_the_end_of_the_month()
    {
        Gs1.ReadDate("261231", Today).Should().Be(new DateTime(2026, 12, 31));
        Gs1.ReadDate("270200", Today).Should().Be(new DateTime(2027, 2, 28));
        Gs1.ReadDate("261331", Today).Should().BeNull();
        Gs1.ReadDate("991231", Today).Should().Be(new DateTime(1999, 12, 31), "more than 50 years ahead is the last century");
        Gs1.ToYymmdd("31/12/2026").Should().Be("261231");
        Gs1.ToYymmdd("2026-12-31").Should().Be("261231");
        Gs1.ToYymmdd("tomorrow").Should().BeNull();
        Gs1.Tidy("17", "31.12.2026").Should().Be("261231");
        Gs1.Tidy("01", "9506000134352").Should().Be("09506000134352");
    }

    [Fact]
    public void Data_is_read_and_written_in_every_form()
    {
        List<Gs1Element> elements = Gs1.Parse("(01)09506000134352(10)AB(1)C(17)261231");
        elements.Should().Equal(
            new Gs1Element("01", "09506000134352"),
            new Gs1Element("10", "AB(1)C"),
            new Gs1Element("17", "261231"));

        Gs1.Format(elements).Should().Be("(01)09506000134352(10)AB(1)C(17)261231");
        Gs1.HumanReadable(elements).Should().Be("(01) 09506000134352 (10) AB(1)C (17) 261231");

        // A separator only after a variable-length element that is not last.
        Gs1.ElementString(elements).Should().Be("0109506000134352" + "10AB(1)C" + Gs1.GroupSeparator + "17261231");
        List<Gs1Element> ordered = Gs1.RecommendedOrder(elements);
        ordered.Select(e => e.Ai).Should().Equal("01", "17", "10");
        Gs1.ElementString(ordered).Should().NotContain(Gs1.GroupSeparator.ToString(), "fixed-length first needs no separator");
    }

    [Fact]
    public void Good_data_has_no_problems()
    {
        Gs1.Check(Gs1.Parse("(01)09506000134352(17)271231(10)ABC123(21)XYZ"), Today).Should().BeEmpty();
        Gs1.Check(Gs1.Parse("(00)106141411234567897"), Today).Where(p => p.IsError).Should().BeEmpty();
        Gs1.Check(Gs1.Parse("(01)09506000134352(17){Expiry}(10){Batch}"), Today).Should().BeEmpty("data fields are checked when printing");
    }

    [Theory]
    [InlineData("(01)09506000134353", "check digit")]
    [InlineData("(01)0950600013435", "exactly 14")]
    [InlineData("(01)09506000134352(17)261331", "not a real date")]
    [InlineData("(01)09506000134352(17)251231", "already passed")]
    [InlineData("(10)ABC", "needs (01)")]
    [InlineData("(01)09506000134352(02)09506000134352(37)5", "cannot be in the same")]
    [InlineData("(02)09506000134352", "needs (37)")]
    [InlineData("(01)09506000134352(10)AB€", "cannot hold")]
    [InlineData("(01)09506000134352(10)A(10)B", "more than once")]
    [InlineData("(01)09506000134352(3107)001250", "at most 5")]
    [InlineData("(01)ABCDEFGHIJKLMN", "digits only")]
    public void Problems_are_named_in_plain_words(string data, string expected)
    {
        Gs1.Check(Gs1.Parse(data), Today).Should().Contain(p => p.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Gs1_datamatrix_starts_with_FNC1_and_holds_the_element_string()
    {
        var spec = new BarcodeSpec { Symbology = BarcodeSymbology.DataMatrix, Gs1Mode = true, Data = "(01)09506000134352(10)ABC12(17)261231" };
        BarcodeSymbol symbol = BarcodeEngine.Encode(spec, spec.Data);

        Result result = BarcodeRaster.DecodeMatrixResult(symbol, BarcodeFormat.DATA_MATRIX)!;
        result.Should().NotBeNull();
        result.ResultMetadata[ResultMetadataType.SYMBOLOGY_IDENTIFIER].Should().Be("]d2", "]d2 is GS1 DataMatrix");
        result.Text.TrimStart(Gs1.GroupSeparator).Should().Be("0109506000134352" + "10ABC12" + Gs1.GroupSeparator + "17261231");
    }

    [Fact]
    public void Gs1_qr_code_holds_the_element_string_not_the_brackets()
    {
        var spec = new BarcodeSpec { Symbology = BarcodeSymbology.QrCode, Gs1Mode = true, Data = "(01)09506000134352(21)SN1" };
        Result result = BarcodeRaster.DecodeMatrixResult(BarcodeEngine.Encode(spec, spec.Data), BarcodeFormat.QR_CODE)!;
        result.ResultMetadata[ResultMetadataType.SYMBOLOGY_IDENTIFIER].Should().Be("]Q3", "]Q3 is GS1 QR Code");
        result.Text.Should().Be("0109506000134352" + "21SN1");
    }

    [Fact]
    public void Gs1_128_shows_its_brackets_under_the_bars_and_warns_about_bad_data()
    {
        var spec = new BarcodeSpec { Symbology = BarcodeSymbology.GS1_128, Gs1Mode = true, Data = "(01)09506000134352(10)ABC" };
        BarcodeEngine.Encode(spec, spec.Data).HumanReadableText.Should().Be("(01)09506000134352(10)ABC");
        BarcodeValidator.Validate(spec, spec.Data).Issues.Should().BeEmpty();

        BarcodeValidationResult bad = BarcodeValidator.Validate(spec, "(01)09506000134353");
        bad.IsValid.Should().BeTrue("a wrong check digit still prints and scans");
        bad.Issues.Should().Contain(i => i.Message.Contains("check digit", StringComparison.Ordinal));
    }
}
