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
    public void The_offer_to_print_a_publisher_project_on_its_label_sheet_is_translated_with_its_sizes_kept()
    {
        const string offer = "This project came from Microsoft Publisher with one 38 × 21.2 mm label on each page, so every label would print on a sheet of its own.\n\n" +
                             "Print it on the Formtec 3100 sheet (65 labels on A4) instead, several labels to a sheet as in Publisher?\n\n" +
                             "You can choose a different sheet later in Label manager.";
        const string done = "Printing on the Formtec 3100 sheet (65 labels on A4). Save to keep it.";
        foreach (AppLanguage language in Localizer.Languages.Where(l => l.Code != "en"))
        {
            string text = Localizer.Load(language).Translate(offer);
            string status = Localizer.Load(language).Translate(done);

            text.Should().NotBe(offer, language.EnglishName);
            text.Should().Contain("38 × 21.2 mm").And.Contain("Formtec 3100").And.Contain("65").And.NotContain("labels on", language.EnglishName);
            status.Should().Contain("Formtec 3100").And.Contain("65").And.NotContain("Save to keep", language.EnglishName);
        }
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
