using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LabelMaker.App.Services;

/// <summary>Kept for older callers: light means the Frost theme, dark the Midnight theme.</summary>
public enum AppTheme
{
    Light = 0,
    Dark = 1,
}

/// <summary>One of the themes in Themes/Theme.*.xaml, as the theme picker shows it.</summary>
public sealed record ThemeInfo(string Id, string Name, bool IsDark, Color Background, Color Accent, Color AccentSecondary, Color AccentTertiary)
{
    public SolidColorBrush BackgroundBrush => Frozen(Background);
    public SolidColorBrush AccentBrush => Frozen(Accent);
    public SolidColorBrush AccentSecondaryBrush => Frozen(AccentSecondary);
    public SolidColorBrush AccentTertiaryBrush => Frozen(AccentTertiary);

    /// <summary>"Dark" or "Light", for the picker.</summary>
    public string Mode => IsDark ? "Dark" : "Light";

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// <summary>
/// Switches the app's whole colour palette live. The look comes from the LabelAxis glass themes
/// (Themes/Theme.*.xaml, eight of them): their LA.* colours and brushes are copied into the
/// application's resources, and the app's own palette (Clr.* / Br.*, which every window and
/// style uses through DynamicResource) is worked out from them. Assigning a resource KEY to a
/// new object raises the change every DynamicResource consumer listens for, so every open
/// window repaints at once with no restart.
/// </summary>
public static class ThemeManager
{
    /// <summary>The themes in the order the picker lists them.</summary>
    public static IReadOnlyList<string> ThemeIds { get; } = new[] { "frost", "aurora", "midnight", "graphite", "ember", "ocean", "sakura", "sage" };

    public const string DefaultTheme = "frost";

    /// <summary>The id of the user's own theme.</summary>
    public const string CustomId = "custom";

    private static IReadOnlyList<ThemeInfo>? _themes;
    private static LabelMaker.Core.Settings.CustomThemeSettings? _custom;

    /// <summary>Every theme with its name and colours (for the picker's swatches): the built-in ones, then the user's own.</summary>
    public static IReadOnlyList<ThemeInfo> Themes => _themes ??= AllIds.Select(id => Describe(id, Load(id))).ToList();

    private static IEnumerable<string> AllIds => _custom is null ? ThemeIds : ThemeIds.Append(CustomId);

    /// <summary>The user's own theme, or null when there is none.</summary>
    public static LabelMaker.Core.Settings.CustomThemeSettings? Custom => _custom;

    /// <summary>Sets (or with null removes) the user's own theme. Apply <see cref="CustomId"/> to show it.</summary>
    public static void SetCustom(LabelMaker.Core.Settings.CustomThemeSettings? custom)
    {
        _custom = custom?.Clone();
        Loaded.Remove(CustomId);
        _themes = null;
    }

    public static string CurrentId { get; private set; } = DefaultTheme;

    public static bool IsDark { get; private set; }

    public static AppTheme Current => IsDark ? AppTheme.Dark : AppTheme.Light;

    /// <summary>Raised after a theme is applied (the canvas draws its rulers and workspace from it).</summary>
    public static event EventHandler? Changed;

    /// <summary>Colours the canvas draws itself (it does not use brushes from resources).</summary>
    public static CanvasPalette Canvas => _canvas ??= CanvasPalette.From(Load(DefaultTheme), dark: false);

    private static CanvasPalette? _canvas;

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

    /// <summary>The theme for a setting: the one named, or Midnight / Frost from the old dark-mode switch.</summary>
    public static string Resolve(string? id, bool legacyDark) =>
        id is not null && AllIds.Contains(id, StringComparer.OrdinalIgnoreCase) ? id.ToLowerInvariant()
        : string.Equals(id, CustomId, StringComparison.OrdinalIgnoreCase) && _custom is null ? (legacyDark ? "midnight" : DefaultTheme)
        : legacyDark ? "midnight" : DefaultTheme;

