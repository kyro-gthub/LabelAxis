using LabelMaker.Core.Units;

namespace LabelMaker.Core.Formtec;

public enum PageOrientation
{
    Portrait = 0,
    Landscape = 1,
}

public enum LabelShape
{
    Rectangle = 0,
    RoundRectangle = 1,
    Ellipse = 2,
}

public enum SheetFillOrder
{
    /// <summary>Fill left-to-right, then top-to-bottom.</summary>
    RowMajor = 0,

    /// <summary>Fill top-to-bottom, then left-to-right.</summary>
    ColumnMajor = 1,
}

/// <summary>What the labels come on.</summary>
public enum LabelMedia
{
    /// <summary>A sheet of labels (A4, Letter) for office printers.</summary>
    Sheet = 0,

    /// <summary>A roll or fanfold strip for a thermal label printer; each "page" is one row of labels.</summary>
    Roll = 1,
}

/// <summary>How a thermal printer finds the start of each label.</summary>
public enum MediaSensing
{
    /// <summary>A see-through gap between die-cut labels.</summary>
    Gap = 0,

    /// <summary>A black mark printed on the back of the liner.</summary>
    BlackMark = 1,

    /// <summary>Continuous stock with no marks; the printer feeds the label length.</summary>
    Continuous = 2,
}

/// <summary>Roll geometry for <see cref="LabelMedia.Roll"/> templates.</summary>
public sealed record RollMedia
{
    /// <summary>Distance between one label (or row of labels) and the next along the roll.</summary>
    public double GapMm { get; init; } = 3.0;

    public MediaSensing Sensing { get; init; } = MediaSensing.Gap;
}

/// <summary>Physical page size in millimetres.</summary>
public readonly record struct PageSize(string Name, double WidthMm, double HeightMm)
{
    public static readonly PageSize A4 = new("A4", 210.0, 297.0);
    public static readonly PageSize A5 = new("A5", 148.0, 210.0);
    public static readonly PageSize Letter = new("Letter", 215.9, 279.4);
    public static readonly PageSize Legal = new("Legal", 215.9, 355.6);

    public PageSize Oriented(PageOrientation orientation) => orientation == PageOrientation.Landscape
        ? this with { WidthMm = Math.Max(WidthMm, HeightMm), HeightMm = Math.Min(WidthMm, HeightMm) }
        : this with { WidthMm = Math.Min(WidthMm, HeightMm), HeightMm = Math.Max(WidthMm, HeightMm) };

    public static PageSize FromName(string name) => name.Trim().ToUpperInvariant() switch
    {
        "A4" => A4,
        "A5" => A5,
        "LETTER" => Letter,
        "LEGAL" => Legal,
        _ => A4,
    };
}

/// <summary>Millimetre insets: top / right / bottom / left.</summary>
public readonly record struct MarginsMm(double Top, double Right, double Bottom, double Left)
{
    public static readonly MarginsMm Zero = new(0, 0, 0, 0);

    public MarginsMm(double uniform) : this(uniform, uniform, uniform, uniform)
    {
    }
}

/// <summary>The label grid on a sheet — the geometric heart of a template.</summary>
public sealed record LabelGrid
{
    public int Columns { get; init; } = 1;
    public int Rows { get; init; } = 1;

    public double LabelWidthMm { get; init; }
    public double LabelHeightMm { get; init; }

    /// <summary>Distance from the physical page's top edge to the first label row's top edge.</summary>
    public double MarginTopMm { get; init; }

    /// <summary>Distance from the physical page's left edge to the first label column's left edge.</summary>
    public double MarginLeftMm { get; init; }

    /// <summary>Horizontal gap between adjacent label columns.</summary>
    public double HGapMm { get; init; }

    /// <summary>Vertical gap between adjacent label rows.</summary>
    public double VGapMm { get; init; }

    public double CornerRadiusMm { get; init; }

    public LabelShape LabelShape { get; init; } = LabelShape.Rectangle;

    public int Count => Columns * Rows;

