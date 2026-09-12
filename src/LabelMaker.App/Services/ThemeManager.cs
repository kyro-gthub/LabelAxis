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
        new("Clr.Bg", "#FFFFFFFF", "#FF1C2230"),
        new("Clr.Surface", "#FFF3F5F9", "#FF12161F"),
        new("Clr.Surface2", "#FFF8FAFC", "#FF171C27"),
        new("Clr.Border", "#14263043", "#1AFFFFFF"),
        new("Clr.BorderStrong", "#26263043", "#2EFFFFFF"),
        new("Clr.Text", "#FF1D2432", "#FFEAEEF6"),
        new("Clr.TextMuted", "#FF8A93A6", "#FF8F99AC"),
        new("Clr.Text2", "#FF5B667C", "#FFBCC5D4"),
        new("Clr.Accent", "#FF2C6FE8", "#FF5C9BFF"),
        new("Clr.AccentHover", "#FF1F5AC6", "#FF7DB0FF"),
        new("Clr.AccentSoft", "#FFE8F1FE", "#FF1E3358"),
        new("Clr.Hover", "#FFF1F5FA", "#FF222936"),
        new("Clr.Danger", "#FFE0555F", "#FFF0707A"),
        new("Clr.Ok", "#FF22B268", "#FF3FCB85"),
        new("Clr.SelectionBlue", "#FF2C6FE8", "#FF5C9BFF"),
        new("Clr.HeaderA", "#FF3E86F5", "#FF5C9BFF"),
        new("Clr.HeaderB", "#FF2C6FE8", "#FF3C7FE0"),
        new("Clr.TitleBarA", "#FFFFFFFF", "#FF1B2230"),
        new("Clr.TitleBarB", "#FFE8F3EE", "#FF16202B"),
    };

    private static readonly (string Key, string Light, string Dark)[] SolidBrushes =
    {
        ("Br.Bg", "#FFFFFFFF", "#FF1C2230"),
        ("Br.Surface", "#FFF3F5F9", "#FF12161F"),
        ("Br.Surface2", "#FFF8FAFC", "#FF171C27"),
        ("Br.Border", "#14263043", "#1AFFFFFF"),
        ("Br.BorderStrong", "#26263043", "#2EFFFFFF"),
        ("Br.Text", "#FF1D2432", "#FFEAEEF6"),
        ("Br.TextMuted", "#FF8A93A6", "#FF8F99AC"),
        ("Br.Text2", "#FF5B667C", "#FFBCC5D4"),
        ("Br.Accent", "#FF2C6FE8", "#FF5C9BFF"),
        ("Br.AccentHover", "#FF1F5AC6", "#FF7DB0FF"),
        ("Br.AccentSoft", "#FFE8F1FE", "#FF1E3358"),
        ("Br.Hover", "#FFF1F5FA", "#FF222936"),
        ("Br.Danger", "#FFE0555F", "#FFF0707A"),
        ("Br.Ok", "#FF22B268", "#FF3FCB85"),
        ("Br.SelectionBlue", "#FF2C6FE8", "#FF5C9BFF"),
        ("PanelBackground", "#FFFFFFFF", "#FF1C2230"),
        ("PanelBorder", "#14263043", "#1AFFFFFF"),
        ("CanvasBackground", "#FFF3F5F9", "#FF12161F"),
    };

    private static readonly (string Key, string[] Light, string[] Dark)[] Gradients =
    {
        ("Br.HeaderGradient", new[] { "#FF3E86F5", "#FF2C6FE8" }, new[] { "#FF5C9BFF", "#FF3C7FE0" }),
        ("Br.AccentGradient", new[] { "#FF3E86F5", "#FF2C6FE8" }, new[] { "#FF5C9BFF", "#FF3C7FE0" }),
        ("Br.TitleBarGradient",
            new[] { "#FFFFFFFF", "#FFF2F7FE", "#FFE9F2FB", "#FFE8F3EE" },
            new[] { "#FF1B2230", "#FF19212D", "#FF17202A", "#FF16202B" }),
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
