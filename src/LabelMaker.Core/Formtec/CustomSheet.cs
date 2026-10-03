namespace LabelMaker.Core.Formtec;

/// <summary>
/// What someone types in to make their own sticker sheet: the sticker's size and shape, the
/// paper, the margins and the gaps. Columns and rows can be given, or left empty to fit as
/// many stickers as the paper holds.
/// </summary>
public sealed record CustomSheetSpec
{
    public string Name { get; init; } = "My stickers";

    public double LabelWidthMm { get; init; } = 50;
    public double LabelHeightMm { get; init; } = 25;
    public LabelShape Shape { get; init; } = LabelShape.Rectangle;

    /// <summary>Corner radius for <see cref="LabelShape.RoundRectangle"/>.</summary>
    public double CornerRadiusMm { get; init; } = 2;

    public PageSize Paper { get; init; } = PageSize.A4;
    public PageOrientation Orientation { get; init; } = PageOrientation.Portrait;

    public double MarginTopMm { get; init; } = 10;
    public double MarginBottomMm { get; init; } = 10;
    public double MarginLeftMm { get; init; } = 10;
    public double MarginRightMm { get; init; } = 10;

    /// <summary>Space between stickers side by side.</summary>
    public double HGapMm { get; init; } = 2;

    /// <summary>Space between rows.</summary>
    public double VGapMm { get; init; } = 2;

    /// <summary>Stickers across; null fits as many as the paper allows.</summary>
    public int? Columns { get; init; }

    /// <summary>Stickers down; null fits as many as the paper allows.</summary>
    public int? Rows { get; init; }

    /// <summary>Put the block of stickers in the middle of the paper instead of at the top-left margins.</summary>
    public bool CenterOnPaper { get; init; }
}

/// <summary>Turns a <see cref="CustomSheetSpec"/> into a sheet template the rest of the app uses like any other.</summary>
public static class CustomSheet
{
    public const string Brand = "Custom";

    /// <summary>How many stickers fit across and down inside the margins.</summary>
    public static (int Columns, int Rows) Fit(CustomSheetSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        PageSize page = spec.Paper.Oriented(spec.Orientation);
        double usableW = page.WidthMm - spec.MarginLeftMm - spec.MarginRightMm;
        double usableH = page.HeightMm - spec.MarginTopMm - spec.MarginBottomMm;
        return (Count(usableW, spec.LabelWidthMm, spec.HGapMm), Count(usableH, spec.LabelHeightMm, spec.VGapMm));
    }

    /// <summary>
    /// The sheet template, or the reason it cannot be made (stickers bigger than the paper,
    /// a grid that runs off the edge). The template is marked as the user's own.
    /// </summary>
    public static LabelTemplate Build(CustomSheetSpec spec, string id)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (spec.LabelWidthMm <= 0 || spec.LabelHeightMm <= 0)
        {
            throw new InvalidOperationException("The sticker needs a width and a height.");
        }

        PageSize page = spec.Paper.Oriented(spec.Orientation);
        (int fitCols, int fitRows) = Fit(spec);
        int cols = spec.Columns is > 0 ? spec.Columns.Value : fitCols;
        int rows = spec.Rows is > 0 ? spec.Rows.Value : fitRows;
        if (cols < 1 || rows < 1)
        {
            throw new InvalidOperationException(
                $"A {spec.LabelWidthMm:0.##} × {spec.LabelHeightMm:0.##} mm sticker does not fit on {page.Name} " +
                $"({page.WidthMm:0.#} × {page.HeightMm:0.#} mm) inside these margins.");
        }

        double blockW = (cols * spec.LabelWidthMm) + ((cols - 1) * spec.HGapMm);
        double blockH = (rows * spec.LabelHeightMm) + ((rows - 1) * spec.VGapMm);
        double left = spec.CenterOnPaper ? (page.WidthMm - blockW) / 2.0 : spec.MarginLeftMm;
        double top = spec.CenterOnPaper ? (page.HeightMm - blockH) / 2.0 : spec.MarginTopMm;
        if (left < -0.01 || top < -0.01 || left + blockW > page.WidthMm + 0.01 || top + blockH > page.HeightMm + 0.01)
        {
            throw new InvalidOperationException(
                $"{cols} across × {rows} down needs {blockW:0.#} × {blockH:0.#} mm, which runs off the {page.Name} paper.");
        }

        LabelShape shape = spec.Shape;
        double radius = shape == LabelShape.RoundRectangle
            ? Math.Clamp(spec.CornerRadiusMm, 0, Math.Min(spec.LabelWidthMm, spec.LabelHeightMm) / 2.0)
            : 0;

        return new LabelTemplate
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(spec.Name) ? "My stickers" : spec.Name.Trim(),
            Brand = Brand,
            ProductCode = $"{spec.LabelWidthMm:0.##} × {spec.LabelHeightMm:0.##} mm",
            Categories = new List<string> { "Custom" },
            Page = spec.Paper,
            Orientation = spec.Orientation,
            Grid = new LabelGrid
            {
                Columns = cols,
                Rows = rows,
                LabelWidthMm = spec.LabelWidthMm,
                LabelHeightMm = spec.LabelHeightMm,
                MarginLeftMm = Math.Round(Math.Max(0, left), 2),
                MarginTopMm = Math.Round(Math.Max(0, top), 2),
                HGapMm = cols > 1 ? spec.HGapMm : 0,
                VGapMm = rows > 1 ? spec.VGapMm : 0,
                CornerRadiusMm = radius,
                LabelShape = shape,
            },
            IsUserDefined = true,
            Source = "Custom sticker size made in LabelAxis",
        };
    }

    /// <summary>Reads a sheet back into the form that made it, so it can be edited.</summary>
    public static CustomSheetSpec ToSpec(LabelTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        LabelGrid g = template.Grid;
        PageSize page = template.EffectivePage;
        Units.SizeMm block = g.BlockSize;
        return new CustomSheetSpec
        {
            Name = template.Name,
            LabelWidthMm = g.LabelWidthMm,
            LabelHeightMm = g.LabelHeightMm,
            Shape = g.LabelShape,
            CornerRadiusMm = g.LabelShape == LabelShape.RoundRectangle ? g.CornerRadiusMm : 2,
            Paper = template.Page,
            Orientation = template.Orientation,
            MarginTopMm = g.MarginTopMm,
            MarginLeftMm = g.MarginLeftMm,
            // The sheet only records where the first sticker is. With the counts fixed, the right
            // and bottom margins change nothing; mirror the left and top ones (or the room that is
            // actually left, if less) so switching to "fit as many" starts somewhere sensible.
            MarginRightMm = Math.Clamp(Math.Round(page.WidthMm - g.MarginLeftMm - block.Width, 2), 0, g.MarginLeftMm),
            MarginBottomMm = Math.Clamp(Math.Round(page.HeightMm - g.MarginTopMm - block.Height, 2), 0, g.MarginTopMm),
            HGapMm = g.HGapMm,
            VGapMm = g.VGapMm,
            Columns = g.Columns,
            Rows = g.Rows,
        };
    }

    /// <summary>A new id for a custom sheet that no other template uses.</summary>
    public static string NewId(Func<string, bool> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        string id;
        do
        {
            id = "custom-" + Guid.NewGuid().ToString("N")[..8];
        }
        while (taken(id));

        return id;
    }

    private static int Count(double space, double item, double gap) =>
        item <= 0 || space < item ? 0 : 1 + (int)Math.Floor(((space - item) / (item + gap)) + 1e-9);
}
