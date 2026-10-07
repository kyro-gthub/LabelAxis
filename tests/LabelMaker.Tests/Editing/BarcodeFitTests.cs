using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Units;
using LabelMaker.Tests.Support;
using Xunit;

namespace LabelMaker.Tests.Editing;

/// <summary>A barcode drawn at its natural size and its box always match, whichever one changes.</summary>
public sealed class BarcodeFitTests
{
    private static (LabelDocument Doc, BarcodeObject Barcode) Label(BarcodeSymbology symbology = BarcodeSymbology.Code128, string data = "ABC-12345")
    {
        var doc = new LabelDocument { SizeMm = new SizeMm(80, 40) };
        var spec = new BarcodeSpec { Symbology = symbology, Data = data, ModuleWidthMm = 0.3, BarHeightMm = 12 };
        SizeMm natural = BarcodeFit.NaturalSize(spec, data)!.Value;
        var barcode = new BarcodeObject { AutoSize = true, Spec = spec, Bounds = new RectMm(5, 5, natural.Width, natural.Height) };
        doc.Objects.Add(barcode);
        return (doc, barcode);
    }

    private static IUndoableCommand Run(LabelDocument doc, IUndoableCommand edit) =>
        TextBoxFit.ExecuteWithFit(edit, doc, new RecordingSurface().TextMeasurer, resolve: null);

    [Fact]
    public void Resizing_the_box_rescales_the_bars_to_fill_it()
    {
        (LabelDocument doc, BarcodeObject barcode) = Label();
        RectMm before = barcode.Bounds;
        RectMm wanted = before with { Width = before.Width / 2, Height = before.Height - 4 };

        IUndoableCommand edit = Run(doc, new SetPropertyCommand<RectMm>("Resize", () => barcode.Bounds, v => barcode.Bounds = v, wanted));

        barcode.Spec.ModuleWidthMm.Should().BeApproximately(0.15, 0.001, "half as wide means bars half as wide");
        barcode.Spec.BarHeightMm.Should().BeApproximately(8, 0.01);
        barcode.Bounds.Width.Should().BeApproximately(wanted.Width, 0.01, "the box is exactly the barcode");
        barcode.Bounds.Height.Should().BeApproximately(wanted.Height, 0.01);
        BarcodeFit.NaturalSize(barcode.Spec, barcode.Spec.Data)!.Value.Width.Should().BeApproximately(barcode.Bounds.Width, 0.01);

        edit.Undo();
        barcode.Bounds.Should().Be(before);
        barcode.Spec.ModuleWidthMm.Should().Be(0.3);
        edit.Do();
        barcode.Spec.ModuleWidthMm.Should().BeApproximately(0.15, 0.001);
    }

    [Fact]
    public void Changing_the_barcode_makes_the_box_follow_around_its_centre()
    {
        (LabelDocument doc, BarcodeObject barcode) = Label();
        PointMm centre = barcode.Bounds.Center;
        double width = barcode.Bounds.Width;

        Run(doc, new SetPropertyCommand<string>("Data", () => barcode.Spec.Data, v => barcode.Spec.Data = v, "ABC-12345-LONGER-VALUE"));
        barcode.Bounds.Width.Should().BeGreaterThan(width + 5, "longer data, longer barcode, longer box");
        barcode.Bounds.Center.X.Should().BeApproximately(centre.X, 0.01);
        BarcodeFit.NaturalSize(barcode.Spec, barcode.Spec.Data)!.Value.Width.Should().BeApproximately(barcode.Bounds.Width, 0.01);

        Run(doc, new SetPropertyCommand<double>("Module", () => barcode.Spec.ModuleWidthMm, v => barcode.Spec.ModuleWidthMm = v, 0.5));
        BarcodeFit.NaturalSize(barcode.Spec, barcode.Spec.Data)!.Value.Width.Should().BeApproximately(barcode.Bounds.Width, 0.01);
    }

    [Fact]
    public void A_square_code_stays_square_and_a_stale_box_is_corrected()
    {
        (LabelDocument doc, BarcodeObject qr) = Label(BarcodeSymbology.QrCode, "https://example.com");
        Run(doc, new SetPropertyCommand<RectMm>("Resize", () => qr.Bounds, v => qr.Bounds = v, qr.Bounds with { Width = 30, Height = 20 }));
        qr.Bounds.Width.Should().BeApproximately(qr.Bounds.Height, 0.01);
        qr.Bounds.Width.Should().BeLessThanOrEqualTo(20.01, "as large as fits the box");

        // A box saved the wrong size (as older versions could) is put right by the next edit.
        qr.Bounds = qr.Bounds with { Width = 3, Height = 3 };
        var other = new TextObject();
        Run(doc, new AddObjectCommand(doc, other));
        qr.Bounds.Width.Should().BeGreaterThan(10);
    }

    [Fact]
    public void Barcodes_fitted_to_their_box_and_locked_ones_are_left_alone()
    {
        (LabelDocument doc, BarcodeObject barcode) = Label();
        barcode.AutoSize = false;
        RectMm wanted = barcode.Bounds with { Width = 10 };
        Run(doc, new SetPropertyCommand<RectMm>("Resize", () => barcode.Bounds, v => barcode.Bounds = v, wanted));
        barcode.Bounds.Should().Be(wanted);
        barcode.Spec.ModuleWidthMm.Should().Be(0.3);

        barcode.AutoSize = true;
        barcode.Locked = true;
        Run(doc, new SetPropertyCommand<string>("Data", () => barcode.Spec.Data, v => barcode.Spec.Data = v, "LONGER-DATA-123456"));
        barcode.Bounds.Should().Be(wanted);
    }
}
