using System.IO;
using FluentAssertions;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Tests.Support;
using Xunit;

namespace LabelMaker.App.Tests.ViewModels;

/// <summary>
/// Saving and opening projects, reading records, pictures and PDF export, through the main
/// view model with the file boxes answered by the test. Everything is written to the harness's
/// own folder.
/// </summary>
public sealed class FilesAndDataTests
{
    [Fact]
    public void A_saved_project_opens_again_with_its_objects_and_pages() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        h.Session.Selection.Single().Name = "Price";
        h.ViewModel.DuplicatePageCommand.Execute(null);
        string path = h.FileIn("shop.labelproj");
        h.Shell.SaveFileAnswer = path;

        h.ViewModel.SaveAsCommand.Execute(null);

        File.Exists(path).Should().BeTrue();
        h.ViewModel.StatusText.Should().Be("Saved shop.labelproj");
        h.ViewModel.HasUnsavedChanges.Should().BeFalse();
        h.Environment.Settings.RecentProjects.Should().Contain(r => r.Path == path);

        h.ViewModel.CloseTab(h.ViewModel.ActiveTab);
        h.ViewModel.OpenProjectPath(path);

        h.ViewModel.StatusText.Should().Be("Opened shop.labelproj");
        h.Session.Project.Pages.Should().ContainSingle();
        h.Session.Project.MasterLabel.Objects.Should().Contain(o => o.Name == "Price");
    });

    [Fact]
    public void Opening_a_project_that_is_already_open_brings_its_tab_forward() => AppHarness.Run(h =>
    {
        string path = h.FileIn("one.labelproj");
        h.Shell.SaveFileAnswer = path;
        h.ViewModel.SaveAsCommand.Execute(null);

        h.ViewModel.OpenProjectPath(path);

        h.ViewModel.StatusText.Should().Be("one.labelproj is already open");
    });

    [Fact]
    public void A_damaged_project_file_is_explained_not_opened() => AppHarness.Run(h =>
    {
        string path = h.FileIn("broken.labelproj");
        File.WriteAllText(path, "this is not a project");

        h.ViewModel.OpenProjectPath(path);

        h.Shell.Warnings.Should().ContainSingle().Which.Should().StartWith("Could not open the project");
    });

    [Fact]
    public void Saving_where_no_file_can_be_written_is_explained() => AppHarness.Run(h =>
    {
        // A file stands where the folder would have to be made.
        string blocker = h.FileIn("blocker");
        File.WriteAllText(blocker, string.Empty);
        h.Shell.SaveFileAnswer = Path.Combine(blocker, "x.labelproj");

        h.ViewModel.SaveAsCommand.Execute(null);

        h.Shell.Warnings.Should().ContainSingle().Which.Should().StartWith("Save failed");
    });

    [Fact]
    public void Cancelling_the_save_box_saves_nothing() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        h.Shell.SaveFileAnswer = null;

        h.ViewModel.SaveAsCommand.Execute(null);

        h.ViewModel.HasUnsavedChanges.Should().BeTrue();
        h.Shell.Warnings.Should().BeEmpty();
    });

    [Fact]
    public void Records_from_a_csv_file_fill_the_label_one_by_one() => AppHarness.Run(h =>
    {
        string csv = h.FileIn("stock.csv");
        File.WriteAllText(csv, "Product,Barcode\nMilk,ABC001\nBread,ABC002\nEggs,ABC002\n");
        h.Shell.OpenFileAnswer = csv;

        h.ViewModel.ImportDataCommand.Execute(null);

        h.ViewModel.StatusText.Should().Be("Imported 3 records, 2 columns from stock.csv");
        h.Session.RecordCount.Should().Be(3);
        h.ViewModel.HasData.Should().BeTrue();

        h.ViewModel.NextRecordCommand.Execute(null);
        h.Session.RecordIndex.Should().Be(1);
        h.ViewModel.LastRecordCommand.Execute(null);
        h.Session.RecordIndex.Should().Be(2);
        h.ViewModel.FirstRecordCommand.Execute(null);
        h.Session.RecordIndex.Should().Be(0);

        h.ViewModel.DuplicateCheckCommand.Execute(null);
        h.Shell.Messages.Should().Contain(m => m.Contains("ABC002"), "the same barcode twice is reported");
    });

    [Fact]
    public void Records_can_be_edited_added_and_deleted_with_undo() => AppHarness.Run(h =>
    {
        string csv = h.FileIn("stock.csv");
        File.WriteAllText(csv, "Product,Price\nMilk,1.25\nBread,2.10\n");
        h.Shell.OpenFileAnswer = csv;
        h.ViewModel.ImportDataCommand.Execute(null);
        Core.Data.DataSet data = h.Session.Data!;

        h.ViewModel.EditRecordCell(data.Rows[0], 1, "1.35");
        data.Rows[0]["Price"].Should().Be("1.35");
        h.Session.Undo();
        data.Rows[0]["Price"].Should().Be("1.25");

        h.ViewModel.AddRecord(2).Should().NotBeNull();
        data.RowCount.Should().Be(3);

        h.ViewModel.DeleteRecords(new[] { data.Rows[0], data.Rows[1] });
        data.RowCount.Should().Be(1);
        h.ViewModel.StatusText.Should().Be("Deleted 2 records");
    });

    [Fact]
    public void Clearing_the_records_asks_first_and_keeps_them_on_no() => AppHarness.Run(h =>
    {
        string csv = h.FileIn("stock.csv");
        File.WriteAllText(csv, "Product\nMilk\n");
        h.Shell.OpenFileAnswer = csv;
        h.ViewModel.ImportDataCommand.Execute(null);

        h.Shell.ConfirmAnswer = false;
        h.ViewModel.ClearDataCommand.Execute(null);
        h.Session.Data.Should().NotBeNull();

        h.Shell.ConfirmAnswer = true;
        h.ViewModel.ClearDataCommand.Execute(null);
        h.Session.Data.Should().BeNull();
    });

    [Fact]
    public void A_file_that_cannot_be_read_as_records_is_explained() => AppHarness.Run(h =>
    {
        h.Shell.OpenFileAnswer = h.FileIn("missing.csv");

        h.ViewModel.ImportDataCommand.Execute(null);

        h.Shell.Warnings.Should().ContainSingle().Which.Should().StartWith("Import failed");
        h.Session.Data.Should().BeNull();
    });

    [Fact]
    public void A_picture_is_added_at_its_own_proportions_and_a_non_picture_is_refused() => AppHarness.Run(h =>
    {
        string png = h.FileIn("logo.png");
        File.WriteAllBytes(png, TestImages.Png(40, 20));
        h.Shell.OpenFileAnswer = png;

        h.ViewModel.AddImageCommand.Execute(null);

        var image = h.Session.Selection.Single().Should().BeOfType<ImageObject>().Subject;
        image.SourceFileName.Should().Be("logo.png");
        image.SourcePixelWidth.Should().Be(40);
        (image.Bounds.Width / image.Bounds.Height).Should().BeApproximately(2, 0.01);
        h.Session.Document.Assets.Contains(image.AssetId).Should().BeTrue();

        string notPicture = h.FileIn("notes.png");
        File.WriteAllText(notPicture, "plain words");
        h.Shell.OpenFileAnswer = notPicture;
        int objects = h.Session.ActiveLabel.Objects.Count;

        h.ViewModel.AddImageCommand.Execute(null);

        h.Shell.Warnings.Should().ContainSingle().Which.Should().Contain("notes.png");
        h.Session.ActiveLabel.Objects.Should().HaveCount(objects);
    });

    [Fact]
    public void A_tiff_picture_keeps_its_proportions_too() => AppHarness.Run(h =>
    {
        // LabelAxis reads the size of PNG, JPEG, GIF and BMP itself; TIFF goes through Windows.
        var pixels = System.Windows.Media.Imaging.BitmapSource.Create(
            30, 10, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[30 * 10 * 4], 30 * 4);
        var encoder = new System.Windows.Media.Imaging.TiffBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(pixels));
        string tiff = h.FileIn("scan.tif");
        using (FileStream file = File.Create(tiff))
        {
            encoder.Save(file);
        }

        h.Shell.OpenFileAnswer = tiff;
        h.ViewModel.AddImageCommand.Execute(null);

        var image = h.Session.Selection.Single().Should().BeOfType<ImageObject>().Subject;
        image.SourcePixelWidth.Should().Be(30);
        image.SourcePixelHeight.Should().Be(10);
        (image.Bounds.Width / image.Bounds.Height).Should().BeApproximately(3, 0.01);
    });

    [Fact]
    public void The_sheet_exports_to_a_pdf_and_is_logged_in_the_print_history() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        string pdf = h.FileIn("sheet.pdf");
        h.Shell.SaveFileAnswer = pdf;

        h.ViewModel.ExportSheetPdfCommand.Execute(null);

        h.Shell.Warnings.Should().BeEmpty();
        File.ReadAllBytes(pdf).Take(5).Should().Equal("%PDF-"u8.ToArray());
        h.ViewModel.StatusText.Should().StartWith("Exported 1 sheet(s)");
        h.Environment.PrintHistory.Entries.Should().ContainSingle();
    });

    [Fact]
    public void The_label_exports_to_svg() => AppHarness.Run(h =>
    {
        h.ViewModel.AddBarcodeCommand.Execute(null);
        string svg = h.FileIn("label.svg");
        h.Shell.SaveFileAnswer = svg;

        h.ViewModel.ExportLabelSvgCommand.Execute(null);

        h.Shell.Warnings.Should().BeEmpty();
        File.ReadAllText(svg).Should().StartWith("<svg");
    });

    [Fact]
    public void Preflight_reports_an_object_that_sticks_out_of_the_label() => AppHarness.Run(h =>
    {
        h.ViewModel.AddTextCommand.Execute(null);
        LabelObject text = h.Session.Selection.Single();
        text.Bounds = text.Bounds with { X = h.Session.ActiveLabel.SizeMm.Width - 2 };

        h.ViewModel.PreflightCommand.Execute(null);

        h.Shell.Messages.Should().NotBeEmpty();
        string.Join("\n", h.Shell.Messages).Should().Contain("'Text' extends past the label edge");
    });
}
