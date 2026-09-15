using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Units;

namespace LabelMaker.Core.Printing;

/// <summary>
/// Turns a project's sheet template and the print dialog's paper and margin choices into the
/// template a print job actually lays out: the chosen paper, and as many labels as fit inside
/// the margins when custom margins are on.
/// </summary>
public static class PrintLayoutResolver
{
    /// <summary>Paper sizes offered in the print dialog, portrait.</summary>
    public static readonly IReadOnlyList<PageSize> Papers = new[]
    {
        new PageSize("A3", 297, 420),
        PageSize.A4,
        PageSize.A5,
        new PageSize("A6", 105, 148),
        new PageSize("B5", 176, 250),
        PageSize.Letter,
        PageSize.Legal,
    };

    /// <summary>The template the job prints with, for <paramref name="project"/> and its print settings.</summary>
    public static LabelTemplate Resolve(ProjectModel project, PrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(settings);
        LabelTemplate template = project.Template
            ?? throw new InvalidOperationException("The project has no sheet template selected.");
        return Resolve(template, settings.Layout, settings.Landscape, project.MasterLabel.SizeMm);
    }

    /// <summary>
    /// <paramref name="template"/> on the chosen paper. With custom margins the label grid is
    /// rebuilt: labels of <paramref name="labelSize"/> (the template's own size when empty),
    /// separated by the gaps, as many across and down as fit inside the margins.
    /// </summary>
    public static LabelTemplate Resolve(LabelTemplate template, PrintLayout? layout, bool landscape, SizeMm labelSize)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (layout is null || layout.IsTemplateLayout || template.IsRoll)
        {
            return template;
        }

        PageSize paper = PaperFor(template, layout);
        // A custom size is used the way round it was typed; Landscape turns it.
        bool custom = string.Equals(layout.PaperSize, PrintLayout.CustomPaper, StringComparison.OrdinalIgnoreCase);
        PageOrientation orientation = custom
            ? (landscape != (layout.PaperWidthMm > layout.PaperHeightMm) ? PageOrientation.Landscape : PageOrientation.Portrait)
            : (landscape ? PageOrientation.Landscape : template.Orientation);
        LabelTemplate result = template with { Page = paper, Orientation = orientation };
        if (!layout.CustomMargins)
        {
            return result;
        }

        PageSize page = result.EffectivePage;
        double w = labelSize.Width > 0 ? labelSize.Width : template.Grid.LabelWidthMm;
        double h = labelSize.Height > 0 ? labelSize.Height : template.Grid.LabelHeightMm;
        double hGap = Math.Max(0, layout.HGapMm);
        double vGap = Math.Max(0, layout.VGapMm);
        double top = Math.Max(0, layout.MarginTopMm), bottom = Math.Max(0, layout.MarginBottomMm);
        double left = Math.Max(0, layout.MarginLeftMm), right = Math.Max(0, layout.MarginRightMm);

        double roomX = page.WidthMm - left - right;
        double roomY = page.HeightMm - top - bottom;
        int columns = Fit(roomX, w, hGap);
        int rows = Fit(roomY, h, vGap);
        if (columns == 0 || rows == 0)
        {
            throw new InvalidOperationException(
                $"A {SheetMeasurements.Mm(w)} × {SheetMeasurements.Mm(h)} mm label does not fit on {page.Name} " +
                $"({SheetMeasurements.Mm(page.WidthMm)} × {SheetMeasurements.Mm(page.HeightMm)} mm) inside these margins.");
        }

        double blockW = (columns * w) + ((columns - 1) * hGap);
        double blockH = (rows * h) + ((rows - 1) * vGap);
        return result with
        {
            Grid = template.Grid with
            {
                Columns = columns,
                Rows = rows,
                LabelWidthMm = w,
                LabelHeightMm = h,
                HGapMm = columns > 1 ? hGap : 0,
                VGapMm = rows > 1 ? vGap : 0,
                MarginLeftMm = layout.CenterOnPaper ? left + ((roomX - blockW) / 2) : left,
                MarginTopMm = layout.CenterOnPaper ? top + ((roomY - blockH) / 2) : top,
            },
        };
    }

    /// <summary>The portrait paper for the layout's paper choice.</summary>
    public static PageSize PaperFor(LabelTemplate template, PrintLayout layout)
    {
        if (string.Equals(layout.PaperSize, PrintLayout.TemplatePaper, StringComparison.OrdinalIgnoreCase))
        {
            return template.Page;
        }

        if (string.Equals(layout.PaperSize, PrintLayout.CustomPaper, StringComparison.OrdinalIgnoreCase))
        {
            double a = Math.Clamp(layout.PaperWidthMm, 10, 2000), b = Math.Clamp(layout.PaperHeightMm, 10, 2000);
            return new PageSize("Custom", Math.Min(a, b), Math.Max(a, b));
        }

        return Papers.FirstOrDefault(p => string.Equals(p.Name, layout.PaperSize, StringComparison.OrdinalIgnoreCase)) is { Name: not null } named
            ? named
            : template.Page;
    }

    private static int Fit(double room, double size, double gap) =>
        size <= 0 || room + 1e-6 < size ? 0 : (int)Math.Floor(((room + gap) / (size + gap)) + 1e-6);
}
