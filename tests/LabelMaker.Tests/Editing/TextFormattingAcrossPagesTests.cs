using FluentAssertions;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Editing;

/// <summary>
/// Apply formatting to other pages, matching every one of a kind: one text box's font, size,
/// colours and so on given to every text box on the other pages.
/// </summary>
public sealed class TextFormattingAcrossPagesTests
{
    /// <summary>Every character 0.6 em wide, lines 1.2 em.</summary>
    private sealed class Measurer : ITextMeasurer
    {
        public double MeasureWidthMm(string text, string fontFamily, double fontSizePt, bool bold, bool italic) =>
            text.Length * UnitConverter.PointsToMm(fontSizePt) * 0.6;

        public double LineHeightMm(string fontFamily, double fontSizePt) => UnitConverter.PointsToMm(fontSizePt) * 1.2;
    }

    private static readonly HashSet<ObjectKind> TextOnly = new() { ObjectKind.Text };

    private static TextObject Text(string name, string text = "Price") => new()
    {
        Name = name,
        Content = RichTextContent.FromPlainText(text),
        Bounds = new RectMm(0, 0, 60, 6),
    };

    private static (ProjectModel Project, TextObject Source, LabelDocument Other) Project()
    {
        var p = new ProjectModel();
        TextObject source = Text("Title");
        source.FontFamily = "Arial";
        source.FontSizePt = 14;
        source.Bold = true;
        source.Foreground = new ColorRgba(192, 0, 0);
        source.HorizontalAlignment = HorizontalAlignment.Center;
        p.MasterLabel.Objects.Add(source);

        var other = new LabelDocument { Objects = { Text("Title", "Other title"), Text("Price") } };
        p.Pages.Add(new LabelPage { Name = "Page 1", Document = other });
        return (p, source, other);
    }

    private static IReadOnlyList<(LabelObject Source, LabelObject Target)> Pairs(ProjectModel p, TextObject source, LayoutMatch match) =>
        LayoutCopy.Pairs(p, p.MasterLabel, new[] { source }, new[] { 0, 1 }, TextOnly, match);

    private static IUndoableCommand? Format(ProjectModel p, TextObject source, TextStyleParts parts,
        LayoutMatch match = LayoutMatch.EveryOfKind, ITextMeasurer? measurer = null) =>
        PageFormatCopy.Build(Pairs(p, source, match), Array.Empty<(LabelObject, LabelDocument)>(),
            PrintablePages.All(p).ToList(), PageFormatParts.Look, parts, measurer);

    [Fact]
    public void Every_text_box_on_the_other_pages_takes_the_look_except_locked_ones()
    {
        (ProjectModel p, TextObject source, LabelDocument other) = Project();
        other.Objects[1].Locked = true;

        Pairs(p, source, LayoutMatch.EveryOfKind).Select(x => x.Target).Should().Equal(other.Objects[0]);
    }

    [Fact]
    public void Matching_by_name_gives_it_only_to_the_box_of_the_same_name()
    {
        (ProjectModel p, TextObject source, LabelDocument other) = Project();

        Pairs(p, source, LayoutMatch.SameName).Select(x => x.Target).Should().Equal(other.Objects[0]);
        Pairs(p, source, LayoutMatch.EveryOfKind).Should().HaveCount(2);
    }

    [Fact]
    public void Chosen_parts_are_copied_the_words_stay_and_undo_puts_everything_back()
    {
        (ProjectModel p, TextObject source, LabelDocument other) = Project();

        IUndoableCommand? command = Format(p, source, TextStyleParts.Font | TextStyleParts.Size | TextStyleParts.Style | TextStyleParts.Colour);
        command.Should().NotBeNull();

        ((TextObject)other.Objects[0]).FontFamily.Should().Be("Segoe UI", "nothing changes until the command runs");
        command!.Do();
        var target = (TextObject)other.Objects[0];
        target.FontFamily.Should().Be("Arial");
        target.FontSizePt.Should().Be(14);
        target.Bold.Should().BeTrue();
        target.Foreground.Should().Be(source.Foreground);
        target.HorizontalAlignment.Should().Be(HorizontalAlignment.Left, "alignment was not ticked");
        target.Content.PlainText.Should().Be("Other title");
        ((TextObject)other.Objects[1]).FontFamily.Should().Be("Arial", "every text box takes the look");

        command.Undo();
        target = (TextObject)other.Objects[0];
        target.FontFamily.Should().Be("Segoe UI");
        target.FontSizePt.Should().Be(10);
        target.Bold.Should().BeFalse();
        target.Foreground.Should().Be(ColorRgba.Black);
    }

    [Fact]
    public void Per_word_formatting_of_a_copied_part_is_cleared_so_the_new_look_shows()
    {
        (ProjectModel p, TextObject source, LabelDocument other) = Project();
        var box = (TextObject)other.Objects[0];
        box.Content.Paragraphs[0].Runs[0].FontSizePt = 30;
        box.Content.Paragraphs[0].Runs[0].Italic = true;

        Format(p, source, TextStyleParts.Size)!.Do();

        TextRun run = ((TextObject)other.Objects[0]).Content.Paragraphs[0].Runs[0];
        run.FontSizePt.Should().BeNull();
        run.Italic.Should().BeTrue("style was not copied");
    }

    [Fact]
    public void A_grow_to_fit_box_grows_when_the_bigger_text_no_longer_fits()
    {
        (ProjectModel p, TextObject source, LabelDocument other) = Project();
        source.FontSizePt = 40;
        double before = other.Objects[0].Bounds.Height;

        IUndoableCommand command = Format(p, source, TextStyleParts.Size, measurer: new Measurer())!;
        command.Do();
        other.Objects[0].Bounds.Height.Should().BeGreaterThan(before);

        command.Undo();
        other.Objects[0].Bounds.Height.Should().Be(before);
    }

    [Fact]
    public void Nothing_to_change_gives_no_command()
    {
        (ProjectModel p, TextObject source, LabelDocument other) = Project();
        foreach (TextObject t in other.Objects.OfType<TextObject>())
        {
            t.FontFamily = source.FontFamily;
        }

        Format(p, source, TextStyleParts.Font).Should().BeNull();
    }

    [Fact]
    public void A_qr_code_never_takes_a_line_barcodes_look()
    {
        var p = new ProjectModel();
        var line = new BarcodeObject { Spec = new Core.Barcodes.BarcodeSpec { Symbology = Core.Barcodes.BarcodeSymbology.Code128, Data = "A1" } };
        p.MasterLabel.Objects.Add(line);
        var qr = new BarcodeObject { Spec = new Core.Barcodes.BarcodeSpec { Symbology = Core.Barcodes.BarcodeSymbology.QrCode, Data = "B2" } };
        var other = new BarcodeObject { Spec = new Core.Barcodes.BarcodeSpec { Symbology = Core.Barcodes.BarcodeSymbology.Code39, Data = "C3" } };
        p.Pages.Add(new LabelPage { Name = "Page 1", Document = new LabelDocument { Objects = { qr, other } } });

        LayoutCopy.Pairs(p, p.MasterLabel, new[] { line }, new[] { 1 }, new HashSet<ObjectKind> { ObjectKind.Barcode }, LayoutMatch.EveryOfKind)
            .Select(x => x.Target).Should().Equal(other);
    }
}
