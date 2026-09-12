using System.Windows;
using System.Windows.Media;

namespace LabelMaker.App.Services;

public enum AppTheme
{
    Light = 0,
    Dark = 1,
}

/// <summary>
/// Switches the app's whole color palette live. Theme.xaml's styles reference
/// every palette color via <c>{DynamicResource Br.X}</c> / <c>{DynamicResource
/// Clr.X}</c> rather than StaticResource specifically so this works: assigning
/// a resource dictionary KEY to a brand-new Brush/Color object (not mutating
/// the existing one -- WPF freezes brushes loaded from compiled XAML, so an
/// in-place `.Color =` throws) raises the change notification every
/// DynamicResource consumer is already listening for, repainting the whole
/// app immediately with no restart needed.
/// </summary>
public static class ThemeManager
{
    public static AppTheme Current { get; private set; } = AppTheme.Light;

    private sealed record Swatch(string Key, string Light, string Dark);

    private static readonly Swatch[] Swatches =
    {
        new("Clr.Bg", "#FFFFFFFF", "#FF20212E"),
        new("Clr.Surface", "#FFEEF0F7", "#FF15161F"),
        new("Clr.Surface2", "#FFF4F5FB", "#FF1B1C28"),
        new("Clr.Border", "#14202659", "#1AFFFFFF"),
        new("Clr.BorderStrong", "#26202659", "#2EFFFFFF"),
        new("Clr.Text", "#FF2A2F45", "#FFEDEEF5"),
        new("Clr.TextMuted", "#FF8A8FB5", "#FF9498B8"),
        new("Clr.Text2", "#FF5A5F70", "#FFC3C5D9"),
        new("Clr.Accent", "#FF5A4FE3", "#FF8B7FFF"),
        new("Clr.AccentHover", "#FF4B3BC7", "#FF9D93FF"),
        new("Clr.AccentSoft", "#FFEFECFE", "#FF2C2760"),
        new("Clr.Hover", "#FFF4F5FB", "#FF262838"),
        new("Clr.Danger", "#FFE0555F", "#FFF0707A"),
        new("Clr.Ok", "#FF2FAE6A", "#FF4CC98A"),
        new("Clr.SelectionBlue", "#FF2E7BFF", "#FF5C97FF"),
        new("Clr.HeaderA", "#FF7C5CFF", "#FF8F72FF"),
        new("Clr.HeaderB", "#FF5A4FE3", "#FF6D60E8"),
        new("Clr.TitleBarA", "#FFEEF0FC", "#FF23222E"),
        new("Clr.TitleBarB", "#FFDFE0FB", "#FF1B1A24"),
    };

    private static readonly (string Key, string Light, string Dark)[] SolidBrushes =
    {
        ("Br.Bg", "#FFFFFFFF", "#FF20212E"),
        ("Br.Surface", "#FFEEF0F7", "#FF15161F"),
        ("Br.Surface2", "#FFF4F5FB", "#FF1B1C28"),
        ("Br.Border", "#14202659", "#1AFFFFFF"),
        ("Br.BorderStrong", "#26202659", "#2EFFFFFF"),
        ("Br.Text", "#FF2A2F45", "#FFEDEEF5"),
        ("Br.TextMuted", "#FF8A8FB5", "#FF9498B8"),
        ("Br.Text2", "#FF5A5F70", "#FFC3C5D9"),
        ("Br.Accent", "#FF5A4FE3", "#FF8B7FFF"),
        ("Br.AccentHover", "#FF4B3BC7", "#FF9D93FF"),
        ("Br.AccentSoft", "#FFEFECFE", "#FF2C2760"),
        ("Br.Hover", "#FFF4F5FB", "#FF262838"),
        ("Br.Danger", "#FFE0555F", "#FFF0707A"),
        ("Br.Ok", "#FF2FAE6A", "#FF4CC98A"),
        ("Br.SelectionBlue", "#FF2E7BFF", "#FF5C97FF"),
        ("PanelBackground", "#FFFFFFFF", "#FF20212E"),
        ("PanelBorder", "#14202659", "#1AFFFFFF"),
        ("CanvasBackground", "#FFEEF0F7", "#FF15161F"),
    };

    private static readonly (string Key, string[] Light, string[] Dark)[] Gradients =
    {
        ("Br.HeaderGradient", new[] { "#FF7C5CFF", "#FF5A4FE3" }, new[] { "#FF8F72FF", "#FF6D60E8" }),
        ("Br.AccentGradient", new[] { "#FF7C5CFF", "#FF5A4FE3" }, new[] { "#FF9B8CFF", "#FF7A6DF0" }),
        ("Br.TitleBarGradient",
            new[] { "#FFEEF0FC", "#FFE7EAFB", "#FFE3E7FD", "#FFDFE0FB" },
            new[] { "#FF23222E", "#FF201F2A", "#FF1D1C27", "#FF1B1A24" }),
    };

    public static void Apply(AppTheme theme)
    {
        Current = theme;
        bool dark = theme == AppTheme.Dark;
        ResourceDictionary res = Application.Current.Resources;

        foreach (Swatch s in Swatches)
        {
            res[s.Key] = C(dark ? s.Dark : s.Light);
        }

        foreach ((string key, string light, string dark2) in SolidBrushes)
        {
            res[key] = new SolidColorBrush(C(dark ? dark2 : light));
        }

        foreach ((string key, string[] light, string[] dark2) in Gradients)
        {
            LinearGradientBrush template = res[key] as LinearGradientBrush
                ?? new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            string[] stops = dark ? dark2 : light;
            var brush = new LinearGradientBrush { StartPoint = template.StartPoint, EndPoint = template.EndPoint };
            for (int i = 0; i < stops.Length; i++)
            {
                double offset = template.GradientStops.Count > i ? template.GradientStops[i].Offset : i / (double)Math.Max(1, stops.Length - 1);
                brush.GradientStops.Add(new GradientStop(C(stops[i]), offset));
            }

            res[key] = brush;
        }
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
}
