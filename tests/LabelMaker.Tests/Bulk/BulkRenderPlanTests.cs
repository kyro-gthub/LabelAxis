using FluentAssertions;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Data;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using Xunit;

namespace LabelMaker.Tests.Bulk;

public sealed class BulkRenderPlanTests
{
    private static LabelTemplate Grid3x8 => TemplateStore.CreateWithBuiltIns().Get("formtec-gs-1024")!;

    private static DataSet MakeData(int rows)
    {
        var data = new List<string[]>();
        for (int i = 1; i <= rows; i++)
        {
            data.Add(new[] { $"Item {i}", $"ABC{i:000000}" });
        }

        return DataSet.FromRows(new[] { "Product", "Barcode" }, data);
    }

    [Fact]
    public void One_label_per_record_flows_across_sheets()
    {
        var plan = new BulkRenderPlan
        {
            Template = Grid3x8,
            MasterLabel = new LabelDocument(),
            Data = MakeData(50), // 24 per sheet -> 3 sheets
        };

        var labels = plan.Enumerate().ToList();
        labels.Should().HaveCount(50);
        labels[0].SheetIndex.Should().Be(0);
        labels[23].SheetIndex.Should().Be(0);
        labels[24].SheetIndex.Should().Be(1);
        labels[48].SheetIndex.Should().Be(2);
        plan.TotalSheets.Should().Be(3);
    }

    [Fact]
    public void Start_position_skips_used_labels_on_the_first_sheet_only()
    {
        var plan = new BulkRenderPlan
        {
            Template = Grid3x8,
            MasterLabel = new LabelDocument(),
            Data = MakeData(30),
            StartPosition = 7, // positions 1-6 already used
        };

        var labels = plan.Enumerate().ToList();
        labels[0].Slot.Index.Should().Be(6); // 0-based -> position 7
        labels.Take(18).Should().OnlyContain(l => l.SheetIndex == 0);
        labels[18].SheetIndex.Should().Be(1);
        labels[18].Slot.Index.Should().Be(0); // second sheet starts fresh
    }

    [Fact]
    public void Skip_positions_are_removed_from_the_first_sheet()
    {
        var plan = new BulkRenderPlan
        {
            Template = Grid3x8,
            MasterLabel = new LabelDocument(),
            Data = MakeData(10),
            SkipPositions = new HashSet<int> { 2, 4 },
        };

        var first10 = plan.Enumerate().Take(10).ToList();
        first10.Select(l => l.Slot.Index + 1).Should().NotContain(new[] { 2, 4 });
    }

    [Fact]
    public void Record_selection_limits_output()
    {
        var plan = new BulkRenderPlan
        {
            Template = Grid3x8,
            MasterLabel = new LabelDocument(),
            Data = MakeData(100),
            RecordSelection = new[] { 0, 5, 10, 99 },
        };

        var labels = plan.Enumerate().ToList();
        labels.Should().HaveCount(4);
        labels[3].RecordIndex.Should().Be(99);
    }

    [Fact]
    public void Resolve_context_exposes_record_and_position_metadata()
    {
        var plan = new BulkRenderPlan
        {
            Template = Grid3x8,
            MasterLabel = new LabelDocument(),
            Data = MakeData(5),
        };

        PlacedLabel third = plan.Enumerate().ElementAt(2);
        third.ResolveContext.RecordNumber.Should().Be(3);
        third.ResolveContext.RecordCount.Should().Be(5);
        third.ResolveContext.LabelPosition.Should().Be(3);
        third.CreateResolver().Resolve("{Product} #{RecordNumber}").Should().Be("Item 3 #3");
    }

    [Fact]
    public void No_data_fills_one_sheet_with_the_master_label()
    {
        var plan = new BulkRenderPlan
        {
            Template = Grid3x8,
            MasterLabel = new LabelDocument(),
            Data = DataSet.Empty,
            FillSheetWhenNoData = true,
        };

        var labels = plan.Enumerate().ToList();
        labels.Should().HaveCount(24);
        labels.Should().OnlyContain(l => l.RecordIndex == -1 && l.SheetIndex == 0);
    }

    [Fact]
    public void Handles_ten_thousand_records_without_materialising_them_all()
    {
        var plan = new BulkRenderPlan
        {
            Template = Grid3x8,
            MasterLabel = new LabelDocument(),
            Data = MakeData(10_000),
        };

        // Enumerate lazily; only touch the first few.
        int seen = 0;
        foreach (PlacedLabel _ in plan.Enumerate())
        {
            if (++seen >= 100)
            {
                break;
            }
        }

        seen.Should().Be(100);
        plan.TotalLabels.Should().Be(10_000);
    }
}
