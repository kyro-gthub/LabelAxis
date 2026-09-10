namespace LabelMaker.Core.Barcodes;

/// <summary>
/// Every barcode type the application can produce. Types are only offered in the
/// UI when the resolved encoder reports support and a round-trip self-test passes
/// (see <c>BarcodeEngine.SupportedSymbologies</c>); nothing here is a stub.
/// </summary>
public enum BarcodeSymbology
{
    // --- Linear, first-party encoder (highest priority) ---
    Code128 = 0,
    Code128A = 1,
    Code128B = 2,
    Code128C = 3,
    GS1_128 = 4,

    // --- Linear, ZXing-backed ---
    Code39 = 10,
    Code93 = 11,
    Ean13 = 12,
    Ean8 = 13,
    UpcA = 14,
    UpcE = 15,
    Itf14 = 16,
    Interleaved2of5 = 17,
    Codabar = 18,
    Isbn13 = 19,
    MsiPlessey = 20,

    // --- 2D, ZXing-backed ---
    QrCode = 40,
    DataMatrix = 41,
    Pdf417 = 42,
    Aztec = 43,
}

public static class BarcodeSymbologyInfo
{
    public static bool Is2D(this BarcodeSymbology s) => s is
        BarcodeSymbology.QrCode or BarcodeSymbology.DataMatrix or
        BarcodeSymbology.Pdf417 or BarcodeSymbology.Aztec;

    public static bool IsCode128Family(this BarcodeSymbology s) => s is
        BarcodeSymbology.Code128 or BarcodeSymbology.Code128A or BarcodeSymbology.Code128B or
        BarcodeSymbology.Code128C or BarcodeSymbology.GS1_128;

    public static bool SupportsHumanReadable(this BarcodeSymbology s) => !s.Is2D();

    public static string DisplayName(this BarcodeSymbology s) => s switch
    {
        BarcodeSymbology.Code128 => "Code 128 (auto)",
        BarcodeSymbology.Code128A => "Code 128 A",
        BarcodeSymbology.Code128B => "Code 128 B",
        BarcodeSymbology.Code128C => "Code 128 C",
        BarcodeSymbology.GS1_128 => "GS1-128",
        BarcodeSymbology.Code39 => "Code 39",
        BarcodeSymbology.Code93 => "Code 93",
        BarcodeSymbology.Ean13 => "EAN-13",
        BarcodeSymbology.Ean8 => "EAN-8",
        BarcodeSymbology.UpcA => "UPC-A",
        BarcodeSymbology.UpcE => "UPC-E",
        BarcodeSymbology.Itf14 => "ITF-14",
        BarcodeSymbology.Interleaved2of5 => "Interleaved 2 of 5",
        BarcodeSymbology.Codabar => "Codabar",
        BarcodeSymbology.Isbn13 => "ISBN-13",
        BarcodeSymbology.MsiPlessey => "MSI Plessey",
        BarcodeSymbology.QrCode => "QR Code",
        BarcodeSymbology.DataMatrix => "Data Matrix",
        BarcodeSymbology.Pdf417 => "PDF417",
        BarcodeSymbology.Aztec => "Aztec",
        _ => s.ToString(),
    };
}
