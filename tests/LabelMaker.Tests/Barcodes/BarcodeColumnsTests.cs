using FluentAssertions;
using LabelMaker.Core.Barcodes;
using Xunit;

namespace LabelMaker.Tests.Barcodes;

/// <summary>Telling a column of barcodes from a column of text, and which kind of barcode it is.</summary>
public sealed class BarcodeColumnsTests
{
    [Fact]
    public void Product_numbers_with_right_check_digits_are_ean_or_upc_whatever_the_column_is_called()
    {
        BarcodeColumns.Suggest("Code", new[] { "4006381333931", "5901234123457" }).Should().Be(BarcodeSymbology.Ean13);
        BarcodeColumns.Suggest("Item", new[] { "036000291452" }).Should().Be(BarcodeSymbology.UpcA);
        BarcodeColumns.Suggest("Short", new[] { "96385074" }).Should().Be(BarcodeSymbology.Ean8);
        BarcodeColumns.Suggest("Book", new[] { "9780306406157" }).Should().Be(BarcodeSymbology.Isbn13);
    }

    [Fact]
    public void A_column_named_barcode_whose_numbers_are_not_ean_is_code_128() =>
        BarcodeColumns.Suggest("Barcode", new[] { "253557152275", "123456789012" }).Should().Be(BarcodeSymbology.Code128,
            "a wrong check digit means these are not UPC-A numbers");

    [Fact]
    public void Ordinary_text_and_numbers_are_not_barcodes()
    {
        BarcodeColumns.Suggest("SKU", new[] { "PRD-0001", "PRD-0002" }).Should().BeNull();
        BarcodeColumns.Suggest("Price (USD)", new[] { "$4.98" }).Should().BeNull();
        BarcodeColumns.Suggest("Qty", new[] { "445", "449" }).Should().BeNull();
    }

    [Fact]
    public void Links_in_a_qr_or_link_column_are_qr_codes() =>
        BarcodeColumns.Suggest("Website link", new[] { "https://example.com/a", "https://example.com/b" }).Should().Be(BarcodeSymbology.QrCode);

    [Fact]
    public void A_barcode_column_in_another_language_is_found() =>
        BarcodeColumns.Suggest("باركود", new[] { "ABC-123" }).Should().Be(BarcodeSymbology.Code128);

    [Fact]
    public void The_values_a_type_cannot_hold_are_counted() =>
        BarcodeColumns.Unfit(BarcodeSymbology.Ean13, new[] { "4006381333931", "ABC", "4006381333932" }).Should().Be(2);
}
