using FluentAssertions;
using LabelMaker.Core.Localization;
using Xunit;

namespace LabelMaker.Tests.Localization;

public sealed class NativeDigitsTests
{
    [Theory]
    [InlineData("۱۲", "12")]      // Persian and Kurdish keyboards
    [InlineData("١٢٫٥", "12.5")]  // Arabic, with its decimal mark
    [InlineData("१२", "12")]      // Hindi
    [InlineData("12.5", "12.5")]
    [InlineData("", "")]
    public void Digits_typed_on_other_keyboards_are_read_as_0_to_9(string typed, string expected) =>
        NativeDigits.ToAscii(typed).Should().Be(expected);

    [Fact]
    public void Text_without_native_digits_is_the_same_text() =>
        NativeDigits.HasNative("Box 12").Should().BeFalse();
}
