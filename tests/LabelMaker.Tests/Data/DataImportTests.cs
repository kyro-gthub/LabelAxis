using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using LabelMaker.Core.Data;
using LabelMaker.Core.Data.Import;
using LabelMaker.Core.Model;
using Xunit;

namespace LabelMaker.Tests.Data;

public sealed class DataImportTests
{
    private const string SampleCsv =
        "Product,Barcode,Price,Description\n" +
        "Item A,100001,15.00,Product A\n" +
        "Item B,100002,20.00,Product B\n" +
        "Item C,100003,9.99,\"Has, comma\"\n";

    private static Stream Csv(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Csv_import_reads_headers_and_rows()
    {
        var service = new DataImportService();
        RawTable raw = service.ReadRaw(Csv(SampleCsv), DataSourceKind.Csv);
        var connection = new DataConnectionModel { Kind = DataSourceKind.Csv };

        DataSet data = service.BuildDataSet(raw, connection);

        data.Columns.Select(c => c.Name).Should().Equal("Product", "Barcode", "Price", "Description");
        data.RowCount.Should().Be(3);
        data.Rows[0]["Product"].Should().Be("Item A");
        data.Rows[2]["Description"].Should().Be("Has, comma");
    }

    [Fact]
    public void Csv_delimiter_autodetection_handles_semicolons()
    {
        var service = new DataImportService();
        RawTable raw = service.ReadRaw(Csv("a;b;c\n1;2;3\n"), DataSourceKind.Csv);
        DataSet data = service.BuildDataSet(raw, new DataConnectionModel());

        data.Columns.Should().HaveCount(3);
        data.Rows[0]["b"].Should().Be("2");
    }

    [Fact]
    public void Header_row_offset_and_excluded_rows_are_applied()
    {
        var service = new DataImportService();
        RawTable raw = service.ReadRaw(Csv("junk line\nProduct,Qty\nA,1\nB,2\nC,3\n"), DataSourceKind.Csv);
        var connection = new DataConnectionModel { HeaderRowIndex = 1, ExcludedRows = { 3 } }; // exclude "B,2"

        DataSet data = service.BuildDataSet(raw, connection);

        data.RowCount.Should().Be(2);
        data.Rows.Select(r => r["Product"]).Should().Equal("A", "C");
    }

    [Fact]
    public void Column_rename_and_filter_and_sort()
    {
        var service = new DataImportService();
        RawTable raw = service.ReadRaw(Csv(SampleCsv), DataSourceKind.Csv);
        var connection = new DataConnectionModel
        {
            ColumnRenames = { ["Description"] = "Notes" },
            Filters = { new DataFilter { Column = "Price", Operator = DataFilterOperator.GreaterThan, Value = "10" } },
            SortColumn = "Price",
            SortDescending = true,
        };

        DataSet data = service.BuildDataSet(raw, connection);

        data.HasColumn("Notes").Should().BeTrue();
        data.RowCount.Should().Be(2);
        data.Rows[0]["Product"].Should().Be("Item B"); // 20.00 first, descending
    }

    [Fact]
    public void Preview_reports_duplicates_per_column()
    {
        var service = new DataImportService();
        RawTable raw = service.ReadRaw(Csv("Code\nA\nA\nB\n"), DataSourceKind.Csv);

        DataPreview preview = service.BuildPreview(raw, new DataConnectionModel());

        preview.TotalDataRows.Should().Be(3);
        preview.DuplicateCountsByColumn.Should().ContainKey("Code");
    }

    [Fact]
    public void Xlsx_import_round_trips_a_generated_workbook()
    {
        using var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            IXLWorksheet ws = wb.AddWorksheet("Labels");
            ws.Cell(1, 1).Value = "Product";
            ws.Cell(1, 2).Value = "Barcode";
            ws.Cell(2, 1).Value = "Widget";
            ws.Cell(2, 2).Value = 100001;
            ws.Cell(3, 1).Value = "Gadget";
            ws.Cell(3, 2).Value = 100002;
            wb.SaveAs(ms);
        }

        ms.Position = 0;
        var service = new DataImportService();
        RawTable raw = service.ReadRaw(ms, DataSourceKind.Xlsx);
        DataSet data = service.BuildDataSet(raw, new DataConnectionModel { Kind = DataSourceKind.Xlsx });

        data.Columns.Select(c => c.Name).Should().Equal("Product", "Barcode");
        data.RowCount.Should().Be(2);
        data.Rows[1]["Product"].Should().Be("Gadget");
        data.Rows[0]["Barcode"].Should().Be("100001");
    }

    [Fact]
    public void Clipboard_tsv_paste_becomes_a_dataset()
    {
        RawTable raw = DataImportService.ReadClipboardText("Name\tCity\nAda\tLondon\nGrace\tNYC");
        DataSet data = new DataImportService().BuildDataSet(raw, new DataConnectionModel { Kind = DataSourceKind.Clipboard });

        data.RowCount.Should().Be(2);
        data.Rows[0]["City"].Should().Be("London");
    }

    [Fact]
    public void Column_type_inference()
    {
        DataSet data = DataSet.FromRows(
            new[] { "n", "d", "t" },
            new[]
            {
                new[] { "1", "1.5", "hello" },
                new[] { "2", "2.5", "world" },
            });

        data.Columns[0].InferredType.Should().Be(DataColumnType.Integer);
        data.Columns[1].InferredType.Should().Be(DataColumnType.Decimal);
        data.Columns[2].InferredType.Should().Be(DataColumnType.Text);
    }
}
