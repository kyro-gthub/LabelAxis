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

    /// <summary>The oriented page size actually used for layout.</summary>
    public PageSize EffectivePage => Page.Oriented(Orientation);

    public SizeMm LabelSizeMm => new(Grid.LabelWidthMm, Grid.LabelHeightMm);

    public LabelTemplate WithId(string id) => this with { Id = id };
}
