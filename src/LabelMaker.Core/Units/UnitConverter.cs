using System.Globalization;

namespace LabelMaker.Core.Units;

/// <summary>
/// The single place where physical length constants live. Every other part of the
/// application converts through here so the physical/logical/screen/device split
/// described in the architecture stays honest.
/// </summary>
public static class UnitConverter
{
    /// <summary>The only hard physical constant in the product.</summary>
    public const double MmPerInch = 25.4;

    /// <summary>WPF device-independent unit density (1 DIU = 1/96 inch).</summary>
    public const double DiuPerInch = 96.0;

    public const double PointsPerInch = 72.0;

    /// <summary>Convert a value expressed in <paramref name="unit"/> to millimetres.</summary>
    /// <param name="dpi">
    /// Required only when <paramref name="unit"/> is <see cref="Unit.Pixel"/>. Ignored otherwise.
    /// </param>
    public static double ToMillimetres(double value, Unit unit, double? dpi = null) => unit switch
    {
        Unit.Mm => value,
        Unit.Cm => value * 10.0,
        Unit.Inch => value * MmPerInch,
        Unit.Point => value / PointsPerInch * MmPerInch,
        Unit.Pixel => value / RequireDpi(dpi) * MmPerInch,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>Convert millimetres to a value expressed in <paramref name="unit"/>.</summary>
    public static double FromMillimetres(double millimetres, Unit unit, double? dpi = null) => unit switch
    {
        Unit.Mm => millimetres,
        Unit.Cm => millimetres / 10.0,
        Unit.Inch => millimetres / MmPerInch,
        Unit.Point => millimetres / MmPerInch * PointsPerInch,
        Unit.Pixel => millimetres / MmPerInch * RequireDpi(dpi),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>Millimetres to WPF device-independent units (screen/print logical space).</summary>
    public static double MmToDiu(double millimetres) => millimetres / MmPerInch * DiuPerInch;

    /// <summary>WPF device-independent units back to millimetres.</summary>
    public static double DiuToMm(double diu) => diu / DiuPerInch * MmPerInch;

    /// <summary>Millimetres to device pixels at a given output DPI (export / raster / printer).</summary>
    public static double MmToDevicePixels(double millimetres, double dpi) => millimetres / MmPerInch * dpi;

    public static double DevicePixelsToMm(double pixels, double dpi) => pixels / dpi * MmPerInch;

    /// <summary>Typographic points to millimetres (font sizing).</summary>
    public static double PointsToMm(double points) => points / PointsPerInch * MmPerInch;

    public static double MmToPoints(double millimetres) => millimetres / MmPerInch * PointsPerInch;

    private static double RequireDpi(double? dpi)
    {
        if (dpi is null or <= 0)
        {
            throw new InvalidOperationException(
                "Converting to/from pixels requires an explicit, positive DPI. " +
                "Pixels are a UI-only unit and never a physical measurement.");
        }

        return dpi.Value;
    }

    internal static bool TryParseUnit(string token, out Unit unit)
    {
        switch (token.Trim().ToLowerInvariant())
        {
            case "mm" or "millimetre" or "millimeter": unit = Unit.Mm; return true;
            case "cm" or "centimetre" or "centimeter": unit = Unit.Cm; return true;
            case "in" or "inch" or "inches" or "\"": unit = Unit.Inch; return true;
            case "pt" or "point" or "points": unit = Unit.Point; return true;
            case "px" or "pixel" or "pixels": unit = Unit.Pixel; return true;
            default: unit = Unit.Mm; return false;
        }
    }

    internal static NumberFormatInfo FileNumberFormat { get; } = CultureInfo.InvariantCulture.NumberFormat;
}
