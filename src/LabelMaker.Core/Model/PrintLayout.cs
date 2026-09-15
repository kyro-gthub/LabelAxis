namespace LabelMaker.Core.Model;

/// <summary>
/// Paper and label positions chosen when printing, instead of the ones the sheet template
/// gives: another paper size, and margins and gaps that decide how many labels (or pages)
/// fit on one sheet — like Publisher's "multiple pages per sheet".
/// </summary>
public sealed class PrintLayout
{
    /// <summary>The paper's name for <see cref="PaperSize"/>: the template's own paper.</summary>
    public const string TemplatePaper = "Template";

    public const string CustomPaper = "Custom";

    /// <summary><see cref="TemplatePaper"/>, a named size ("A4", "Letter", …) or <see cref="CustomPaper"/>.</summary>
    public string PaperSize { get; set; } = TemplatePaper;

    /// <summary>Paper width for <see cref="CustomPaper"/>, portrait.</summary>
    public double PaperWidthMm { get; set; } = 210;

    public double PaperHeightMm { get; set; } = 297;

    /// <summary>False: labels go where the template puts them. True: the margins and gaps below decide.</summary>
    public bool CustomMargins { get; set; }

    public double MarginTopMm { get; set; } = 10;

    public double MarginBottomMm { get; set; } = 10;

    public double MarginLeftMm { get; set; } = 10;

    public double MarginRightMm { get; set; } = 10;

    /// <summary>Space between neighbouring labels across.</summary>
    public double HGapMm { get; set; } = 2;

    /// <summary>Space between neighbouring labels down.</summary>
    public double VGapMm { get; set; } = 2;

    /// <summary>Centre the block of labels in the space inside the margins.</summary>
    public bool CenterOnPaper { get; set; } = true;

    public bool IsTemplateLayout => !CustomMargins && string.Equals(PaperSize, TemplatePaper, StringComparison.OrdinalIgnoreCase);

    public PrintLayout Clone() => (PrintLayout)MemberwiseClone();
}
