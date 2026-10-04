using System.Text.RegularExpressions;
using FluentAssertions;
using LabelMaker.Core.Help;
using Xunit;

namespace LabelMaker.Tests.Help;

public sealed class HelpGuideTests
{
    private static string Repo
    {
        get
        {
            string? dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "LabelMaker.slnx")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            return dir ?? throw new DirectoryNotFoundException("Repository root not found.");
        }
    }

    private static HelpLibrary Guide() => new(Directory
        .EnumerateFiles(Path.Combine(Repo, "src", "LabelMaker.App", "Help"), "*.md")
        .Select(f => HelpMarkup.Parse(File.ReadAllText(f))));

    [Fact]
    public void Markup_reads_headings_steps_bullets_tips_tables_keys_and_links()
    {
        HelpTopic t = HelpMarkup.Parse("""
            ---
            id: demo
            title: Demo page
            category: Printing
            order: 3
            keywords: one, two
            ---
            # Demo
            Press `Ctrl+P` to **print**, see [[calibration|calibration]].

            1. First step
            2. Second step
              carried on
            - A bullet
            > Tip: Measure twice
            > and cut once.
            | Keys | Does |
            |---|---|
            | `F1` | Help |
            """);

        t.Id.Should().Be("demo");
        t.Title.Should().Be("Demo page");
        t.Category.Should().Be("Printing");
        t.Keywords.Should().Equal("one", "two");
        t.Blocks[0].Should().Be(new HelpHeading(1, "Demo"));

        var p = (HelpParagraph)t.Blocks[1];
        p.Inlines.Should().ContainEquivalentOf(new HelpKey("Ctrl+P"));
        p.Inlines.Should().ContainEquivalentOf(new HelpBold("print"));
        p.Inlines.Should().ContainEquivalentOf(new HelpLink("calibration", "calibration"));

        var steps = (HelpList)t.Blocks[2];
        steps.Numbered.Should().BeTrue();
        steps.Items.Should().HaveCount(2);
        string.Concat(steps.Items[1].Select(i => i.Text)).Should().Be("Second step carried on");
        ((HelpList)t.Blocks[3]).Numbered.Should().BeFalse();

        var tip = (HelpCallout)t.Blocks[4];
        tip.Kind.Should().Be("Tip");
        string.Concat(tip.Inlines.Select(i => i.Text)).Should().Be("Measure twice and cut once.");

        var table = (HelpTable)t.Blocks[5];
        table.Rows.Should().HaveCount(2, "the |---| line is not a row");
    }

    [Fact]
    public void An_indented_point_is_a_sub_point_of_the_item_above()
    {
        HelpTopic t = HelpMarkup.Parse("---\nid: x\n---\n- Send to:\n  - Windows printer\n  - File\n- Resolution\n");
        var list = (HelpList)t.Blocks.Single();
        list.Items.Should().HaveCount(4);
        Enumerable.Range(0, 4).Select(list.LevelOf).Should().Equal(0, 1, 1, 0);
    }

    [Fact]
    public void Every_page_reads_has_a_unique_id_and_every_link_goes_to_a_real_page()
    {
        HelpLibrary guide = Guide();
        guide.Topics.Should().HaveCountGreaterThan(15);
        guide.Topics.Select(t => t.Id).Should().OnlyHaveUniqueItems();

        var links = guide.Topics.SelectMany(t => t.Blocks).SelectMany(Inlines).OfType<HelpLink>().ToList();
        links.Should().NotBeEmpty();
        foreach (HelpLink link in links)
        {
            guide.Find(link.TopicId).Should().NotBeNull($"the link to \"{link.TopicId}\" must lead to a page");
        }

        guide.Topics[0].Id.Should().Be("getting-started", "the guide opens on the first steps");
    }

    [Fact]
    public void Every_window_and_menu_points_at_a_page_that_exists()
    {
        HelpLibrary guide = Guide();
        string app = Path.Combine(Repo, "src", "LabelMaker.App");
        var used = new HashSet<string>();
        foreach (string file in Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            string xaml = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(xaml, "HelpGuide\\.Topic=\"([^\"]+)\""))
            {
                used.Add(m.Groups[1].Value);
            }

            foreach (Match m in Regex.Matches(xaml, "Command=\"\\{Binding HelpCommand\\}\" CommandParameter=\"([^\"]+)\""))
            {
                used.Add(m.Groups[1].Value);
            }
        }

        string vm = string.Concat(Directory.EnumerateFiles(Path.Combine(app, "ViewModels"), "MainViewModel*.cs").Select(File.ReadAllText));
        foreach (Match m in Regex.Matches(vm, "=> \"([a-z-]+)\","))
        {
            used.Add(m.Groups[1].Value);
        }

        used.Should().Contain(new[] { "printing", "thermal", "text", "arabic" });
        foreach (string id in used)
        {
            guide.Find(id).Should().NotBeNull($"\"{id}\" is used by a window or menu");
        }
    }

    [Fact]
    public void Search_puts_title_matches_first_needs_every_word_and_finds_arabic_by_its_name()
    {
        HelpLibrary guide = Guide();

        guide.Search("calibration").First().Id.Should().Be("calibration");
        guide.Search("bluetooth printer").Should().Contain(t => t.Id == "thermal");
        guide.Search("bluetooth zzzz").Should().BeEmpty();
        guide.Search("عربي").Should().Contain(t => t.Id == "arabic");
        guide.Search(string.Empty).Should().HaveCount(guide.Topics.Count);

        HelpLibrary.Snippet(guide.Find("calibration")!, "offset").Should().Contain("offset");
    }

    private static IEnumerable<HelpInline> Inlines(HelpBlock block) => block switch
    {
        HelpParagraph p => p.Inlines,
        HelpList l => l.Items.SelectMany(i => i),
        HelpCallout c => c.Inlines,
        HelpTable t => t.Rows.SelectMany(r => r).SelectMany(c => c),
        _ => Enumerable.Empty<HelpInline>(),
    };
}