    /// <summary>Overall block occupied by all labels + inter-label gaps (excludes page margins).</summary>
    public SizeMm BlockSize => new(
        (Columns * LabelWidthMm) + (Math.Max(0, Columns - 1) * HGapMm),
        (Rows * LabelHeightMm) + (Math.Max(0, Rows - 1) * VGapMm));
}

/// <summary>
/// A label-sheet template. Loaded from JSON as pure data — never hard-coded into
/// logic. The same schema serves Formtec, Avery and any other brand.
/// </summary>
public sealed record LabelTemplate
{
    public const string CurrentSchema = "labelmaker.template/1";

    public string Schema { get; init; } = CurrentSchema;

    /// <summary>Stable unique key, e.g. "formtec-3100".</summary>
    public required string Id { get; init; }

    public string Brand { get; init; } = "Formtec";

    public string ProductCode { get; init; } = string.Empty;

    public required string Name { get; init; }

    public List<string> Categories { get; init; } = new();

    public PageSize Page { get; init; } = PageSize.A4;

    public PageOrientation Orientation { get; init; } = PageOrientation.Portrait;

    /// <summary>Printer non-imageable border assumption for preflight (not a hard clip).</summary>
    public MarginsMm PrintableMarginMm { get; init; } = new(4.0);

    public required LabelGrid Grid { get; init; }

    public SheetFillOrder FillOrder { get; init; } = SheetFillOrder.RowMajor;

    public string? Notes { get; init; }

    public string? Source { get; init; }

    /// <summary>True for user/imported templates, false for the shipped built-ins.</summary>
    public bool IsUserDefined { get; init; }

    public LabelMedia Media { get; init; } = LabelMedia.Sheet;

    /// <summary>Gap and sensing for roll media; null for sheets.</summary>
    public RollMedia? Roll { get; init; }

    public bool IsRoll => Media == LabelMedia.Roll;

    /// <summary>
    /// A roll template for a thermal printer: <paramref name="across"/> labels side by side
    /// on a liner as wide as the labels plus the side margins, one row per printed "page".
    /// </summary>
    public static LabelTemplate CreateRoll(
        string id, string name, double labelWidthMm, double labelHeightMm, int across = 1,
        double columnGapMm = 2.0, double sideMarginMm = 0.0, double rollGapMm = 3.0,
        MediaSensing sensing = MediaSensing.Gap, double cornerRadiusMm = 0.0)
    {
        across = Math.Max(1, across);
        double width = (sideMarginMm * 2) + (labelWidthMm * across) + (columnGapMm * (across - 1));
        return new LabelTemplate
        {
            Id = id,
            Name = name,
            Brand = "Thermal roll",
            ProductCode = across == 1 ? $"{labelWidthMm:0.#} × {labelHeightMm:0.#}" : $"{labelWidthMm:0.#} × {labelHeightMm:0.#} ×{across}",
            Categories = new List<string> { "Thermal" },
            Page = new PageSize("Roll", width, labelHeightMm),
            Orientation = width >= labelHeightMm ? PageOrientation.Landscape : PageOrientation.Portrait,
            PrintableMarginMm = MarginsMm.Zero,
            Grid = new LabelGrid
            {
                Columns = across,
                Rows = 1,
                LabelWidthMm = labelWidthMm,
                LabelHeightMm = labelHeightMm,
                MarginLeftMm = sideMarginMm,
                MarginTopMm = 0,
                HGapMm = across > 1 ? columnGapMm : 0,
                VGapMm = 0,
                CornerRadiusMm = cornerRadiusMm,
                LabelShape = cornerRadiusMm > 0 ? LabelShape.RoundRectangle : LabelShape.Rectangle,
            },
            Media = LabelMedia.Roll,
            Roll = new RollMedia { GapMm = rollGapMm, Sensing = sensing },
            Source = "Thermal roll defined in Label Maker",
        };
    }

    /// <summary>The oriented page size actually used for layout.</summary>
    public PageSize EffectivePage => Page.Oriented(Orientation);

    public SizeMm LabelSizeMm => new(Grid.LabelWidthMm, Grid.LabelHeightMm);

    public LabelTemplate WithId(string id) => this with { Id = id };
}
