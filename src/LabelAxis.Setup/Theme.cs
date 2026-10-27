using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace LabelAxis.Setup;

/// <summary>Setup's colours, light or dark as Windows is set (the brushes the window uses by name).</summary>
internal static class Theme
{
    public static bool WindowsUsesDark()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    public static void Apply(ResourceDictionary resources, bool dark)
    {
        Set(resources, "Bg", dark ? "#111622" : "#FFFFFF");
        Set(resources, "Surface", dark ? "#182033" : "#F5F7FB");
        Set(resources, "Hover", dark ? "#1F2940" : "#EEF2F8");
        Set(resources, "Text", dark ? "#F1F5F9" : "#0F172A");
        Set(resources, "Muted", dark ? "#94A3B8" : "#64748B");
        Set(resources, "Line", dark ? "#2A3550" : "#E2E8F0");
        Set(resources, "Accent", dark ? "#3B82F6" : "#2563EB");
        Set(resources, "AccentSoft", dark ? "#1B2A4A" : "#E8F0FE");
        Set(resources, "Field", dark ? "#0E1320" : "#FFFFFF");
        Set(resources, "Good", dark ? "#22C55E" : "#16A34A");
        Set(resources, "Danger", dark ? "#F87171" : "#DC2626");
        Set(resources, "DangerSoft", dark ? "#3A1A1F" : "#FEF2F2");
        Set(resources, "Shadow", dark ? "#000000" : "#1E293B");
        resources["PanelTop"] = Color(dark ? "#1C3266" : "#3F8AF4");
        resources["PanelBottom"] = Color(dark ? "#0A1022" : "#1543B5");
    }

    private static void Set(ResourceDictionary resources, string key, string hex)
    {
        var brush = new SolidColorBrush(Color(hex));
        brush.Freeze();
        resources[key] = brush;
        resources[key + "Color"] = brush.Color;
    }

    private static Color Color(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