    public static void Apply(AppTheme theme) => Apply(theme == AppTheme.Dark ? "midnight" : DefaultTheme);

    public static void Apply(string id)
    {
        id = Resolve(id, legacyDark: false);
        ResourceDictionary theme = Load(id);
        ResourceDictionary res = Application.Current.Resources;
        bool dark = theme["LA.IsDark"] is true;
        CurrentId = id;
        IsDark = dark;

        // The theme's own tokens, for the glass styles (LabelAxis.Styles.xaml) and the shell.
        foreach (object key in theme.Keys)
        {
            res[key] = theme[key];
        }

        Color bg = Col(theme, "Background");
        Color glass = Col(theme, "GlassFill");
        Color glassHover = Col(theme, "GlassFillHover");
        Color text = Col(theme, "TextPrimary");
        Color accent = Col(theme, "Accent");
        Color accent2 = Col(theme, "AccentSecondary");

        // Dialogs, menus and drop-downs stay solid, so text on them reads on any background:
        // the glass colours are laid over the window colour once, here.
        Color surface = bg;
        Color card = Over(glassHover, bg);
        Color inset = Over(glass, bg);
        Color hover = Over(WithAlpha(text, 0x12), card);
        Color accentSoft = Over(Col(theme, "AccentSubtle"), card);
        Color accentHover = Mix(accent, dark ? Colors.White : Colors.Black, 0.14);

        var palette = new (string Key, Color Value)[]
        {
            ("Bg", card), ("Surface", surface), ("Surface2", inset),
            ("Border", Col(theme, "GlassBorder")), ("BorderStrong", Col(theme, "GlassBorderStrong")),
            ("Text", text), ("Text2", Col(theme, "TextSecondary")), ("TextMuted", Col(theme, "TextTertiary")),
            ("Accent", accent), ("AccentHover", accentHover), ("AccentSoft", accentSoft), ("Hover", hover),
            ("Danger", Col(theme, "Danger")), ("Ok", Col(theme, "Success")), ("SelectionBlue", accent),
            ("HeaderA", accent), ("HeaderB", Mix(accent, accent2, 0.35)),
            ("TitleBarA", WithAlpha(bg, 0)), ("TitleBarB", WithAlpha(bg, 0)),
        };

        foreach ((string key, Color value) in palette)
        {
            res["Clr." + key] = value;
            if (!key.StartsWith("Title", StringComparison.Ordinal) && !key.StartsWith("Header", StringComparison.Ordinal))
            {
                res["Br." + key] = Brush(value);
            }
        }

        res["PanelBackground"] = Brush(card);
        res["PanelBorder"] = Brush(Col(theme, "GlassBorder"));
        res["CanvasBackground"] = Brush(Over(Col(theme, "CanvasWell"), bg));
        res["Br.HeaderGradient"] = Gradient(accent, Mix(accent, accent2, 0.35));
        res["Br.AccentGradient"] = Gradient(accent, accent2);
        res["Br.TitleBarGradient"] = Brush(Colors.Transparent);

        // The main window's panels: mostly solid, with only a hint of the glow behind. The
        // theme files' own glass (4% in the dark themes) let the glow wash out the toolbar's
        // icons and the panels' text.
        Color shellBase = dark ? Over(WithAlpha(Colors.White, 0x0C), bg) : card;
        res["LA.Brush.ShellFill"] = Brush(WithAlpha(shellBase, dark ? (byte)0xE0 : (byte)0xD6));

        // Softer glows than the design files ask for, for the same reason.
        if (theme["LA.OrbOpacity"] is double orb)
        {
            res["LA.OrbOpacity"] = orb * 0.6;
        }

        if (theme["LA.OrbOpacitySoft"] is double soft)
        {
            res["LA.OrbOpacitySoft"] = soft * 0.6;
        }

        // The icons in the theme's ink (see Icons.xaml for why they are made afresh).
        var icons = new ResourceDictionary { Source = new Uri("pack://application:,,,/LabelAxis;component/Icons.xaml") };
        Color ink = Col(theme, "TextPrimary");
        if (icons["IconPen"] is Pen { IsFrozen: false } pen)
        {
            pen.Brush = Brush(ink);
        }

        if (icons["IconFill"] is SolidColorBrush { IsFrozen: false } fill)
        {
            fill.Color = ink;
        }

        foreach (object key in icons.Keys)
        {
            res[key] = icons[key];
        }

        _canvas = CanvasPalette.From(theme, dark);

        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBar(window);
            window.InvalidateVisual();
        }

