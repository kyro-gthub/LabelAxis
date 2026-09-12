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
    public void Shape_with_a_second_stop_is_drawn_as_a_gradient()
    {
        var doc = new LabelDocument { SizeMm = new SizeMm(60, 30) };
        doc.Objects.Add(new ShapeObject
        {
            Bounds = new RectMm(2, 2, 20, 10),
            Shape = ShapeKind.Rectangle,
            Fill = new ColorRgba(0x2C, 0x6F, 0xE8),
            GradientTo = ColorRgba.White,
            GradientAngleDegrees = 45,
            StrokeWidthMm = 0,
        });

        var surface = new RecordingSurface();
        new LabelRenderer().Render(doc, new ResolveContext(), surface);

        surface.Ops.Should().Contain("gradientRect(45)");
    }

    [Fact]
    public void Shape_without_a_second_stop_stays_a_flat_fill()
    {
        var doc = new LabelDocument { SizeMm = new SizeMm(60, 30) };
        doc.Objects.Add(new ShapeObject
        {
            Bounds = new RectMm(2, 2, 20, 10),
            Shape = ShapeKind.Rectangle,
            Fill = new ColorRgba(0x2C, 0x6F, 0xE8),
            StrokeWidthMm = 0,
        });

        var surface = new RecordingSurface();
        new LabelRenderer().Render(doc, new ResolveContext(), surface);

        surface.Ops.Should().Contain("fillRect");
        surface.Ops.Should().NotContain(op => op.StartsWith("gradient", StringComparison.Ordinal));
    }

    [Fact]
    public void Cropped_image_clips_to_its_box_and_draws_enlarged()
    {
        var doc = new LabelDocument { SizeMm = new SizeMm(60, 30) };
        var image = new ImageObject
        {
            Bounds = new RectMm(0, 0, 20, 20),
            AssetId = "asset",
            FitMode = ImageFitMode.Stretch,
        };
        image.Adjustments.CropX = 0.25;
        image.Adjustments.CropY = 0;
        image.Adjustments.CropWidth = 0.5;
        image.Adjustments.CropHeight = 1.0;
        doc.Objects.Add(image);

        var surface = new RecordingSurface();
        surface.UseStubImage(400, 200);
        new LabelRenderer().Render(doc, new ResolveContext(), surface);

        // The crop is implemented as clip + oversized draw, so both must be present.
        surface.Ops.Should().ContainInOrder("clipRect", "image", "pop");
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
