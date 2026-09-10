using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;
using LabelMaker.Tests.Support;
using Xunit;

namespace LabelMaker.Tests.Rendering;

public sealed class LabelRendererTests
{
    [Fact]
    public void Resolves_tokens_against_the_current_record()
    {
        var doc = new LabelDocument { SizeMm = new SizeMm(60, 30) };
        doc.Objects.Add(new TextObject
        {
            Bounds = new RectMm(2, 2, 56, 8),
            Content = RichTextContent.FromPlainText("{Product} — {Price}"),
        });

        var context = new ResolveContext
        {
            Record = new Dictionary<string, string> { ["Product"] = "Widget", ["Price"] = "15.00" },
        };

        var surface = new RecordingSurface();
        new LabelRenderer().Render(doc, context, surface);

        surface.DrawnText.Should().Contain("Widget — 15.00");
    }

    [Fact]
    public void Barcode_object_emits_a_batch_of_bar_rectangles()
    {
        var doc = new LabelDocument { SizeMm = new SizeMm(60, 30) };
        doc.Objects.Add(new BarcodeObject
        {
            Bounds = new RectMm(2, 2, 50, 20),
            AutoSize = true,
            Spec = new BarcodeSpec { Symbology = BarcodeSymbology.Code128, Data = "ABC12345", ModuleWidthMm = 0.3, BarHeightMm = 12 },
        });

        var surface = new RecordingSurface();
        new LabelRenderer().Render(doc, new ResolveContext(), surface);

        surface.BarcodeRectBatches.Should().Be(1);
        surface.RectanglesFilled.Should().BeGreaterThan(20);
        surface.DrawnText.Should().Contain("ABC12345"); // HRI
    }

    [Fact]
    public void Invisible_objects_are_skipped()
    {
        var doc = new LabelDocument { SizeMm = new SizeMm(60, 30) };
        doc.Objects.Add(new TextObject { Visible = false, Content = RichTextContent.FromPlainText("hidden") });

        var surface = new RecordingSurface();
        new LabelRenderer().Render(doc, new ResolveContext(), surface);

        surface.DrawnText.Should().NotContain("hidden");
    }

    [Fact]
    public void Shrink_to_fit_reduces_text_that_would_overflow()
    {
        var style = new TextObject
        {
            FontSizePt = 40,
            AutoFit = TextAutoFit.ShrinkToFit,
            MinAutoFitPt = 4,
            WordWrap = true,
        };
        var content = RichTextContent.FromPlainText("This is a fairly long piece of text that must shrink");

        var measurer = new RecordingSurface().TextMeasurer;
        TextBlock block = TextLayouter.Layout(content, style, new RectMm(0, 0, 30, 12), measurer);

        block.TotalHeightMm.Should().BeLessThanOrEqualTo(12.5);
    }

    [Fact]
    public void Rotation_pushes_a_transform_for_the_object()
    {
        var doc = new LabelDocument { SizeMm = new SizeMm(60, 30) };
        doc.Objects.Add(new TextObject
        {
            Bounds = new RectMm(10, 10, 20, 6),
            RotationDegrees = 90,
            Content = RichTextContent.FromPlainText("rot"),
        });

        var surface = new RecordingSurface();
        new LabelRenderer().Render(doc, new ResolveContext(), surface);

        surface.Ops.Should().Contain(o => o.StartsWith("push(") && o.Contains("90"));
    }
}
