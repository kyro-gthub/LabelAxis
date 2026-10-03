using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LabelMaker.Core.Formtec;

namespace LabelMaker.App.Views;

/// <summary>Draws a sheet with its label grid to scale, as the Label manager and the custom size dialog show it.</summary>
internal static class SheetPreview
{
    /// <summary>The paper with every label on it, fitted inside <paramref name="maxWidth"/> × <paramref name="maxHeight"/>.</summary>
    public static FrameworkElement Draw(FrameworkElement resources, LabelTemplate t, double maxWidth, double maxHeight)
    {
        PageSize page = t.EffectivePage;
        double scale = Math.Min(maxWidth / Math.Max(1, page.WidthMm), maxHeight / Math.Max(1, page.HeightMm));

        var canvas = new Canvas
        {
            Width = page.WidthMm * scale,
            Height = page.HeightMm * scale,
            Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            ClipToBounds = true,
        };

        var border = new Border
        {
            BorderBrush = (Brush)resources.FindResource("Br.BorderStrong"),
            BorderThickness = new Thickness(1),
            Child = canvas,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        LabelGrid g = t.Grid;
        var fill = (Brush)resources.FindResource("Br.AccentSoft");
        var stroke = (Brush)resources.FindResource("Br.Accent");

        for (int row = 0; row < g.Rows; row++)
        {
            for (int col = 0; col < g.Columns; col++)
            {
                double x = g.MarginLeftMm + (col * (g.LabelWidthMm + g.HGapMm));
                double y = g.MarginTopMm + (row * (g.LabelHeightMm + g.VGapMm));

                Shape cell = g.LabelShape == LabelShape.Ellipse
                    ? new Ellipse()
                    : new Rectangle
                    {
                        RadiusX = g.CornerRadiusMm * scale,
                        RadiusY = g.CornerRadiusMm * scale,
                    };

                cell.Width = Math.Max(1, g.LabelWidthMm * scale);
                cell.Height = Math.Max(1, g.LabelHeightMm * scale);
                cell.Fill = fill;
                cell.Stroke = stroke;
                cell.StrokeThickness = 0.6;

                Canvas.SetLeft(cell, x * scale);
                Canvas.SetTop(cell, y * scale);
                canvas.Children.Add(cell);
            }
        }

        return border;
    }
}
