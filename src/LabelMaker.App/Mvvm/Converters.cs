using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace LabelMaker.App.Mvvm;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>
/// "#RRGGBB(AA)" to a brush, for the colour-palette swatch buttons. A fully
/// transparent entry is drawn as a checkerboard so it is not mistaken for white.
/// </summary>
public sealed class HexBrushConverter : IValueConverter
{
    private static readonly Brush Checker = BuildChecker();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && Core.Model.ColorRgba.TryParse(s, out Core.Model.ColorRgba c))
        {
            return c.A == 0 ? Checker : new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static Brush BuildChecker()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        var dark = new SolidColorBrush(Color.FromRgb(0xD6, 0xDC, 0xE6));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 4, 4))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(4, 4, 4, 4))));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}

/// <summary>
/// Drives a segmented tab strip from one int property: pass the tab's own index
/// as ConverterParameter to a ToggleButton's IsChecked (two-way) or a section's
/// Visibility. Used for Properties (General/Style/Position/Data) and similar
/// in-panel tab strips that aren't a full TabControl.
/// </summary>
public sealed class IntEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && parameter is not null && i == System.Convert.ToInt32(parameter, culture);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? System.Convert.ToInt32(parameter, culture) : Binding.DoNothing;
}

public sealed class IntEqualsVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && parameter is not null && i == System.Convert.ToInt32(parameter, culture)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when the bound value is a non-null, non-empty/non-zero collection or number, for compact conditionals.</summary>
public sealed class NotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Resolves an icon resource key (e.g. "Icon.Sheet") to the drawing itself, for data-driven lists.</summary>
public sealed class IconLookupConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? Application.Current.TryFindResource(key) as ImageSource : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Picks the locked/unlocked padlock icon for the Objects list' lock-toggle button.</summary>
public sealed class LockIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current.TryFindResource(value is true ? "Icon.Lock" : "Icon.Unlock") as ImageSource;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
