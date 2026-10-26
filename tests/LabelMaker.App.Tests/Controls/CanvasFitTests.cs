using FluentAssertions;
using LabelMaker.App.Controls;
using Xunit;

namespace LabelMaker.App.Tests.Controls;

/// <summary>The label filling the canvas when the window opens, grows or is maximised.</summary>
public sealed class CanvasFitTests
{
    private static void Lay(DesignerCanvas canvas, double width, double height)
    {
        canvas.Measure(new System.Windows.Size(width, height));
        canvas.Arrange(new System.Windows.Rect(0, 0, width, height));
        canvas.UpdateLayout();
    }

    [Fact]
    public void The_label_is_fitted_again_when_the_canvas_grows_until_the_user_zooms() => AppHarness.Run(h =>
    {
        var canvas = new DesignerCanvas(h.ViewModel.SheetRenderer);
        h.ViewModel.AttachCanvas(canvas);

        Lay(canvas, 300, 200); // the window before it is maximised
        double small = canvas.Zoom;
        Lay(canvas, 1100, 700);
        canvas.Zoom.Should().BeGreaterThan(small * 2, "the label fills the bigger canvas instead of staying small in the middle");

        canvas.ZoomOut();
        double chosen = canvas.Zoom;
        Lay(canvas, 1200, 760);
        canvas.Zoom.Should().Be(chosen, "a zoom the user chose is kept when the window changes size");
    });
}
