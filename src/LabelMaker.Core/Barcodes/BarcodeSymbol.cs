namespace LabelMaker.Core.Barcodes;

/// <summary>One run of equal modules in a 1D symbol.</summary>
/// <param name="StartModule">0-based module index of the run's left edge (includes leading quiet zone).</param>
/// <param name="WidthModules">Run width in modules (&gt;= 1).</param>
/// <param name="IsBar">True for a dark bar, false for a light space.</param>
public readonly record struct BarModule(int StartModule, int WidthModules, bool IsBar);

public enum BarcodeDimensionality
{
    Linear = 1,
    TwoD = 2,
}

/// <summary>
/// The resolution-independent output of an encoder. Nothing here is rasterised —
/// a drawing surface fills exact rectangles at whatever DPI it runs at, so output
/// is always print-quality (spec §6).
/// </summary>
public sealed class BarcodeSymbol
{
    public required BarcodeSymbology Symbology { get; init; }

    public required BarcodeDimensionality Dimensionality { get; init; }

    /// <summary>The value actually encoded (may include an appended check digit).</summary>
    public required string EncodedValue { get; init; }

    /// <summary>Text to show under/over the symbol (HRI). Empty when not applicable.</summary>
    public string HumanReadableText { get; init; } = string.Empty;

    /// <summary>Quiet zone width in modules applied on every side.</summary>
    public int QuietZoneModules { get; init; }

    // --- 1D ---
    /// <summary>Left-to-right module runs, including the quiet zones as light runs.</summary>
    public IReadOnlyList<BarModule> Bars { get; init; } = Array.Empty<BarModule>();

    /// <summary>Total width in modules including both quiet zones.</summary>
    public int TotalWidthModules { get; init; }

    // --- 2D ---
    /// <summary><c>Matrix[row, col]</c> — true = dark cell. Excludes the quiet zone.</summary>
    public bool[,]? Matrix { get; init; }

    public int MatrixRows => Matrix?.GetLength(0) ?? 0;
    public int MatrixColumns => Matrix?.GetLength(1) ?? 0;

    public bool Is2D => Dimensionality == BarcodeDimensionality.TwoD;

    /// <summary>Natural aspect ratio (width / height) at unit module size, HRI excluded.</summary>
    public double NaturalAspect(double barHeightModules) => Is2D
        ? 1.0
        : barHeightModules <= 0 ? 1.0 : TotalWidthModules / barHeightModules;
}
