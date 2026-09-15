using FluentAssertions;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Printing;

public sealed class PrintLayoutTests
{
    private static readonly TemplateStore Store = TemplateStore.CreateWithBuiltIns();

    [Fact]
    public void The_template_layout_is_used_unchanged_by_default()
    {
        LabelTemplate t = Store.Get("formtec-gs-1189")!;
        PrintLayoutResolver.Resolve(t, new PrintLayout(), landscape: false, t.LabelSizeMm).Should().BeSameAs(t);
    }

    [Fact]
    public void Custom_margins_fit_as_many_labels_as_the_paper_holds()
    {
        LabelTemplate t = Store.Get("formtec-gs-1189")!;
        var layout = new PrintLayout
        {
            CustomMargins = true, MarginTopMm = 10, MarginBottomMm = 10, MarginLeftMm = 10, MarginRightMm = 10,
            HGapMm = 5, VGapMm = 5, CenterOnPaper = false,
        };

        LabelTemplate r = PrintLayoutResolver.Resolve(t, layout, landscape: false, new SizeMm(60, 40));

        // 190 mm across: 60+5+60+5+60 = 190 → 3; 277 mm down: 6 × 40 + 5 × 5 = 265 → 6.
        r.Grid.Columns.Should().Be(3);
        r.Grid.Rows.Should().Be(6);
        r.Grid.MarginLeftMm.Should().Be(10);
        r.Grid.MarginTopMm.Should().Be(10);
        r.Grid.LabelWidthMm.Should().Be(60);
    }

    [Fact]
    public void Centring_splits_the_spare_room_and_landscape_paper_turns_the_sheet()
    {
        LabelTemplate t = Store.Get("formtec-gs-1189")!;
        var layout = new PrintLayout { PaperSize = "A5", CustomMargins = true, MarginTopMm = 0, MarginBottomMm = 0, MarginLeftMm = 0, MarginRightMm = 0, HGapMm = 0, VGapMm = 0 };

        LabelTemplate r = PrintLayoutResolver.Resolve(t, layout, landscape: true, new SizeMm(100, 70));

        r.EffectivePage.WidthMm.Should().Be(210);
        r.EffectivePage.HeightMm.Should().Be(148);
        r.Grid.Columns.Should().Be(2);
        r.Grid.Rows.Should().Be(2);
        r.Grid.MarginLeftMm.Should().BeApproximately(5, 1e-9);
        r.Grid.MarginTopMm.Should().BeApproximately(4, 1e-9);
    }

    [Fact]
    public void A_custom_paper_is_used_the_way_round_it_was_typed()
    {
        LabelTemplate t = Store.Get("formtec-gs-1189")!;
        var layout = new PrintLayout { PaperSize = PrintLayout.CustomPaper, PaperWidthMm = 200, PaperHeightMm = 150 };

        PrintLayoutResolver.Resolve(t, layout, landscape: false, t.LabelSizeMm).EffectivePage.WidthMm.Should().Be(200);
        PrintLayoutResolver.Resolve(t, layout, landscape: true, t.LabelSizeMm).EffectivePage.WidthMm.Should().Be(150);
    }

    [Fact]
    public void A_label_that_does_not_fit_inside_the_margins_is_reported()
    {
        LabelTemplate t = Store.Get("formtec-gs-1189")!;
        var layout = new PrintLayout { PaperSize = "A6", CustomMargins = true };

        Action act = () => PrintLayoutResolver.Resolve(t, layout, landscape: false, new SizeMm(150, 100));

        act.Should().Throw<InvalidOperationException>().WithMessage("*does not fit*");
    }

    [Fact]
    public void Another_paper_keeps_the_template_label_positions()
    {
        LabelTemplate t = Store.Get("formtec-gs-1189")!;
        LabelTemplate r = PrintLayoutResolver.Resolve(t, new PrintLayout { PaperSize = "Letter" }, landscape: false, t.LabelSizeMm);

        r.EffectivePage.Name.Should().Be("Letter");
        r.Grid.Should().Be(t.Grid);
    }

    [Fact]
    public void Pages_print_the_master_label_first_when_it_has_content_and_repeat_each_page()
    {
        ProjectModel p = ProjectModel.CreateNew(Store.Get("formtec-gs-1189")!);
        var a = new LabelPage { Name = "A", Document = new LabelDocument { Name = "A" } };
        var b = new LabelPage { Name = "B", Document = new LabelDocument { Name = "B" } };
        p.Pages.Add(a);
        p.Pages.Add(b);

        PrintablePages.For(p, new PrintSettings()).Should().Equal(a.Document, b.Document);

        p.MasterLabel.Objects.Add(new LabelMaker.Core.Model.Objects.ShapeObject());
        PrintablePages.For(p, new PrintSettings { CopiesPerPage = 2 })
            .Should().Equal(p.MasterLabel, p.MasterLabel, a.Document, a.Document, b.Document, b.Document);

        PrintablePages.For(p, new PrintSettings { SelectedRecords = { 1 } }).Should().Equal(b.Document);
    }
}
