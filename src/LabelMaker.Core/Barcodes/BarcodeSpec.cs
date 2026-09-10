using LabelMaker.Core.Model;

namespace LabelMaker.Core.Barcodes;

public enum HumanReadablePosition
{
    None = 0,
    Below = 1,
    Above = 2,
}

public enum QrErrorCorrection
{
    Low = 0,     // ~7%
    Medium = 1,  // ~15%
    Quartile = 2, // ~25%
    High = 3,    // ~30%
}

public enum BarcodeRotation
{
    None = 0,
    Rotate90 = 90,
    Rotate180 = 180,
    Rotate270 = 270,
}

/// <summary>
/// Resolution-independent description of a barcode. The same spec is used by
/// <see cref="Model.Objects.BarcodeObject"/>, the standalone image generator and
/// the bulk tools. <see cref="Data"/> may contain <c>{Expression}</c> tokens;
/// they are resolved before encoding.
/// </summary>
public sealed class BarcodeSpec
{
    public BarcodeSymbology Symbology { get; set; } = BarcodeSymbology.Code128;

    /// <summary>Literal value or an expression producing the value.</summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// Narrowest bar / module width in millimetres. Drives physical size for 1D
    /// symbols; for 2D symbols this is the module (cell) size.
    /// </summary>
    public double ModuleWidthMm { get; set; } = 0.33; // ~13 mil, a common minimum

    /// <summary>Bar height in millimetres (1D only). 2D symbols are square.</summary>
    public double BarHeightMm { get; set; } = 15.0;

    /// <summary>Quiet zone in modules on each side (min enforced per symbology).</summary>
    public int QuietZoneModules { get; set; } = 10;

    public HumanReadablePosition HumanReadable { get; set; } = HumanReadablePosition.Below;
    public string HumanReadableFontFamily { get; set; } = "Consolas";
    public double HumanReadableFontSizePt { get; set; } = 8.0;

    /// <summary>Show the raw value or the encoded value with check digit, when they differ.</summary>
    public bool HumanReadableShowCheckDigit { get; set; } = true;

    /// <summary>Add / verify a check digit where the symbology makes it optional (Code 39, I2of5, MSI).</summary>
    public bool AddCheckDigit { get; set; }

    public ColorRgba Foreground { get; set; } = ColorRgba.Black;
    public ColorRgba Background { get; set; } = ColorRgba.White;

    public BarcodeRotation Rotation { get; set; } = BarcodeRotation.None;

    // --- GS1 ---
    /// <summary>
    /// Treat <c>(nn)</c> groups in <see cref="Data"/> as GS1 Application
    /// Identifiers and insert FNC1 where required. Implied for
    /// <see cref="BarcodeSymbology.GS1_128"/>.
    /// </summary>
    public bool Gs1Mode { get; set; }

    // --- 2D ---
    public QrErrorCorrection QrEcLevel { get; set; } = QrErrorCorrection.Medium;

    /// <summary>Character encoding for the payload (default UTF-8 for 2D, Latin-1 for 1D).</summary>
    public string CharacterEncoding { get; set; } = "UTF-8";

    /// <summary>PDF417 columns (0 = auto).</summary>
    public int Pdf417Columns { get; set; }

    public BarcodeSpec Clone() => (BarcodeSpec)MemberwiseClone();
}
