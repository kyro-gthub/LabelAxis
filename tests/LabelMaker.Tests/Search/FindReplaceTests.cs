using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Search;
using Xunit;

namespace LabelMaker.Tests.Search;

public sealed class FindReplaceTests
{
    private static ProjectModel Project(params LabelObject[] masterObjects)
    {
        var project = new ProjectModel();
        project.MasterLabel.Objects.AddRange(masterObjects);
        return project;
    }

    private static TextObject Text(string s) => new() { Content = RichTextContent.FromPlainText(s) };

    private static void ReplaceAll(ProjectModel project, FindOptions options, string replacement, out int count)
    {
        IReadOnlyList<IUndoableCommand> commands =
            FindReplaceEngine.BuildReplaceAll(project, null, options, replacement, out count);
        foreach (IUndoableCommand c in commands)
        {
            c.Do();
        }
    }

    [Fact]
    public void Finds_text_barcode_table_and_name_matches_across_pages()
    {
        var table = new TableObject { Rows = 1, Columns = 2, Cells = new() { "Apple pie", "pear" } };
        var project = Project(
            Text("Apple juice"),
            new BarcodeObject { Spec = new BarcodeSpec { Data = "APPLE-01" } },
            table);
        project.Pages.Add(new LabelPage { Name = "Page 2", Document = new LabelDocument { Objects = { new TextObject { Name = "apple label" } } } });

        var matches = FindReplaceEngine.FindAll(project, null, new FindOptions { Query = "apple" });

        matches.Select(m => m.Field).Should().BeEquivalentTo(new[]
        {
            FindField.Text, FindField.BarcodeData, FindField.TableCell, FindField.ObjectName,
        });
        matches.Single(m => m.Field == FindField.ObjectName).PageName.Should().Be("Page 2");
    }

    [Fact]
    public void Current_label_scope_ignores_other_pages()
    {
        var project = Project(Text("apple"));
        project.Pages.Add(new LabelPage { Document = new LabelDocument { Objects = { Text("apple") } } });

        var matches = FindReplaceEngine.FindAll(project, null,
            new FindOptions { Query = "apple", Scope = FindScope.CurrentLabel });

        matches.Should().ContainSingle();
    }

    [Fact]
    public void Match_case_and_whole_word_are_respected()
    {
        var project = Project(Text("Cat cat category"));

        FindReplaceEngine.FindAll(project, null, new FindOptions { Query = "cat" }).Should().HaveCount(3);
        FindReplaceEngine.FindAll(project, null, new FindOptions { Query = "cat", MatchCase = true }).Should().HaveCount(2);
        FindReplaceEngine.FindAll(project, null, new FindOptions { Query = "cat", WholeWord = true }).Should().HaveCount(2);
        FindReplaceEngine.FindAll(project, null, new FindOptions { Query = "cat", WholeWord = true, MatchCase = true }).Should().ContainSingle();
    }

    [Fact]
    public void Plain_queries_are_literal_even_with_regex_characters()
    {
        var project = Project(Text("Price (incl. VAT) 1.50"));

        FindReplaceEngine.FindAll(project, null, new FindOptions { Query = "(incl." }).Should().ContainSingle();
        FindReplaceEngine.FindAll(project, null, new FindOptions { Query = "1.50" }).Should().ContainSingle();
        FindReplaceEngine.FindAll(project, null, new FindOptions { Query = "1x50" }).Should().BeEmpty();
    }

    [Fact]
    public void Tokens_are_skipped_by_default_so_bindings_survive()
    {
        var text = Text("Price: {Price}");
        var project = Project(text);

        ReplaceAll(project, new FindOptions { Query = "Price" }, "Cost", out int count);

        count.Should().Be(1);
        text.Content.PlainText.Should().Be("Cost: {Price}");
    }

    [Fact]
    public void Tokens_can_be_included_on_request()
    {
        var text = Text("Price: {Price}");
        var project = Project(text);

        ReplaceAll(project, new FindOptions { Query = "Price", SkipTokens = false }, "Cost", out int count);

        count.Should().Be(2);
        text.Content.PlainText.Should().Be("Cost: {Cost}");
    }

