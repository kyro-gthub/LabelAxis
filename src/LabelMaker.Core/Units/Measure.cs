using System.Globalization;

namespace LabelMaker.Core.Units;

/// <summary>
/// An immutable physical length. Internally always millimetres; the originating
/// <see cref="Unit"/> is remembered only so the UI can round-trip a user's
/// preferred display unit. Two measures are equal when their millimetre values
/// are equal to within <see cref="Epsilon"/>.
/// </summary>
public readonly struct Measure : IEquatable<Measure>, IComparable<Measure>
{
    /// <summary>Tolerance for physical equality: 1 micron.</summary>
    public const double Epsilon = 1e-3;

    public static readonly Measure Zero = new(0.0, Unit.Mm);

    private Measure(double millimetres, Unit displayUnit)
    {
        Millimetres = millimetres;
        DisplayUnit = displayUnit;
    }

    /// <summary>Canonical value in millimetres.</summary>
    public double Millimetres { get; }

    /// <summary>The unit the value was authored in, for UI display only.</summary>
    public Unit DisplayUnit { get; }

    public double Centimetres => Millimetres / 10.0;
    public double Inches => Millimetres / UnitConverter.MmPerInch;
    public double Points => UnitConverter.MmToPoints(Millimetres);
    public double Diu => UnitConverter.MmToDiu(Millimetres);

    public static Measure FromMm(double mm) => new(mm, Unit.Mm);
    public static Measure FromCm(double cm) => new(cm * 10.0, Unit.Cm);
    public static Measure FromInches(double inch) => new(inch * UnitConverter.MmPerInch, Unit.Inch);
    public static Measure FromPoints(double pt) => new(UnitConverter.PointsToMm(pt), Unit.Point);
    public static Measure FromDiu(double diu) => new(UnitConverter.DiuToMm(diu), Unit.Mm);

    public static Measure From(double value, Unit unit, double? dpi = null) =>
        new(UnitConverter.ToMillimetres(value, unit, dpi), unit);

    /// <summary>Pixels at a given DPI. The result's display unit is <see cref="Unit.Pixel"/>.</summary>
    public static Measure FromPixels(double px, double dpi) =>
        new(UnitConverter.ToMillimetres(px, Unit.Pixel, dpi), Unit.Pixel);

    /// <summary>Value expressed in <paramref name="unit"/> (needs a DPI only for pixels).</summary>
    public double In(Unit unit, double? dpi = null) =>
        UnitConverter.FromMillimetres(Millimetres, unit, dpi);

    public double ToDevicePixels(double dpi) => UnitConverter.MmToDevicePixels(Millimetres, dpi);

    public Measure WithDisplayUnit(Unit unit) => new(Millimetres, unit);

    public Measure Clamp(Measure min, Measure max) =>
        new(Math.Clamp(Millimetres, min.Millimetres, max.Millimetres), DisplayUnit);

    public static Measure operator +(Measure a, Measure b) => new(a.Millimetres + b.Millimetres, a.DisplayUnit);
    public static Measure operator -(Measure a, Measure b) => new(a.Millimetres - b.Millimetres, a.DisplayUnit);
    public static Measure operator *(Measure a, double factor) => new(a.Millimetres * factor, a.DisplayUnit);
    public static Measure operator /(Measure a, double divisor) => new(a.Millimetres / divisor, a.DisplayUnit);

    public static bool operator <(Measure a, Measure b) => a.Millimetres < b.Millimetres - Epsilon;
    public static bool operator >(Measure a, Measure b) => a.Millimetres > b.Millimetres + Epsilon;
    public static bool operator <=(Measure a, Measure b) => a.Millimetres <= b.Millimetres + Epsilon;
    public static bool operator >=(Measure a, Measure b) => a.Millimetres >= b.Millimetres - Epsilon;
    public static bool operator ==(Measure a, Measure b) => a.Equals(b);
    public static bool operator !=(Measure a, Measure b) => !a.Equals(b);

    public int CompareTo(Measure other) =>
        Math.Abs(Millimetres - other.Millimetres) <= Epsilon ? 0 : Millimetres.CompareTo(other.Millimetres);

    public bool Equals(Measure other) => Math.Abs(Millimetres - other.Millimetres) <= Epsilon;

    public override bool Equals(object? obj) => obj is Measure m && Equals(m);

    public override int GetHashCode() => Math.Round(Millimetres, 3).GetHashCode();

    /// <summary>
    /// Culture-invariant "12.5 mm" form used in files. Pixels are never persisted,
    /// so a pixel display unit is written as millimetres.
    /// </summary>
    public string ToInvariantString() =>
        ToInvariantString(DisplayUnit == Unit.Pixel ? Unit.Mm : DisplayUnit);

    public string ToInvariantString(Unit unit)
    {
        Unit effective = unit == Unit.Pixel ? Unit.Mm : unit;
        double v = In(effective);
        return string.Create(CultureInfo.InvariantCulture, $"{v:0.####} {effective.Symbol()}");
    }

    /// <summary>Culture-aware display form for the property grid.</summary>
    public string ToDisplayString(Unit unit, int decimals = 2, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        double v = In(unit);
        return string.Format(culture, "{0} {1}", Math.Round(v, decimals).ToString("0.##", culture), unit.Symbol());
    }

    public override string ToString() => ToInvariantString();

    /// <summary>
    /// Parses "40", "40 mm", "1.5in", "12 pt". A bare number uses
    /// <paramref name="assumedUnit"/>. Pixels require <paramref name="dpi"/>.
    /// </summary>
    public static bool TryParse(string? text, Unit assumedUnit, out Measure measure, double? dpi = null)
    {
        measure = Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string s = text.Trim();
        int splitAt = s.Length;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool numeric = char.IsDigit(c) || c is '.' or ',' or '-' or '+' or ' '
                           || c is 'e' or 'E';
            if (!numeric)
            {
                splitAt = i;
                break;
            }
        }

        string numberPart = s[..splitAt].Trim();
        string unitPart = s[splitAt..].Trim();

        // Accept both "1,5" and "1.5" defensively.
        numberPart = numberPart.Replace(" ", string.Empty);
        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && !double.TryParse(numberPart, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            return false;
        }

        Unit unit = assumedUnit;
        if (unitPart.Length > 0 && !UnitConverter.TryParseUnit(unitPart, out unit))
        {
            return false;
        }

        if (unit == Unit.Pixel && dpi is null or <= 0)
        {
            return false;
        }

        measure = From(value, unit, dpi);
        return true;
    }

    public static Measure Parse(string text, Unit assumedUnit, double? dpi = null) =>
        TryParse(text, assumedUnit, out var m, dpi)
            ? m
            : throw new FormatException($"'{text}' is not a valid measurement.");
}
