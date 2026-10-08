using FluentAssertions;
using LabelMaker.App.Services;
using LabelMaker.App.ViewModels;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.App.Tests.ViewModels;

/// <summary>The Properties panel: what typing into each box does to the selected object.</summary>
public sealed class SelectionPropertiesTests
{
    [Fact]
    public void The_panel_shows_nothing_without_a_selection() => AppHarness.Run(h =>
    {
        h.Session.ClearSelection();
        SelectionProperties p = h.ViewModel.Properties;

        p.HasSelection.Should().BeFalse();
        p.TypeName.Should().Be("—");
        p.IsText.Should().BeFalse();
    });

    [Fact]
    public void Typing_a_width_changes_the_box_and_undo_puts_it_back() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        SelectionProperties p = h.ViewModel.Properties;
        LabelObject text = h.Session.Selection.Single();
        double width = text.Bounds.Width, height = text.Bounds.Height;

        p.Width = 33;

        text.Bounds.Width.Should().Be(33);
        text.Bounds.Height.Should().Be(height, "without the lock only the width changes");
        h.Session.Undo();
        text.Bounds.Width.Should().Be(width);
    });

    [Fact]
    public void With_the_lock_on_the_box_keeps_its_proportions() => AppHarness.Run(h =>
    {
        h.ViewModel.AddRectangleCommand.Execute(null);
        SelectionProperties p = h.ViewModel.Properties;
        LabelObject shape = h.Session.Selection.Single();
        double aspect = shape.Bounds.Width / shape.Bounds.Height;

        p.LockAspect = true;
        p.Height = shape.Bounds.Height * 2;

        (shape.Bounds.Width / shape.Bounds.Height).Should().BeApproximately(aspect, 1e-9);
    });

    [Fact]
    public void Lengths_are_shown_and_typed_in_the_unit_chosen_in_the_settings() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        SelectionProperties p = h.ViewModel.Properties;
        LabelObject text = h.Session.Selection.Single();
        double widthMm = text.Bounds.Width;

        h.Environment.Settings.DisplayUnit = Unit.Cm;
        p.NotifyUnitChanged();
        p.Width.Should().BeApproximately(widthMm / 10, 0.01);

        p.Width = 2.5;
        text.Bounds.Width.Should().BeApproximately(25, 1e-9, "storage stays in millimetres");
    });

    [Fact]
    public void A_width_too_small_to_see_is_kept_at_a_tenth_of_a_millimetre() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);

        h.ViewModel.Properties.Width = -5;

        h.Session.Selection.Single().Bounds.Width.Should().Be(0.1);
    });

    [Fact]
    public void Ticking_bold_also_clears_words_that_were_unbolded_one_by_one() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        var text = (TextObject)h.Session.Selection.Single();
        text.Content = new RichTextContent
        {
            Paragraphs = { new Paragraph { Runs = { new TextRun { Text = "Price ", Bold = false }, new TextRun { Text = "9.99" } } } },
        };

        h.ViewModel.Properties.Bold = true;

        text.Bold.Should().BeTrue();
        text.Content.Paragraphs.SelectMany(p => p.Runs).Should().OnlyContain(r => r.Bold == null);
        h.Session.Undo();
        text.Bold.Should().BeFalse();
        text.Content.Paragraphs[0].Runs[0].Bold.Should().BeFalse("one undo step brings back both");
    });

    [Fact]
    public void Typing_in_the_text_field_keeps_a_coloured_word_coloured() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        var text = (TextObject)h.Session.Selection.Single();
        var red = new ColorRgba(200, 0, 0);
        text.Content = new RichTextContent
        {
            Paragraphs = { new Paragraph { Runs = { new TextRun { Text = "Now " }, new TextRun { Text = "9.99", Foreground = red } } } },
        };

        h.ViewModel.Properties.TextContent = "Now 8.99";

        text.Content.PlainText.Should().Be("Now 8.99");
        text.Content.Paragraphs[0].Runs.Should().Contain(r => r.Foreground == red);
    });

    [Fact]
    public void Adding_a_column_to_a_table_keeps_each_cell_in_its_row() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTableCommand.Execute(null);
        var table = (TableObject)h.Session.Selection.Single();

        h.ViewModel.Properties.TableColumns = 3;

        table.Columns.Should().Be(3);
        table.Cells.Should().Equal("Item", "Value", "", "Size", "{Size}", "", "Batch", "{Batch}", "");
        h.ViewModel.Properties.TableCells.Should().HaveCount(9);
        h.ViewModel.Properties.TableCells[0].IsHeader.Should().BeTrue();
        h.ViewModel.Properties.TableCells[3].IsHeader.Should().BeFalse();
    });

    [Fact]
    public void Removing_a_row_drops_the_last_row_only() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTableCommand.Execute(null);
        var table = (TableObject)h.Session.Selection.Single();

        h.ViewModel.Properties.TableRows = 2;

        table.Rows.Should().Be(2);
        table.Cells.Should().Equal("Item", "Value", "Size", "{Size}");
    });

    [Fact]
    public void A_hex_colour_that_is_not_one_is_ignored() => AppHarness.Run(h =>
    {
        h.ViewModel.AddRectangleCommand.Execute(null);
        var shape = (ShapeObject)h.Session.Selection.Single();
        ColorRgba before = shape.Fill;

        h.ViewModel.Properties.FillHex = "not a colour";
        shape.Fill.Should().Be(before);

        h.ViewModel.Properties.FillHex = "#336699";
        shape.Fill.Should().Be(new ColorRgba(0x33, 0x66, 0x99));
    });

    [Fact]
    public void A_gradient_from_the_colour_window_is_one_undo_step() => AppHarness.Run(h =>
    {
        h.ViewModel.AddRectangleCommand.Execute(null);
        var shape = (ShapeObject)h.Session.Selection.Single();
        ColorRgba fill = shape.Fill;
        var to = new ColorRgba(255, 255, 255);

        h.ViewModel.Properties.ApplyFill("fill", new FillChoice(new ColorRgba(10, 20, 30), to, -90));

        shape.Fill.Should().Be(new ColorRgba(10, 20, 30));
        shape.GradientTo.Should().Be(to);
        shape.GradientAngleDegrees.Should().Be(270, "the direction is kept as 0 to 359 degrees");
        h.Session.Undo();
        shape.Fill.Should().Be(fill);
        shape.GradientTo.Should().BeNull();
    });

    [Fact]
    public void Switching_the_gradient_on_over_no_fill_starts_from_a_colour_that_shows() => AppHarness.Run(h =>
    {
        h.ViewModel.AddRectangleCommand.Execute(null);
        var shape = (ShapeObject)h.Session.Selection.Single();
        shape.Fill = ColorRgba.Transparent;

        h.ViewModel.Properties.UseGradient = true;

        shape.Fill.IsTransparent.Should().BeFalse();
        shape.HasGradient.Should().BeTrue();
    });

    [Fact]
    public void Barcode_options_follow_the_kind_of_barcode() => AppHarness.Run(h =>
    {
        h.ViewModel.AddQrCodeCommand.Execute(null);
        SelectionProperties p = h.ViewModel.Properties;
        p.IsQr.Should().BeTrue();
        p.Is2D.Should().BeTrue();
        p.IsLinear.Should().BeFalse();
        p.CanBeGs1.Should().BeTrue();

        h.ViewModel.AddBarcodeCommand.Execute(null);
        p.IsQr.Should().BeFalse();
        p.IsLinear.Should().BeTrue();
    });

    [Fact]
    public void A_barcode_value_it_cannot_hold_is_explained() => AppHarness.Run(h =>
    {
        h.ViewModel.AddBarcodeCommand.Execute(null);
        SelectionProperties p = h.ViewModel.Properties;

        p.Symbology = BarcodeSymbology.Ean13;
        p.BarcodeData = "12AB";

        p.BarcodeValidationMessage.Should().NotBeNullOrWhiteSpace();
    });
}
