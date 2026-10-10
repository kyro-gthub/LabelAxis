using System.Globalization;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Units;

namespace LabelMaker.Core.Printing;

/// <summary>
/// The measurements of a sheet as it prints: page size, margins from each paper edge to
/// the label block, label size, gaps between labels and centre-to-centre pitch. With a
/// <see cref="PrintCoordinateMapper"/> the values include the printer's calibration and
/// the scale setting, so they describe the paper that comes out, not just the template.
/// </summary>
public sealed record SheetMeasurements
{
    public required double PageWidthMm { get; init; }

    public required double PageHeightMm { get; init; }

    public required int Columns { get; init; }

    public required int Rows { get; init; }

    public required double LabelWidthMm { get; init; }

    public required double LabelHeightMm { get; init; }

    public required double MarginTopMm { get; init; }

    public required double MarginLeftMm { get; init; }

    public required double MarginRightMm { get; init; }

    public required double MarginBottomMm { get; init; }

    /// <summary>Space between neighbouring columns; 0 when labels butt together.</summary>
    public required double HGapMm { get; init; }

    /// <summary>Space between neighbouring rows.</summary>
    public required double VGapMm { get; init; }

    /// <summary>Distance from one label's left edge to the next one's.</summary>
    public double HPitchMm => LabelWidthMm + HGapMm;

    public double VPitchMm => LabelHeightMm + VGapMm;

    /// <summary>Printed rectangles of the first (top-left) and last (bottom-right) labels.</summary>
    public required RectMm First { get; init; }

    public required RectMm Last { get; init; }

    /// <summary>Printed rectangle of the label to the right of the first, when there is one.</summary>
    public RectMm? SecondInRow { get; init; }

    /// <summary>Printed rectangle of the label below the first, when there is one.</summary>
    public RectMm? SecondInColumn { get; init; }

    public static SheetMeasurements From(LabelTemplate template, PrintCoordinateMapper? mapper = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        LabelGrid g = template.Grid;
        PageSize page = template.EffectivePage;

        RectMm Map(RectMm r) => mapper?.PageToCanvas(r) ?? r;

        RectMm first = Map(TemplateGeometry.SlotRect(template, 0, 0));
        RectMm last = Map(TemplateGeometry.SlotRect(template, g.Rows - 1, g.Columns - 1));
        RectMm? right = g.Columns > 1 ? Map(TemplateGeometry.SlotRect(template, 0, 1)) : null;
        RectMm? below = g.Rows > 1 ? Map(TemplateGeometry.SlotRect(template, 1, 0)) : null;

        return new SheetMeasurements
        {
            PageWidthMm = page.WidthMm,
            PageHeightMm = page.HeightMm,
            Columns = g.Columns,
            Rows = g.Rows,
            LabelWidthMm = first.Width,
            LabelHeightMm = first.Height,
            MarginLeftMm = first.X,
            MarginTopMm = first.Y,
            MarginRightMm = page.WidthMm - last.Right,
            MarginBottomMm = page.HeightMm - last.Bottom,
            HGapMm = right is { } r ? r.X - first.Right : 0,
            VGapMm = below is { } b ? b.Y - first.Bottom : 0,
            First = first,
            Last = last,
            SecondInRow = right,
            SecondInColumn = below,
        };
    }

    /// <summary>
    /// "38 × 21.15 mm", "0.5 mm": two decimals at most, no trailing zeros. A value that rounds to
    /// nothing is "0", never "-0" (labels that touch measure a hair below zero apart).
    /// </summary>
    public static string Mm(double value)
    {
        double rounded = Math.Round(value, 2);
        return (rounded == 0 ? 0 : rounded).ToString("0.##", CultureInfo.CurrentCulture);
    }

    /// <summary>One line for status bars and dialogs.</summary>
    public string Describe() =>
        $"{Columns} × {Rows} · label {Mm(LabelWidthMm)} × {Mm(LabelHeightMm)} mm · " +
        $"margins top {Mm(MarginTopMm)}, left {Mm(MarginLeftMm)}, right {Mm(MarginRightMm)}, bottom {Mm(MarginBottomMm)} mm · " +
        (HGapMm > 0.005 || VGapMm > 0.005
            ? $"gaps {Mm(HGapMm)} across, {Mm(VGapMm)} down · pitch {Mm(HPitchMm)} × {Mm(VPitchMm)} mm"
            : "no gaps between labels");
}
