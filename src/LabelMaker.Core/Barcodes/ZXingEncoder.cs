using ZXing;
using ZXing.Common;
using ZXing.QrCode.Internal;

namespace LabelMaker.Core.Barcodes;

/// <summary>
/// Adapter over ZXing.Net for every symbology except the Code 128 family. ZXing
/// does the standards-compliant encoding; we read its bit matrix and re-express
/// it as resolution-independent <see cref="BarcodeSymbol"/> geometry so rendering
/// stays vector-crisp at any DPI.
/// </summary>
public static class ZXingEncoder
{
    public static bool Supports(BarcodeSymbology symbology) => TryMapFormat(symbology, out _);

    public static BarcodeSymbol Encode(BarcodeSpec spec, string resolvedValue)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!TryMapFormat(spec.Symbology, out BarcodeFormat format))
        {
            throw new NotSupportedException($"{spec.Symbology} is not handled by the ZXing encoder.");
        }

        bool is2d = spec.Symbology.Is2D();
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.MARGIN] = 0, // we apply the quiet zone ourselves
        };

        if (!string.IsNullOrWhiteSpace(spec.CharacterEncoding))
        {
            hints[EncodeHintType.CHARACTER_SET] = spec.CharacterEncoding;
        }

        switch (spec.Symbology)
        {
            case BarcodeSymbology.QrCode:
                hints[EncodeHintType.ERROR_CORRECTION] = spec.QrEcLevel switch
                {
                    QrErrorCorrection.Low => ErrorCorrectionLevel.L,
                    QrErrorCorrection.Medium => ErrorCorrectionLevel.M,
                    QrErrorCorrection.Quartile => ErrorCorrectionLevel.Q,
                    _ => ErrorCorrectionLevel.H,
                };
                if (spec.Gs1Mode)
                {
                    hints[EncodeHintType.GS1_FORMAT] = true;
                }

                break;

            case BarcodeSymbology.Pdf417 when spec.Pdf417Columns > 0:
                hints[EncodeHintType.PDF417_DIMENSIONS] =
                    new ZXing.PDF417.Internal.Dimensions(spec.Pdf417Columns, spec.Pdf417Columns, 3, 90);
                break;

            case BarcodeSymbology.Aztec:
                hints[EncodeHintType.ERROR_CORRECTION] = 33; // percent
                break;

            case BarcodeSymbology.GS1_128:
            case BarcodeSymbology.Code39 when spec.AddCheckDigit:
                hints[EncodeHintType.GS1_FORMAT] = spec.Gs1Mode;
                break;
        }

        // GS1 data typed as "(01)…(17)…" goes into a 2D code as its element string, with
        // separators after variable-length elements (the brackets are not part of the data).
        string content = resolvedValue;
        if (spec.Gs1Mode && is2d && content.Contains('('))
        {
            content = Gs1.Gs1.ElementString(Gs1.Gs1.Parse(content));
        }

        BitMatrix matrix;
        try
        {
            // ZXing's DataMatrix writer ignores the GS1 setting, so GS1 DataMatrix is made here.
            matrix = spec.Gs1Mode && spec.Symbology == BarcodeSymbology.DataMatrix
                ? Gs1.Gs1DataMatrix.Encode(content)
                : new MultiFormatWriter().encode(content, format, 1, 1, hints);
        }
        catch (Exception ex)
        {
            throw new System.FormatException($"{spec.Symbology.DisplayName()} could not encode \"{resolvedValue}\": {ex.Message}", ex);
        }

        int quiet = NormalizeQuietZone(spec, is2d);

        return is2d
            ? BuildMatrixSymbol(spec.Symbology, matrix, resolvedValue, quiet)
            : BuildLinearSymbol(spec.Symbology, matrix, resolvedValue, quiet);
    }

    private static int NormalizeQuietZone(BarcodeSpec spec, bool is2d)
    {
        int requested = spec.QuietZoneModules;
        int min = spec.Symbology switch
        {
            BarcodeSymbology.QrCode => 4,
            BarcodeSymbology.DataMatrix => 1,
            BarcodeSymbology.Aztec => 0,
            BarcodeSymbology.Pdf417 => 2,
            BarcodeSymbology.Ean13 or BarcodeSymbology.Ean8 or BarcodeSymbology.UpcA or BarcodeSymbology.UpcE => 9,
            _ => is2d ? 0 : 10,
        };
        return Math.Max(requested, min);
    }

    private static BarcodeSymbol BuildLinearSymbol(
        BarcodeSymbology symbology, BitMatrix matrix, string value, int quietZone)
    {
        int width = matrix.Width;
        int row = matrix.Height / 2;

        // Trim ZXing's own margin (we re-apply the quiet zone).
        int left = 0;
        while (left < width && !matrix[left, row])
        {
            left++;
        }

        int right = width - 1;
        while (right >= 0 && !matrix[right, row])
        {
            right--;
        }

        if (left > right)
        {
            throw new System.FormatException($"{symbology.DisplayName()} produced an empty symbol for \"{value}\".");
        }

        var bars = new List<BarModule>();
        int module = 0;

        void AddRun(int w, bool isBar)
        {
            bars.Add(new BarModule(module, w, isBar));
            module += w;
        }

        AddRun(quietZone, isBar: false);

        int runStart = left;
        bool runIsBar = matrix[left, row];
        for (int x = left + 1; x <= right; x++)
        {
            bool bar = matrix[x, row];
            if (bar != runIsBar)
            {
                AddRun(x - runStart, runIsBar);
                runStart = x;
                runIsBar = bar;
            }
        }

        AddRun(right - runStart + 1, runIsBar);
        AddRun(quietZone, isBar: false);

        return new BarcodeSymbol
        {
            Symbology = symbology,
            Dimensionality = BarcodeDimensionality.Linear,
            EncodedValue = value,
            HumanReadableText = value,
            QuietZoneModules = quietZone,
            Bars = bars,
            TotalWidthModules = module,
        };
    }

    private static BarcodeSymbol BuildMatrixSymbol(
        BarcodeSymbology symbology, BitMatrix matrix, string value, int quietZone)
    {
        int rows = matrix.Height;
        int cols = matrix.Width;
        var cells = new bool[rows, cols];
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                cells[y, x] = matrix[x, y];
            }
        }

        return new BarcodeSymbol
        {
            Symbology = symbology,
            Dimensionality = BarcodeDimensionality.TwoD,
            EncodedValue = value,
            HumanReadableText = string.Empty,
            QuietZoneModules = quietZone,
            Matrix = cells,
        };
    }

    private static bool TryMapFormat(BarcodeSymbology symbology, out BarcodeFormat format)
    {
        switch (symbology)
        {
            case BarcodeSymbology.Code39: format = BarcodeFormat.CODE_39; return true;
            case BarcodeSymbology.Code93: format = BarcodeFormat.CODE_93; return true;
            case BarcodeSymbology.Ean13: format = BarcodeFormat.EAN_13; return true;
            case BarcodeSymbology.Ean8: format = BarcodeFormat.EAN_8; return true;
            case BarcodeSymbology.UpcA: format = BarcodeFormat.UPC_A; return true;
            case BarcodeSymbology.UpcE: format = BarcodeFormat.UPC_E; return true;
            case BarcodeSymbology.Itf14:
            case BarcodeSymbology.Interleaved2of5: format = BarcodeFormat.ITF; return true;
            case BarcodeSymbology.Codabar: format = BarcodeFormat.CODABAR; return true;
            case BarcodeSymbology.Isbn13: format = BarcodeFormat.EAN_13; return true; // ISBN-13 is an EAN-13 in 978/979
            case BarcodeSymbology.MsiPlessey: format = BarcodeFormat.MSI; return true;
            case BarcodeSymbology.QrCode: format = BarcodeFormat.QR_CODE; return true;
            case BarcodeSymbology.DataMatrix: format = BarcodeFormat.DATA_MATRIX; return true;
            case BarcodeSymbology.Pdf417: format = BarcodeFormat.PDF_417; return true;
            case BarcodeSymbology.Aztec: format = BarcodeFormat.AZTEC; return true;
            default: format = default; return false;
        }
    }
}
