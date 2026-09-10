namespace LabelMaker.Core.Units;

/// <summary>
/// Physical (and one screen-only) length units understood by the property grid
/// and template schema. The canonical internal store is always millimetres.
/// </summary>
public enum Unit
{
    /// <summary>Millimetre. Canonical unit for all persisted geometry.</summary>
    Mm = 0,

    /// <summary>Centimetre (10 mm).</summary>
    Cm = 1,

    /// <summary>Inch (25.4 mm).</summary>
    Inch = 2,

    /// <summary>Typographic point (1/72 inch).</summary>
    Point = 3,

    /// <summary>
    /// Pixels. UI-only. A pixel has no physical size without a DPI, so converting
    /// a <see cref="Measure"/> to or from <see cref="Pixel"/> requires an explicit
    /// DPI and otherwise throws.
    /// </summary>
    Pixel = 4,
}

public static class UnitExtensions
{
    public static string Symbol(this Unit unit) => unit switch
    {
        Unit.Mm => "mm",
        Unit.Cm => "cm",
        Unit.Inch => "in",
        Unit.Point => "pt",
        Unit.Pixel => "px",
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    public static bool IsPhysical(this Unit unit) => unit != Unit.Pixel;
}
