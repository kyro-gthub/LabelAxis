using FluentAssertions;
using LabelMaker.Core.Data;
using LabelMaker.Core.Data.Import;
using LabelMaker.Core.Model;
using Xunit;

namespace LabelMaker.Tests.Data;

public sealed class DataSetEditingTests
{
    private static DataSet Make(int rows) =>
        DataSet.FromRows(new[] { "Sku", "Name" }, Enumerable.Range(1, rows).Select(i => new[] { $"S{i}", $"Item {i}" }));

    [Fact]
    public void Editing_a_cell_changes_the_record_and_notifies_bindings()
    {
        DataSet data = Make(3);
        DataRecord record = data.Rows[1];
        var changed = new List<string?>();
        record.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        string old = data.SetValue(record, 1, "Renamed");

        old.Should().Be("Item 2");
        record["Name"].Should().Be("Renamed");
        changed.Should().Contain(new[] { "Item[]", "RawValues" });
    }

    [Fact]
    public void Removing_many_rows_and_restoring_them_puts_every_row_back_in_place()
    {
        DataSet data = Make(10);
        List<DataRecord> original = data.Rows.ToList();
        DataRecord[] doomed = { original[0], original[4], original[5], original[9] };

        var removed = data.RemoveRows(doomed);

        data.Rows.Select(r => r["Sku"]).Should().Equal("S2", "S3", "S4", "S7", "S8", "S9");
        removed.Select(r => r.Index).Should().Equal(0, 4, 5, 9);

        data.RestoreRows(removed);
        data.Rows.Should().Equal(original);
    }

    [Fact]
    public void New_records_are_empty_and_numbered_after_the_last_row()
    {
        DataSet data = Make(2);

        DataRecord record = data.CreateRecord();
        data.InsertRow(1, record);

        record.SourceRowNumber.Should().Be(3);
        record["Sku"].Should().BeEmpty();
        data.Rows.Select(r => r["Sku"]).Should().Equal("S1", "", "S2");
        data.IndexOf(record).Should().Be(1);
    }

    [Fact]
    public void Csv_snapshot_reads_back_to_the_same_records()
    {
        DataSet data = DataSet.FromRows(new[] { "Sku", "Note" }, new[]
        {
            new[] { "A1", "plain" },
            new[] { "A2", "has, comma" },
            new[] { "A3", "has \"quotes\"" },
            new[] { "A4", "two\nlines" },
            new[] { "A5", "semi;colons;everywhere;here" },
        });

        string csv = data.ToCsv();
        var service = new DataImportService();
        RawTable raw = service.ReadRaw(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv)), DataSourceKind.Csv,
            new TabularReadOptions { Delimiter = "," });
        DataSet back = service.BuildDataSet(raw, new DataConnectionModel { Kind = DataSourceKind.Csv });

        back.Columns.Select(c => c.Name).Should().Equal("Sku", "Note");
        back.Rows.Select(r => r["Note"]).Should().Equal(data.Rows.Select(r => r["Note"]));
    }
}
