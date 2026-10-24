using FluentAssertions;
using LabelMaker.App.Services;
using LabelMaker.App.Views;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Data;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using Xunit;

namespace LabelMaker.App.Tests.ViewModels;

/// <summary>
/// Pages made in one go (from records, numbered copies), the page list after a duplicate, print
/// settings changed in a print window, and what a barcode scanner types.
/// </summary>
public sealed class MakePagesTests
{
    private static void UseRecords(AppHarness h, params string[] products) => h.Session.SetData(
        DataSet.FromRows(new[] { "Product", "Barcode" }, products.Select((p, i) => new[] { p, $"P{i + 1:000}" }).ToList()),
        new DataConnectionModel { Name = "stock.xlsx", Kind = DataSourceKind.Xlsx });

    private static void PutOnLabel(AppHarness h, params LabelObject[] objects)
    {
        h.Session.ActiveLabel.Objects.Clear(); // the test project starts with sample objects
        foreach (LabelObject obj in objects)
        {
            h.Session.ActiveLabel.Objects.Add(obj);
        }
    }

    [Fact]
    public void Imported_records_become_one_page_each_with_their_values_and_print_as_pages() => AppHarness.Run(h =>
    {
        UseRecords(h, "Cold brew coffee", "Olive oil", "Wildflower honey");
        PutOnLabel(h,
            new TextObject { Name = "Name", Content = RichTextContent.FromPlainText("{Product}") },
            new BarcodeObject { Name = "Code", Spec = new BarcodeSpec { Data = "{Barcode}" } });
        h.Shell.ConfirmAnswer = true;

        h.ViewModel.PagesFromRecordsCommand.Execute(null);

        h.Session.Project.Pages.Select(p => p.Name).Should().Equal("Cold brew coffee", "Olive oil", "Wildflower honey");
        h.Session.Project.Pages[1].Document.Objects.OfType<TextObject>().Single().Content.PlainText.Should().Be("Olive oil");
        h.Session.Project.Pages[2].Document.Objects.OfType<BarcodeObject>().Single().Spec.Data.Should().Be("P003");
        h.Session.Project.PrintSettings.Content.Should().Be(PrintContent.Pages);

        h.Session.Undo();
        h.Session.Project.Pages.Should().BeEmpty("making the pages is one step that undo takes back");
    });

    [Fact]
    public void A_label_without_any_of_the_records_columns_says_so_instead_of_making_identical_pages() => AppHarness.Run(h =>
    {
        UseRecords(h, "Cold brew coffee", "Olive oil");
        PutOnLabel(h, new TextObject { Name = "Name", Content = RichTextContent.FromPlainText("Fixed text") });

        h.ViewModel.MakePagesFromRecords(new[] { 0, 1 }).Should().Be(0);

        h.Session.Project.Pages.Should().BeEmpty();
        h.Shell.Messages.Should().Contain(m => m.Contains("does not use any of the records' columns"));
    });

    [Fact]
    public void Numbered_copies_follow_the_page_with_their_barcodes_counting_up() => AppHarness.Run(h =>
    {
        PutOnLabel(h, new BarcodeObject { Name = "Code", Spec = new BarcodeSpec { Data = "BOX-0007" } });
        h.Shell.PromptAnswer = "3";

        h.ViewModel.NumberedPagesCommand.Execute(null);

        h.Session.Project.Pages.Select(p => p.Name).Should().Equal("BOX-0008", "BOX-0009", "BOX-0010");
        h.ViewModel.SelectedPage!.Name.Should().Be("BOX-0008", "the first new page is the one on screen");
    });

    [Fact]
    public void A_page_duplicated_in_the_middle_appears_right_after_it_and_is_selected() => AppHarness.Run(h =>
    {
        h.ViewModel.AddPageCommand.Execute(null);
        h.ViewModel.AddPageCommand.Execute(null);
        h.ViewModel.AddPageCommand.Execute(null);
        h.ViewModel.SelectedPage = h.ViewModel.Pages[1];
        string first = h.ViewModel.Pages[1].Name;

        h.ViewModel.DuplicatePageCommand.Execute(null);

        h.ViewModel.Pages.Should().HaveCount(5);
        h.ViewModel.Pages[2].Name.Should().Be(first + " copy");
        h.ViewModel.SelectedPage.Should().Be(h.ViewModel.Pages[2]);
        h.ViewModel.Pages.Select(p => p.Number).Should().Equal(1, 2, 3, 4, 5);
    });

    [Fact]
    public void Print_settings_changed_in_a_print_window_mark_the_project_to_be_saved() => AppHarness.Run(h =>
    {
        h.Session.Document.HasUnsavedChanges = false;
        string before = PrintSettingsWatch.Snapshot(h.Session.Project.PrintSettings);

        PrintSettingsWatch.MarkIfChanged(h.Session, before).Should().BeFalse("nothing changed");
        h.Session.Document.HasUnsavedChanges.Should().BeFalse();

        h.Session.Project.PrintSettings.Layout.CustomMargins = true;
        h.Session.Project.PrintSettings.Layout.MarginTopMm = 12.5;

        PrintSettingsWatch.MarkIfChanged(h.Session, before).Should().BeTrue();
        h.Session.Document.HasUnsavedChanges.Should().BeTrue("so closing asks to save, and auto-save keeps it");
    });

    [Theory]
    [InlineData("MILK001", "MILK001")]
    [InlineData("  4006381333931\r\n", "4006381333931")]
    [InlineData("]E04006381333931", "4006381333931")] // an AIM symbology code in front
    [InlineData("]C1010400638133393121ABC", "010400638133393121ABC")]
    public void What_a_scanner_types_is_cleaned_before_it_is_compared(string typed, string expected) =>
        BarcodeCheckWindow.CleanScan(typed).Should().Be(expected);
}
