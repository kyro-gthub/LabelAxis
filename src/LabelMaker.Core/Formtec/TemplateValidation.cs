using LabelMaker.Core.Units;

namespace LabelMaker.Core.Formtec;

public enum TemplateIssueSeverity
{
    Error = 0,
    Warning = 1,
}

public readonly record struct TemplateIssue(TemplateIssueSeverity Severity, string Message);

public sealed class TemplateValidationResult
{
    public List<TemplateIssue> Issues { get; } = new();

    public bool HasErrors => Issues.Any(i => i.Severity == TemplateIssueSeverity.Error);
    public bool IsValid => !HasErrors;

    internal void Error(string message) => Issues.Add(new TemplateIssue(TemplateIssueSeverity.Error, message));
    internal void Warn(string message) => Issues.Add(new TemplateIssue(TemplateIssueSeverity.Warning, message));
}

/// <summary>Structural + geometric sanity checks for a template definition.</summary>
public static class TemplateValidator
{
    public static TemplateValidationResult Validate(LabelTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var r = new TemplateValidationResult();
        LabelGrid g = template.Grid;
        PageSize page = template.EffectivePage;

        if (template.Schema != LabelTemplate.CurrentSchema)
        {
            r.Warn($"Unknown schema '{template.Schema}'; expected '{LabelTemplate.CurrentSchema}'.");
        }

        if (string.IsNullOrWhiteSpace(template.Id))
        {
            r.Error("Template id is required.");
        }

        if (string.IsNullOrWhiteSpace(template.Name))
        {
            r.Error("Template name is required.");
        }

        if (g.Columns < 1 || g.Rows < 1)
        {
            r.Error("Grid must have at least one row and one column.");
        }

        if (g.LabelWidthMm <= 0 || g.LabelHeightMm <= 0)
        {
            r.Error("Label width and height must be positive.");
        }

        if (g.HGapMm < 0 || g.VGapMm < 0)
        {
            r.Error("Gaps cannot be negative.");
        }

        if (g.MarginLeftMm < 0 || g.MarginTopMm < 0)
        {
            r.Error("Margins cannot be negative.");
        }

        if (page.WidthMm <= 0 || page.HeightMm <= 0)
        {
            r.Error("Page size must be positive.");
        }

        if (r.HasErrors)
        {
            return r;
        }

        double blockRight = g.MarginLeftMm + g.BlockSize.Width;
        double blockBottom = g.MarginTopMm + g.BlockSize.Height;

        if (blockRight > page.WidthMm + 0.5)
        {
            r.Error($"Labels extend {blockRight - page.WidthMm:0.##} mm past the right page edge.");
        }
        else if (blockRight > page.WidthMm - template.PrintableMarginMm.Right + 0.01)
        {
            r.Warn("Rightmost labels reach into the assumed printer margin.");
        }

        if (blockBottom > page.HeightMm + 0.5)
        {
            r.Error($"Labels extend {blockBottom - page.HeightMm:0.##} mm past the bottom page edge.");
        }
        else if (blockBottom > page.HeightMm - template.PrintableMarginMm.Bottom + 0.01)
        {
            r.Warn("Bottom labels reach into the assumed printer margin.");
        }

        if (g.MarginLeftMm < template.PrintableMarginMm.Left - 0.01 ||
            g.MarginTopMm < template.PrintableMarginMm.Top - 0.01)
        {
            r.Warn("First label starts inside the assumed printer margin.");
        }

        if (g.LabelShape == LabelShape.RoundRectangle && g.CornerRadiusMm <= 0)
        {
            r.Warn("Round-rectangle labels have a zero corner radius.");
        }

        if (g.CornerRadiusMm * 2 > Math.Min(g.LabelWidthMm, g.LabelHeightMm) + 0.01)
        {
            r.Warn("Corner radius is larger than half the label — it will be clamped.");
        }

        return r;
    }
}
