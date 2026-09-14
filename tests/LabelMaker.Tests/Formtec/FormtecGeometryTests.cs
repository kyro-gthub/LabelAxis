using FluentAssertions;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Formtec;

public sealed class FormtecGeometryTests
{
    private static readonly TemplateStore Store = TemplateStore.CreateWithBuiltIns();

    [Fact]
    public void All_builtin_templates_load_and_validate()
    {
        Store.LoadErrors.Should().BeEmpty();
        Store.All.Should().NotBeEmpty();

        foreach (LabelTemplate template in Store.All)
        {
            TemplateValidationResult result = TemplateValidator.Validate(template);
            result.HasErrors.Should().BeFalse(
                $"{template.Id} should be geometrically valid: {string.Join("; ", result.Issues.Select(i => i.Message))}");
        }
    }

    [Fact]
    public void Formtec_gs_1065_matches_words_label_definition()
    {
        // Word's Mailings > Labels definition: 38 x 21.15 mm, 5 x 13, first label 10 mm in and 11 mm down, no gaps.
        LabelTemplate t = Store.Get("formtec-gs-1065")!;

        RectMm first = TemplateGeometry.SlotRect(t, 0);
        first.X.Should().BeApproximately(10, 1e-6);
        first.Y.Should().BeApproximately(11, 1e-6);
        first.Width.Should().BeApproximately(38, 1e-6);
        first.Height.Should().BeApproximately(21.15, 1e-6);

        RectMm last = TemplateGeometry.SlotRect(t, t.Grid.Count - 1); // row 13, column 5
        last.X.Should().BeApproximately(10 + (4 * 38), 1e-6);
        last.Y.Should().BeApproximately(11 + (12 * 21.15), 1e-6);
        last.Right.Should().BeApproximately(200, 1e-6, "labels butt together, leaving 10 mm each side");
    }
    [Fact]
    public void Row_major_index_maps_left_to_right_then_top_to_bottom()
    {
        LabelTemplate t = Store.Get("formtec-gs-1024")!; // 3 cols x 8 rows

        TemplateGeometry.IndexToRowColumn(t, 0).Should().Be((0, 0));
        TemplateGeometry.IndexToRowColumn(t, 2).Should().Be((0, 2));
        TemplateGeometry.IndexToRowColumn(t, 3).Should().Be((1, 0));
        TemplateGeometry.IndexToRowColumn(t, 23).Should().Be((7, 2));
    }

    [Fact]
    public void Column_major_fill_order_changes_index_mapping()
    {
        LabelTemplate row = Store.Get("formtec-gs-1024")!;
        LabelTemplate col = row with { Id = "test-colmajor", FillOrder = SheetFillOrder.ColumnMajor };

        TemplateGeometry.IndexToRowColumn(col, 1).Should().Be((1, 0));
        TemplateGeometry.IndexToRowColumn(col, 8).Should().Be((0, 1));
    }

    [Fact]
    public void Every_label_stays_within_the_physical_page()
    {
        foreach (LabelTemplate t in Store.All)
        {
            PageSize page = t.EffectivePage;
            foreach (LabelSlot slot in TemplateGeometry.AllSlots(t))
            {
                slot.RectMm.Right.Should().BeLessThanOrEqualTo(page.WidthMm + 0.51, $"{t.Id} {slot.PositionLabel}");
                slot.RectMm.Bottom.Should().BeLessThanOrEqualTo(page.HeightMm + 0.51, $"{t.Id} {slot.PositionLabel}");
                slot.RectMm.X.Should().BeGreaterThanOrEqualTo(-0.01);
                slot.RectMm.Y.Should().BeGreaterThanOrEqualTo(-0.01);
            }
        }
    }

    [Fact]
    public void Printable_slots_can_start_partway_through_a_sheet()
    {
        LabelTemplate t = Store.Get("formtec-gs-1024")!; // 24 labels

        var slots = TemplateGeometry.PrintableSlots(t, startPosition: 7);
        slots.Should().HaveCount(18);
        slots[0].Index.Should().Be(6);
    }

    [Fact]
    public void Printable_slots_honour_skipped_positions()
    {
        LabelTemplate t = Store.Get("formtec-gs-1065")!; // 65 labels

        var skip = new HashSet<int> { 1, 2, 3 };
        var slots = TemplateGeometry.PrintableSlots(t, startPosition: 1, skipPositions: skip);

        slots.Should().HaveCount(62);
        slots.Select(s => s.Index + 1).Should().NotContain(new[] { 1, 2, 3 });
    }

    [Fact]
    public void Templates_export_and_reimport_via_csv_unchanged()
    {
        string csv = Store.ExportCsv();
        var roundTrip = new TemplateStore();
        var imported = roundTrip.ImportCsv(csv, markUserDefined: false);

        imported.Should().HaveCount(Store.All.Count);
        LabelTemplate original = Store.Get("formtec-3642")!;
        LabelTemplate back = roundTrip.Get("formtec-3642")!;
        back.Grid.LabelWidthMm.Should().BeApproximately(original.Grid.LabelWidthMm, 1e-6);
        back.Grid.Rows.Should().Be(original.Grid.Rows);
        back.Grid.LabelShape.Should().Be(original.Grid.LabelShape);
    }
}