        Changed?.Invoke(null, EventArgs.Empty);
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

        int dark = IsDark ? 1 : 0;
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

    private static readonly Dictionary<string, ResourceDictionary> Loaded = new(StringComparer.OrdinalIgnoreCase);

    private static ResourceDictionary Load(string id)
    {
        if (string.Equals(id, CustomId, StringComparison.OrdinalIgnoreCase) && _custom is { } custom)
        {
            if (!Loaded.TryGetValue(CustomId, out ResourceDictionary? own))
            {
                own = BuildCustom(custom);
                Loaded[CustomId] = own;
            }

            return own;
        }

        if (!Loaded.TryGetValue(id, out ResourceDictionary? dict))
        {
            string name = char.ToUpperInvariant(id[0]) + id[1..];
            dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/LabelAxis;component/Themes/Theme.{name}.xaml") };
            Loaded[id] = dict;
        }

        return dict;
    }

    /// <summary>
    /// The user's own theme: the theme it starts from with its colours replaced. Text and glass
    /// colours come from a light or a dark theme to suit the background, so text always reads;
    /// the text on accent-coloured buttons is black or white, whichever stands out more.
    /// </summary>
    private static ResourceDictionary BuildCustom(LabelMaker.Core.Settings.CustomThemeSettings custom)
    {
        string baseId = ThemeIds.Contains(custom.BasedOn, StringComparer.OrdinalIgnoreCase) ? custom.BasedOn.ToLowerInvariant() : DefaultTheme;
        ResourceDictionary start = Load(baseId);
        Color? background = Parse(custom.Background);
        if (background is { } bgColour && (Luminance(bgColour) < 0.4) != (start["LA.IsDark"] is true))
        {
            start = Load(Luminance(bgColour) < 0.4 ? "midnight" : DefaultTheme); // text that reads on this background
        }

        var d = new ResourceDictionary();
        foreach (object key in start.Keys)
        {
            d[key] = start[key];
        }

        Color bg = background ?? Col(start, "Background");
        Color accent = Parse(custom.Accent) ?? Col(start, "Accent");
        Color accent2 = Parse(custom.AccentSecondary) ?? Col(start, "AccentSecondary");
        Color accent3 = Parse(custom.AccentTertiary) ?? Col(start, "AccentTertiary");
        Color onAccent = Contrast(Colors.White, accent) >= Contrast(Color.FromRgb(0x14, 0x18, 0x20), accent) ? Colors.White : Color.FromRgb(0x14, 0x18, 0x20);

        d["LA.ThemeId"] = CustomId;
        d["LA.ThemeName"] = string.IsNullOrWhiteSpace(custom.Name) ? "My theme" : custom.Name.Trim();
        double glow = Math.Clamp(custom.Glow, 0, 1.5);
        d["LA.OrbOpacity"] = (start["LA.OrbOpacity"] is double o ? o : 0.38) * glow;
        d["LA.OrbOpacitySoft"] = (start["LA.OrbOpacitySoft"] is double s ? s : 0.18) * glow;

        void Set(string token, Color colour)
        {
            d["LA.Color." + token] = colour;
            d["LA.Brush." + token] = new SolidColorBrush(colour);
        }

        Set("Background", bg);
        Set("Accent", accent);
        Set("AccentSecondary", accent2);
        Set("AccentTertiary", accent3);
        Set("OnAccent", onAccent);
        Set("AccentSubtle", WithAlpha(accent, 0x24));
        Set("AccentBorder", WithAlpha(accent, 0x59));
        d["LA.Brush.AccentGradient"] = Gradient(accent, accent2);
        d["LA.Brush.Orb1"] = Orb(accent);
        d["LA.Brush.Orb2"] = Orb(accent2);
        d["LA.Brush.Orb3"] = Orb(accent3);
        return d;
    }

