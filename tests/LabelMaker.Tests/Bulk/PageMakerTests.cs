using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Barcodes.Gs1;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using Xunit;

namespace LabelMaker.Tests.Bulk;

/// <summary>Pages made in one go: one per record with its values written in, and numbered copies.</summary>
public sealed class PageMakerTests
{
    private static LabelDocument Label(string text, string barcode, BarcodeSymbology symbology = BarcodeSymbology.Code128) => new()
    {
        Objects =
        {
            new TextObject { Name = "Title", Content = RichTextContent.FromPlainText(text) },
            new BarcodeObject { Name = "Code", Spec = new BarcodeSpec { Symbology = symbology, Data = barcode } },
            new TableObject { Name = "Facts", Cells = new List<string> { "Origin", "{origin}", "Made", "{TODAY}" } },
        },
    };

    private static readonly Dictionary<string, string> Honey = new()
    {
        ["Product"] = "Wildflower honey",
        ["Origin"] = "New Zealand",
        ["Barcode"] = "2000022000000",
    };

    [Fact]
    public void A_record_page_has_the_records_values_written_in_and_keeps_other_placeholders()
    {
        LabelDocument label = Label("{Product} · {UPPER({Product})} · printed {TODAY}", "{Barcode}");

        LabelDocument page = PageMaker.ForRecord(label, Honey);

        page.Objects.OfType<TextObject>().Single().Content.PlainText.Should().Be("Wildflower honey · WILDFLOWER HONEY · printed {TODAY}");
        page.Objects.OfType<BarcodeObject>().Single().Spec.Data.Should().Be("2000022000000");
        page.Objects.OfType<TableObject>().Single().Cells.Should().Equal("Origin", "New Zealand", "Made", "{TODAY}");
        page.Id.Should().NotBe(label.Id);
        label.Objects.OfType<TextObject>().Single().Content.PlainText.Should().StartWith("{Product}", "the label itself is left as it was");
    }

    [Theory]
    [InlineData("SKU-000123", BarcodeSymbology.Code128, 1, "SKU-000124")]
    [InlineData("MILK099", BarcodeSymbology.Code128, 1, "MILK100")]
    [InlineData("A12-B7", BarcodeSymbology.Code39, 5, "A12-B12")]
    [InlineData("999", BarcodeSymbology.Code128, 1, "1000")]
    [InlineData("4006381333931", BarcodeSymbology.Ean13, 1, "4006381333948")] // the check digit is worked out again
    [InlineData("400638133393", BarcodeSymbology.Ean13, 1, "400638133394")] // given without its check digit
    public void Barcode_numbers_count_up_keeping_their_zeros_and_check_digits(string value, BarcodeSymbology symbology, long by, string expected) =>
        PageMaker.CountUp(value, symbology, by).Should().Be(expected);

    [Theory]
    [InlineData("NO-NUMBER")]
    [InlineData("{Barcode}")]
    [InlineData("")]
    public void Barcodes_without_a_number_or_filled_from_data_do_not_count(string value) =>
        PageMaker.CountUp(value, BarcodeSymbology.Code128, 1).Should().BeNull();

    [Fact]
    public void Numbered_copies_count_up_one_page_after_another()
    {
        IReadOnlyList<LabelDocument> pages = PageMaker.Numbered(Label("Box", "BOX-0007"), 3);

        pages.Select(p => p.Objects.OfType<BarcodeObject>().Single().Spec.Data).Should().Equal("BOX-0008", "BOX-0009", "BOX-0010");
        pages.Select(p => p.Id).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("(01)09506000134352(17)261231(21)0007", "(01)09506000134352(17)261231(21)0008")]
    [InlineData("(01)09506000134352(17)261231(10)LOT41", "(01)09506000134352(17)261231(10)LOT42")]
    [InlineData("(00)095060001343520017", "(00)095060001343520024")]
    public void Gs1_data_counts_up_its_serial_sscc_or_batch_and_leaves_dates_and_gtins_alone(string value, string expected) =>
        PageMaker.CountUp(value, BarcodeSymbology.GS1_128, 1).Should().Be(expected);

    [Fact]
    public void Gs1_data_with_nothing_that_counts_is_not_counted_up() =>
        PageMaker.CountUp("(01)09506000134352(17)261231", BarcodeSymbology.GS1_128, 1).Should().BeNull("the date and the GTIN must stay");

    [Fact]
    public void Upc_e_counts_up_and_gets_the_check_digit_of_the_upc_a_number_it_stands_for()
    {
        // 0123456 stands for UPC-A 0 12345 00006, check digit 5; 0123457 for 0 12345 00007, check digit 2.
        PageMaker.CountUp("01234565", BarcodeSymbology.UpcE, 1).Should().Be("01234572");
        Gs1.CheckDigit("01234500007").Should().Be(2);
    }
}
