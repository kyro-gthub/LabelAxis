using System.Globalization;
using LabelMaker.Core.Barcodes;
using ZXing;
using ZXing.Common;
using ZXing.OneD;

namespace LabelMaker.Core.Verification;

/// <summary>
/// Checks how well a barcode in a picture will scan, the way a barcode verifier does — ISO/IEC
/// 15416 for barcodes with bars (ten scan lines across the bars; contrast, edge contrast,
/// modulation, defects, decodability, quiet zones) and the main parts of ISO/IEC 15415 for
/// square codes (contrast, modulation, damaged modules, axial non-uniformity). A picture from
/// a webcam or a phone is not a calibrated verifier: the grade is a close guide, not a
/// certificate. The grading itself is in BarcodeGrader.Linear.cs (bars) and
/// BarcodeGrader.Square.cs (square codes).
/// </summary>
public static partial class BarcodeGrader
{
    private const double Step = 0.5; // samples per pixel along a scan line: 2

    /// <summary>Finds a barcode in the picture and grades it; <see cref="BarcodeQualityReport.NotFound"/> when there is none.</summary>
    public static BarcodeQualityReport Check(GrayImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        Result? result = Decode(image);
        if (result is null)
        {
            return BarcodeQualityReport.NotFound;
        }

        return Is2D(result.BarcodeFormat) ? Grade2D(image, result) : Grade1D(image, result);
    }

    // ---- finding ------------------------------------------------------------------------

    private static Result? Decode(GrayImage image)
    {
        var source = new PlanarYUVLuminanceSource(image.Pixels, image.Width, image.Height, 0, 0, image.Width, image.Height, false);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, TryInverted = true },
        };
        return reader.Decode(source);
    }

    private static bool Is2D(BarcodeFormat f) => f is BarcodeFormat.QR_CODE or BarcodeFormat.DATA_MATRIX or BarcodeFormat.PDF_417 or BarcodeFormat.AZTEC;

    private static BarcodeSymbology? Map(BarcodeFormat f, bool gs1) => f switch
    {
        BarcodeFormat.CODE_128 => gs1 ? BarcodeSymbology.GS1_128 : BarcodeSymbology.Code128,
        BarcodeFormat.EAN_13 => BarcodeSymbology.Ean13,
        BarcodeFormat.EAN_8 => BarcodeSymbology.Ean8,
        BarcodeFormat.UPC_A => BarcodeSymbology.UpcA,
        BarcodeFormat.UPC_E => BarcodeSymbology.UpcE,
        BarcodeFormat.CODE_39 => BarcodeSymbology.Code39,
        BarcodeFormat.CODE_93 => BarcodeSymbology.Code93,
        BarcodeFormat.ITF => BarcodeSymbology.Interleaved2of5,
        BarcodeFormat.CODABAR => BarcodeSymbology.Codabar,
        BarcodeFormat.MSI => BarcodeSymbology.MsiPlessey,
        BarcodeFormat.QR_CODE => BarcodeSymbology.QrCode,
        BarcodeFormat.DATA_MATRIX => BarcodeSymbology.DataMatrix,
        BarcodeFormat.PDF_417 => BarcodeSymbology.Pdf417,
        BarcodeFormat.AZTEC => BarcodeSymbology.Aztec,
        _ => null,
    };

    private static (BarcodeSymbology? Kind, string Name) Describe(Result result)
    {
        string id = result.ResultMetadata?.TryGetValue(ResultMetadataType.SYMBOLOGY_IDENTIFIER, out object? s) == true ? s as string ?? string.Empty : string.Empty;
        bool gs1 = id is "]C1" or "]d2" or "]Q3" || (result.Text?.Length > 0 && result.Text[0] == '\u001d');
        BarcodeSymbology? kind = Map(result.BarcodeFormat, gs1 && result.BarcodeFormat == BarcodeFormat.CODE_128);
        string name = kind?.DisplayName() ?? result.BarcodeFormat.ToString();
        if (gs1 && result.BarcodeFormat is BarcodeFormat.DATA_MATRIX or BarcodeFormat.QR_CODE)
        {
            name = "GS1 " + name;
        }

        return (kind, name);
    }

    // ---- grades ---------------------------------------------------------------------------

    private static QualityGrade Band(double value, double a, double b, double c, double d) =>
        value >= a ? QualityGrade.A : value >= b ? QualityGrade.B : value >= c ? QualityGrade.C : value >= d ? QualityGrade.D : QualityGrade.F;

    private static QualityGrade BandLow(double value, double a, double b, double c, double d) =>
        value <= a ? QualityGrade.A : value <= b ? QualityGrade.B : value <= c ? QualityGrade.C : value <= d ? QualityGrade.D : QualityGrade.F;

    /// <summary>The letter for an average of grades (ISO: 3.5 and over is A…).</summary>
    public static QualityGrade FromScore(double score) =>
        score >= 3.5 ? QualityGrade.A : score >= 2.5 ? QualityGrade.B : score >= 1.5 ? QualityGrade.C : score >= 0.5 ? QualityGrade.D : QualityGrade.F;

    private static string Pct(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}
