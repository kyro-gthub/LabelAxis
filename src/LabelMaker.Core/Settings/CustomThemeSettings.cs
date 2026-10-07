namespace LabelMaker.Core.Settings;

/// <summary>
/// The user's own theme (Settings › Appearance › Make your own theme): one of the built-in themes
/// to start from, with its colours and glow changed. Colours are hex ("#0F7896").
/// </summary>
public sealed class CustomThemeSettings
{
    public string Name { get; set; } = "My theme";

    /// <summary>The built-in theme it starts from (frost, midnight…): its text and glass colours are kept.</summary>
    public string BasedOn { get; set; } = "frost";

    /// <summary>Buttons, selections and the first glow.</summary>
    public string Accent { get; set; } = "#0F7896";

    /// <summary>The second glow and the far end of gradients.</summary>
    public string AccentSecondary { get; set; } = "#7C6BD0";

    /// <summary>The third, smaller glow and highlights.</summary>
    public string AccentTertiary { get; set; } = "#D9A04A";

    /// <summary>The window background; null keeps the starting theme's.</summary>
    public string? Background { get; set; }

    /// <summary>How strong the glows behind the panels are, 0 (none) to 1.5; 1 is the theme's own.</summary>
    public double Glow { get; set; } = 1.0;

    public CustomThemeSettings Clone() => (CustomThemeSettings)MemberwiseClone();
}
