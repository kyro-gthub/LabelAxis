using FluentAssertions;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using Xunit;

namespace LabelMaker.Tests.Bulk;

public sealed class MultiDocumentRenderPlanTests
{
    private static LabelTemplate Grid3x8 => TemplateStore.CreateWithBuiltIns().Get("formtec-3103")!; // 24-up

    private static List<LabelDocument> MakePages(int count) =>
        Enumerable.Range(1, count).Select(i => new LabelDocument { Name = $"Page {i}" }).ToList();

    [Fact]
    public void Pages_fill_slots_in_order_on_one_sheet()
    {
        var plan = new MultiDocumentRenderPlan { Template = Grid3x8, Documents = MakePages(5) };

        var placed = plan.Enumerate().ToList();
        placed.Should().HaveCount(5);
        placed.Should().OnlyContain(p => p.SheetIndex == 0);
        placed.Select(p => p.Slot.Index).Should().Equal(0, 1, 2, 3, 4);
        placed.Select(p => p.Document.Name).Should().Equal("Page 1", "Page 2", "Page 3", "Page 4", "Page 5");
    }

    [Fact]
    public void Overflow_pages_spill_onto_additional_sheets()
    {
        var plan = new MultiDocumentRenderPlan { Template = Grid3x8, Documents = MakePages(30) }; // 24/sheet

        var sheets = MultiDocumentSheetLayout.PlanSheets(plan).ToList();
        sheets.Should().HaveCount(2);
        sheets[0].Documents.Should().HaveCount(24);
        sheets[1].Documents.Should().HaveCount(6);
        sheets[1].Documents[0].Slot.Index.Should().Be(0); // fresh sheet starts at slot 0
    }

    [Fact]
    public void Start_position_skips_used_labels_on_the_first_sheet_only()
    {
        var plan = new MultiDocumentRenderPlan { Template = Grid3x8, Documents = MakePages(10), StartPosition = 7 };

        var placed = plan.Enumerate().ToList();
        placed[0].Slot.Index.Should().Be(6); // 0-based -> position 7
        placed.Take(18).Should().OnlyContain(p => p.SheetIndex == 0);
    }

    [Fact]
    public void No_pages_produces_no_output()
    {
        var plan = new MultiDocumentRenderPlan { Template = Grid3x8, Documents = Array.Empty<LabelDocument>() };
        plan.Enumerate().Should().BeEmpty();
        MultiDocumentSheetLayout.PlanSheets(plan).Should().BeEmpty();
    }
}