    private static RadialGradientBrush Orb(Color c) =>
        new(new GradientStopCollection { new(c, 0), new(WithAlpha(c, 0), 0.66) });

    private static Color? Parse(string? hex) =>
        LabelMaker.Core.Model.ColorRgba.TryParse(hex, out LabelMaker.Core.Model.ColorRgba c) ? Color.FromRgb(c.R, c.G, c.B) : null;

    /// <summary>Relative luminance (WCAG), 0 for black to 1 for white.</summary>
    public static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    /// <summary>The WCAG contrast ratio of two colours (1 to 21).</summary>
    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static ThemeInfo Describe(string id, ResourceDictionary d) => new(
        id, d["LA.ThemeName"] as string ?? id, d["LA.IsDark"] is true,
        Col(d, "Background"), Col(d, "Accent"), Col(d, "AccentSecondary"), Col(d, "AccentTertiary"));

    internal static Color Col(ResourceDictionary d, string token) => d["LA.Color." + token] is Color c ? c : Colors.Magenta;

    /// <summary>Colour <paramref name="top"/> laid over the solid colour <paramref name="bottom"/>.</summary>
    internal static Color Over(Color top, Color bottom)
    {
        double a = top.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round((top.R * a) + (bottom.R * (1 - a))),
            (byte)Math.Round((top.G * a) + (bottom.G * (1 - a))),
            (byte)Math.Round((top.B * a) + (bottom.B * (1 - a))));
    }

    internal static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + ((b.A - a.A) * t)),
        (byte)Math.Round(a.R + ((b.R - a.R) * t)),
        (byte)Math.Round(a.G + ((b.G - a.G) * t)),
        (byte)Math.Round(a.B + ((b.B - a.B) * t)));

    internal static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static SolidColorBrush Brush(Color c) => new(c);

    private static LinearGradientBrush Gradient(Color from, Color to) =>
        new(new GradientStopCollection { new(from, 0), new(to, 1) }, new Point(0, 0), new Point(1, 1));

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
}

/// <summary>The colours the designer canvas paints with, worked out from the theme.</summary>
public sealed record CanvasPalette(
    Color Workspace, Color RulerFill, Color RulerLine, Color RulerTick, Color RulerText,
    Color PaperShadow, Color PaperBorder, Color Accent, Color GridDot)
{
    public static CanvasPalette From(ResourceDictionary theme, bool dark)
    {
        Color bg = ThemeManager.Col(theme, "Background");
        Color text = ThemeManager.Col(theme, "TextPrimary");
        Color glass = ThemeManager.Over(ThemeManager.Col(theme, "GlassFill"), bg);
        return new CanvasPalette(
            Workspace: ThemeManager.Over(ThemeManager.Col(theme, "CanvasWell"), bg),
            RulerFill: glass,
            RulerLine: ThemeManager.Over(ThemeManager.Col(theme, "GlassBorder"), glass),
            RulerTick: ThemeManager.Over(ThemeManager.WithAlpha(text, 0x70), glass),
            RulerText: ThemeManager.Col(theme, "TextTertiary"),
            PaperShadow: dark ? Color.FromArgb(90, 0, 0, 0) : ThemeManager.WithAlpha(ThemeManager.Col(theme, "Shadow"), 34),
            PaperBorder: ThemeManager.Over(ThemeManager.Col(theme, "GlassBorderStrong"), bg),
            Accent: ThemeManager.Col(theme, "Accent"),
            GridDot: ThemeManager.Col(theme, "GridDot"));
    }
}
