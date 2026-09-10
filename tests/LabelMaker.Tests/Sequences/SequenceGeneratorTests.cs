using FluentAssertions;
using LabelMaker.Core.Sequences;
using Xunit;

namespace LabelMaker.Tests.Sequences;

public sealed class SequenceGeneratorTests
{
    [Fact]
    public void Simple_padded_numeric_range()
    {
        var def = new SequenceDefinition { Start = 1, End = 5, Padding = 6, Prefix = "", Suffix = "", Casing = SequenceCasing.AsIs };
        SequenceGenerator.Generate(def).Should().Equal("000001", "000002", "000003", "000004", "000005");
    }

    [Fact]
    public void Prefix_and_suffix_with_padding()
    {
        var def = new SequenceDefinition { Prefix = "ABC", Start = 1, End = 3, Padding = 6, Casing = SequenceCasing.Upper };
        SequenceGenerator.Generate(def).Should().Equal("ABC000001", "ABC000002", "ABC000003");
    }

    [Fact]
    public void Count_mode_with_step()
    {
        var def = new SequenceDefinition { Start = 100, UseCount = true, Count = 4, Step = 5, Padding = 4, Casing = SequenceCasing.AsIs };
        SequenceGenerator.Generate(def).Should().Equal("0100", "0105", "0110", "0115");
    }

    [Fact]
    public void Pattern_places_number_in_hash_run()
    {
        var def = new SequenceDefinition { Pattern = "BOX-A-#####", Start = 1, End = 2, Casing = SequenceCasing.AsIs };
        SequenceGenerator.Generate(def).Should().Equal("BOX-A-00001", "BOX-A-00002");
    }

    [Fact]
    public void Pattern_supports_mid_string_tokens()
    {
        var def = new SequenceDefinition { Pattern = "ITEM2026-######", Start = 1, UseCount = true, Count = 1 };
        SequenceGenerator.Generate(def).Should().Equal("ITEM2026-000001");
    }

    [Fact]
    public void Preview_reports_first_last_and_count()
    {
        var def = new SequenceDefinition { Prefix = "PRD-", Suffix = "-IRQ", Start = 1, End = 10000, Padding = 6 };
        (string first, string last, long count) = SequenceGenerator.Preview(def);

        first.Should().Be("PRD-000001-IRQ");
        last.Should().Be("PRD-010000-IRQ");
        count.Should().Be(10000);
    }

    [Fact]
    public void Hex_radix_formats_uppercased_by_default()
    {
        var def = new SequenceDefinition { Radix = 16, Start = 250, End = 255, Padding = 4, Casing = SequenceCasing.Upper };
        SequenceGenerator.Generate(def).Should().Equal("00FA", "00FB", "00FC", "00FD", "00FE", "00FF");
    }

    [Fact]
    public void Enumerate_is_lazy_and_handles_large_ranges()
    {
        var def = new SequenceDefinition { Prefix = "ABC", Start = 1, End = 1_000_000, Padding = 7 };
        def.EffectiveCount.Should().Be(1_000_000);
        SequenceGenerator.Enumerate(def).Take(3).Should().Equal("ABC0000001", "ABC0000002", "ABC0000003");
    }

    [Fact]
    public void Zero_step_is_rejected()
    {
        var def = new SequenceDefinition { Step = 0 };
        Action act = () => SequenceGenerator.Generate(def);
        act.Should().Throw<InvalidOperationException>();
    }
}