    [Fact]
    public void Regex_replacement_supports_groups()
    {
        var barcode = new BarcodeObject { Spec = new BarcodeSpec { Data = "LOT-2024-001" } };
        var project = Project(barcode);

        ReplaceAll(project, new FindOptions { Query = @"LOT-(\d{4})-(\d+)", UseRegex = true }, "$1/$2", out int count);

        count.Should().Be(1);
        barcode.Spec.Data.Should().Be("2024/001");
    }

    [Fact]
    public void Invalid_regex_is_reported_not_thrown()
    {
        string? problem = FindReplaceEngine.Validate(new FindOptions { Query = "(unclosed", UseRegex = true });

        problem.Should().StartWith("Invalid regular expression");
        FindReplaceEngine.FindAll(Project(Text("x")), null, new FindOptions { Query = "(unclosed", UseRegex = true })
            .Should().BeEmpty();
    }

    [Fact]
    public void Replacement_keeps_run_formatting_and_handles_matches_across_runs()
    {
        var paragraph = new Paragraph
        {
            Runs =
            {
                new TextRun { Text = "Hello ", Bold = true },
                new TextRun { Text = "World", Italic = true },
            },
        };
        var text = new TextObject { Content = new RichTextContent { Paragraphs = { paragraph } } };
        var project = Project(text);

        ReplaceAll(project, new FindOptions { Query = "o W" }, "0-w", out int count);

        count.Should().Be(1);
        Paragraph p = text.Content.Paragraphs[0];
        p.PlainText.Should().Be("Hell0-world");
        p.Runs[0].Text.Should().Be("Hell0-w");
        p.Runs[0].Bold.Should().BeTrue();
        p.Runs[1].Text.Should().Be("orld");
        p.Runs[1].Italic.Should().BeTrue();
    }

    [Fact]
    public void Replace_all_is_fully_undoable()
    {
        var text = Text("red apple, red pear");
        var project = Project(text);
        var commands = FindReplaceEngine.BuildReplaceAll(project, null, new FindOptions { Query = "red" }, "green", out _);
        var composite = new CompositeCommand("Replace all", commands);

        composite.Do();
        text.Content.PlainText.Should().Be("green apple, green pear");

        composite.Undo();
        text.Content.PlainText.Should().Be("red apple, red pear");
    }

    [Fact]
    public void Replace_all_can_leave_locked_objects_untouched()
    {
        var locked = new TextObject { Content = RichTextContent.FromPlainText("SALE"), Locked = true };
        var free = Text("SALE");
        var project = Project(locked, free);

        var commands = FindReplaceEngine.BuildReplaceAll(
            project, null, new FindOptions { Query = "SALE" }, "NEW", skipLocked: true,
            out int occurrences, out int objects, out int lockedSkipped);
        foreach (IUndoableCommand c in commands)
        {
            c.Do();
        }

        locked.Content.PlainText.Should().Be("SALE");
        free.Content.PlainText.Should().Be("NEW");
        occurrences.Should().Be(1);
        objects.Should().Be(1);
        lockedSkipped.Should().Be(1);
    }

    [Fact]
    public void Replace_one_changes_only_that_occurrence()
    {
        var text = Text("one two one");
        var project = Project(text);
        var options = new FindOptions { Query = "one" };
        FindMatch second = FindReplaceEngine.FindAll(project, null, options)[1];

        IUndoableCommand? command = FindReplaceEngine.BuildReplaceOne(second, options, "1");

        command.Should().NotBeNull();
        command!.Do();
        text.Content.PlainText.Should().Be("one two 1");
    }

    [Fact]
    public void Replace_one_refuses_a_stale_match()
    {
        var text = Text("one two one");
        var project = Project(text);
        var options = new FindOptions { Query = "one" };
        FindMatch second = FindReplaceEngine.FindAll(project, null, options)[1];

        text.Content = RichTextContent.FromPlainText("changed");

        FindReplaceEngine.BuildReplaceOne(second, options, "1").Should().BeNull();
    }

    [Fact]
    public void Context_shows_text_around_the_hit()
    {
        var project = Project(Text("Best before 12/2026 keep refrigerated"));

        FindMatch m = FindReplaceEngine.FindAll(project, null, new FindOptions { Query = "12/2026" }).Single();

        m.Before.Should().Be("Best before ");
        m.Hit.Should().Be("12/2026");
        m.After.Should().Be(" keep refrigerated");
    }
}
