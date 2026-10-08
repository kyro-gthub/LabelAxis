using System.Windows;
using System.Windows.Media;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Controls;

/// <summary>The brushes the colour window and the Properties panel draw colours and gradients with.</summary>
public static class ColourBrushes
{
    public static Color ToWpf(ColorRgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>A grey and white chequer that tiles every <paramref name="cell"/> pixels, shown behind see-through colours.</summary>
    public static Brush Checker(double cell)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, cell * 2, cell * 2))));
        var dark = new SolidColorBrush(Color.FromRgb(0xCC, 0xD2, 0xDC));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, cell, cell))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(cell, cell, cell, cell))));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, cell * 2, cell * 2),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>The gradient as it is drawn on the label: along the angle, through the box's centre.</summary>
    public static Brush Gradient(ColorRgba from, ColorRgba to, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180.0;
        double dx = Math.Cos(radians) / 2.0, dy = Math.Sin(radians) / 2.0;
        var brush = new LinearGradientBrush(ToWpf(from), ToWpf(to), new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// The little colour square beside a colour's name. A colour that is partly or fully see-through
    /// is shown over a chequered square, so "no colour" never looks like a chosen colour.
    /// </summary>
    public static Brush Swatch(ColorRgba c) =>
        c.A == 255 ? new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) : OverChecks(new SolidColorBrush(ToWpf(c)));

    /// <summary>A gradient's swatch, over the chequer when either end is see-through.</summary>
    public static Brush Swatch(ColorRgba from, ColorRgba to, double angleDegrees)
    {
        Brush gradient = Gradient(from, to, angleDegrees);
        return from.A == 255 && to.A == 255 ? gradient : OverChecks(gradient);
    }

    private static Brush OverChecks(Brush colour)
    {
        var group = new DrawingGroup();
        var whole = new RectangleGeometry(new Rect(0, 0, 16, 16));
        group.Children.Add(new GeometryDrawing(Brushes.White, null, whole));
        var grey = new SolidColorBrush(Color.FromRgb(0xCC, 0xD2, 0xDC));
        for (int y = 0; y < 4; y++)
        {
            for (int x = y % 2; x < 4; x += 2)
            {
                group.Children.Add(new GeometryDrawing(grey, null, new RectangleGeometry(new Rect(x * 4, y * 4, 4, 4))));
            }
        }

        group.Children.Add(new GeometryDrawing(colour, null, whole));
        var brush = new DrawingBrush(group) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }
}
