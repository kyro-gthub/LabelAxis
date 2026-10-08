using FluentAssertions;
using LabelMaker.App.Controls;
using LabelMaker.Core.Model;
using Xunit;

namespace LabelMaker.App.Tests.Controls;

/// <summary>The colour window's recent colours and gradients, and how a gradient is saved.</summary>
public sealed class ColourMemoryTests
{
    [Fact]
    public void A_colour_used_again_moves_to_the_front_instead_of_being_listed_twice()
    {
        var recent = new List<string> { "#112233", "#445566", "#778899" };

        ColourMemory.Remember(recent, "#445566", max: 20);

        recent.Should().Equal("#445566", "#112233", "#778899");
    }

    [Fact]
    public void Remembering_ignores_letter_case_when_looking_for_the_same_colour()
    {
        var recent = new List<string> { "#ABCDEF" };

        ColourMemory.Remember(recent, "#abcdef", max: 20);

        recent.Should().Equal("#abcdef");
    }

    [Fact]
    public void The_oldest_colours_drop_off_the_end_of_a_full_list()
    {
        var recent = Enumerable.Range(0, ColourMemory.MaxRecentColours).Select(i => $"#0000{i:X2}").ToList();

        ColourMemory.Remember(recent, "#FFFFFF", ColourMemory.MaxRecentColours);

        recent.Should().HaveCount(ColourMemory.MaxRecentColours);
        recent[0].Should().Be("#FFFFFF");
        recent.Should().NotContain($"#0000{ColourMemory.MaxRecentColours - 1:X2}");
    }

    [Fact]
    public void A_saved_gradient_reads_back_the_same()
    {
        var from = new ColorRgba(0x2C, 0x6F, 0xE8);
        var to = new ColorRgba(0, 0, 0, 0);

        string saved = ColourMemory.FormatGradient(from, to, 135);

        ColourMemory.ParseGradient(saved).Should().Be((from, to, 135d));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#FFFFFF")]
    [InlineData("#FFFFFF #000000")]
    [InlineData("#FFFFFF #000000 north")]
    [InlineData("white black 90")]
    public void A_damaged_saved_gradient_is_skipped(string? saved) =>
        ColourMemory.ParseGradient(saved).Should().BeNull();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    [InlineData(360, 0)]
    [InlineData(450, 90)]
    [InlineData(-90, 270)]
    [InlineData(-720, 0)]
    public void Directions_are_kept_between_0_and_360_degrees(double degrees, double expected) =>
        ColourMemory.NormaliseAngle(degrees).Should().Be(expected);

    [Theory]
    [InlineData(0xF6, 0xE2, 0x7A)]
    [InlineData(0x14, 0x1E, 0x30)]
    [InlineData(0x80, 0x80, 0x80)]
    public void A_new_gradient_ends_on_a_different_colour_with_the_same_opacity(byte r, byte g, byte b)
    {
        var start = new ColorRgba(r, g, b, 200);

        ColorRgba partner = ColourMemory.PartnerOf(start);

        partner.Should().NotBe(start);
        partner.A.Should().Be(200);
    }

    [Fact]
    public void Every_ready_made_gradient_is_two_real_colours_with_a_name()
    {
        ColourMemory.PresetGradients.Should().NotBeEmpty();
        foreach ((string name, string from, string to) in ColourMemory.PresetGradients)
        {
            name.Should().NotBeNullOrWhiteSpace();
            ColorRgba.TryParse(from, out _).Should().BeTrue(name);
            ColorRgba.TryParse(to, out _).Should().BeTrue(name);
        }

        ColourMemory.PresetGradients.Select(g => g.Name).Should().OnlyHaveUniqueItems();
    }
}
