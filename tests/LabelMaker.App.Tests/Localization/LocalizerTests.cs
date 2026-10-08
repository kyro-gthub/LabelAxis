using FluentAssertions;
using LabelMaker.App.Localization;
using Xunit;

namespace LabelMaker.App.Tests.Localization;

/// <summary>Which interface language LabelAxis picks, and that every language comes with the program.</summary>
public sealed class LocalizerTests
{
    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("fr-CA", "fr")]
    [InlineData("fr", "fr")]
    [InlineData("es-MX", "es")]
    [InlineData("nl-BE", "nl")]
    [InlineData("ru-RU", "ru")]
    [InlineData("hi-IN", "hi")]
    [InlineData("ar-IQ", "ar")]
    [InlineData("fa-IR", "fa")]
    [InlineData("ckb-IQ", "ckb")]
    [InlineData("ku-Arab-IQ", "ckb")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-Hans-SG", "zh-Hans")]
    [InlineData("zh-TW", "en")]
    [InlineData("zh-Hant-HK", "en")]
    [InlineData("de-DE", "en")]
    [InlineData("frx", "en")]
    [InlineData("", "en")]
    public void The_language_of_windows_is_followed_when_labelaxis_has_it(string culture, string expected) =>
        Localizer.FromWindows(culture).Code.Should().Be(expected);

    [Theory]
    [InlineData("fa")]
    [InlineData("FA")]
    public void A_language_chosen_in_the_settings_is_found_in_any_letter_case(string code) =>
        Localizer.Resolve(code).Code.Should().Be("fa");

    [Fact]
    public void A_setting_for_a_language_no_longer_offered_falls_back_to_english() =>
        Localizer.Resolve("tlh").Code.Should().Be("en");

    [Fact]
    public void Arabic_kurdish_and_persian_are_laid_out_right_to_left_and_no_others()
    {
        Localizer.Languages.Where(l => l.RightToLeft).Select(l => l.Code).Should().BeEquivalentTo("ar", "ckb", "fa");
    }

    [Fact]
    public void Every_language_but_english_comes_with_its_translations_inside_the_program()
    {
        foreach (AppLanguage language in Localizer.Languages)
        {
            int count = Localizer.Load(language).Count;
            if (language.Code == "en")
            {
                count.Should().Be(0);
            }
            else
            {
                count.Should().BeGreaterThan(2000, language.EnglishName);
            }
        }
    }

    [Fact]
    public void The_language_list_shows_each_name_in_its_own_language_with_the_english_name()
    {
        Localizer.Resolve("fr").Display.Should().Be("Français  ·  French");
        Localizer.Resolve("en").Display.Should().Be("English");
    }
}
