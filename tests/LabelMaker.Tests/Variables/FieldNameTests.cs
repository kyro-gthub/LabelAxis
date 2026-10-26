using FluentAssertions;
using LabelMaker.Core.Variables;
using Xunit;

namespace LabelMaker.Tests.Variables;

/// <summary>Columns whose names are more than plain Latin words: "Price (USD)", "اسم المنتج".</summary>
public sealed class FieldNameTests
{
    private static VariableResolver Resolver() => new(new ResolveContext
    {
        Record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Price (USD)"] = "$4.98",
            ["اسم المنتج"] = "فرشاة أسنان",
            ["Qty"] = "3",
        },
        Now = new DateTime(2026, 1, 15),
    });

    [Theory]
    [InlineData("{Price (USD)}", "$4.98")]
    [InlineData("{اسم المنتج}", "فرشاة أسنان")]
    [InlineData("{Qty}", "3")]
    public void A_column_is_filled_in_whatever_its_name_looks_like(string token, string expected) =>
        Resolver().Resolve(token).Should().Be(expected);

    [Fact]
    public void A_name_with_a_note_in_brackets_is_one_field_not_a_call()
    {
        TokenScanner.ExtractFieldNames("{Price (USD)}").Should().Equal("Price (USD)");
        TokenScanner.ExtractFieldNames("{UPPER(Product)}").Should().Equal(new[] { "Product" }, "a call still gives the field inside it");
    }

    [Fact]
    public void Field_names_in_any_alphabet_are_found() =>
        TokenScanner.ExtractFieldNames("{اسم المنتج} – {Qty}").Should().Equal("اسم المنتج", "Qty");
}
