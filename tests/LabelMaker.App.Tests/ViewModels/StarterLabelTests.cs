using FluentAssertions;
using LabelMaker.App.Rendering;
using LabelMaker.App.ViewModels;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.App.Tests.ViewModels;

/// <summary>The title and barcode a new project starts with, on labels small and large.</summary>
public sealed class StarterLabelTests
{
    [Theory]
    [InlineData(25, 10)]     // Formtec GS-1189: the title wrapped below its box, the bars a sliver
    [InlineData(38.1, 21.2)]
    [InlineData(63.5, 38.1)]
    [InlineData(100, 70)]
    public void The_title_stays_on_one_line_above_a_barcode_that_scans(double width, double height) => Sta.Run(() =>
    {
        LabelTemplate template = LabelTemplate.CreateRoll("t", "t", width, height);
        ProjectModel project = ProjectModel.CreateNew(template, "Untitled");

        MainViewModel.SeedStarterLabel(project);

        var label = new RectMm(0, 0, width, height);
        TextObject title = project.MasterLabel.Objects.OfType<TextObject>().Single();
        BarcodeObject barcode = project.MasterLabel.Objects.OfType<BarcodeObject>().Single();
        label.Contains(title.Bounds).Should().BeTrue();
        label.Contains(barcode.Bounds).Should().BeTrue();
        title.Bounds.Bottom.Should().BeLessThanOrEqualTo(barcode.Bounds.Y + 1e-9, "the barcode starts below the title");

        RectMm content = new(title.Bounds.X + title.PaddingMm.Left, title.Bounds.Y + title.PaddingMm.Top,
            title.Bounds.Width - title.PaddingMm.Left - title.PaddingMm.Right, title.Bounds.Height - title.PaddingMm.Top - title.PaddingMm.Bottom);
        TextBlock block = TextLayouter.Layout(title.Content, title, content, new WpfTextMeasurer());
        block.Lines.Should().HaveCount(1);
        block.InkHeightMm.Should().BeLessThanOrEqualTo(content.Height + 0.1, "the title fits inside its box");

        BarcodeSymbol symbol = BarcodeEngine.Encode(barcode.Spec, barcode.Spec.Data);
        PlacedBarcode placed = BarcodeLayout.PlaceFit(symbol, barcode.Spec, barcode.Bounds.Size);
        placed.SymbolRect.Height.Should().BeGreaterThan(barcode.Bounds.Height * 0.55, "most of the barcode's height is bars");
        (placed.SymbolRect.Bottom + (placed.HriRect?.Height ?? 0)).Should().BeLessThanOrEqualTo(barcode.Bounds.Height + 0.05);
    });
}
