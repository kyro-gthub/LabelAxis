using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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

    /// <summary>
    /// Set on every window by the AppWindow style. The dark frame has to be asked for when
    /// the window handle is created, before the window is first drawn: Windows 10 keeps
    /// painting the caption light when it is set later, which left white title bars with
    /// white (invisible) minimise / maximise / close buttons until the window was clicked.
    /// </summary>
    public static readonly DependencyProperty FollowThemeProperty = DependencyProperty.RegisterAttached(
        "FollowTheme", typeof(bool), typeof(ThemeManager), new PropertyMetadata(false, (d, e) =>
        {
            if (d is Window window && e.NewValue is true)
            {
                window.SourceInitialized -= OnSourceInitialized;
                window.SourceInitialized += OnSourceInitialized;
            }
        }));

    public static bool GetFollowTheme(DependencyObject d) => (bool)d.GetValue(FollowThemeProperty);

    public static void SetFollowTheme(DependencyObject d, bool value) => d.SetValue(FollowThemeProperty, value);

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            ApplyTitleBar(window);
        }
    }

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

        // The toolbar and list icons are drawings built once from these two resources; they
        // hold on to the objects rather than the resource keys, so recolour them in place.
        Color accent = C(dark ? "#FF5C9BFF" : "#FF2C6FE8");
        if (Application.Current.TryFindResource("IconPen") is Pen { IsFrozen: false } pen)
        {
            pen.Brush = new SolidColorBrush(accent);
        }

        if (Application.Current.TryFindResource("IconFill") is SolidColorBrush { IsFrozen: false } fill)
        {
            fill.Color = accent;
        }

        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBar(window);
            window.InvalidateVisual();
        }
    }

    /// <summary>
    /// The shell draws its own title bar, but the dialogs use the system one -- and
    /// Windows keeps painting that white in dark mode unless the window opts in to
    /// the immersive dark frame. Called for every window as it loads (see App.OnStartup)
    /// and again whenever the theme changes.
    /// </summary>
    public static void ApplyTitleBar(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int dark = Current == AppTheme.Dark ? 1 : 0;
        // 20 is the documented attribute; Windows 10 builds before 1903 used 19.
        if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
        }

        // The attribute only takes effect on the next frame calculation. Windows that
        // are already on screen keep their old caption until they are asked to redraw
        // the non-client area, which leaves half-light/half-dark title bars.
        _ = SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);

        // Windows 10 still keeps the old caption on a window that is already visible until its
        // activation changes; flipping the non-client activation state repaints it now.
        if (window.IsVisible)
        {
            bool active = window.IsActive;
            _ = SendMessage(hwnd, WmNcActivate, (IntPtr)(active ? 0 : 1), IntPtr.Zero);
            _ = SendMessage(hwnd, WmNcActivate, (IntPtr)(active ? 1 : 0), IntPtr.Zero);
        }
    }

    private const int WmNcActivate = 0x0086;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
}
