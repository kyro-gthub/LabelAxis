using LabelMaker.Core.Barcodes;
using ZXing;
using ZXing.Common;

namespace LabelMaker.Tests.Support;

/// <summary>
/// Rasterises a <see cref="BarcodeSymbol"/> to a plain RGB buffer so tests can
/// decode it back with ZXing — the independent verification the spec asks for
/// (spec §40, §6).
/// </summary>
internal static class BarcodeRaster
{
    public static (byte[] Rgb, int Width, int Height) Render1D(BarcodeSymbol symbol, int modulepx = 3, int height = 60)
    {
        if (symbol.Dimensionality != BarcodeDimensionality.Linear)
        {
            throw new InvalidOperationException("Not a 1D symbol.");
        }

        int width = symbol.TotalWidthModules * modulepx;
        var rgb = NewWhite(width, height);

        foreach (BarModule bar in symbol.Bars)
        {
            if (!bar.IsBar)
            {
                continue;
            }

            int x0 = bar.StartModule * modulepx;
            int x1 = x0 + (bar.WidthModules * modulepx);
            for (int y = 0; y < height; y++)
            {
                for (int x = x0; x < x1 && x < width; x++)
                {
                    SetBlack(rgb, width, x, y);
                }
            }
        }

        return (rgb, width, height);
    }

    public static (byte[] Rgb, int Width, int Height) Render2D(BarcodeSymbol symbol, int modulepx = 6)
    {
        if (symbol.Matrix is null)
        {
            throw new InvalidOperationException("Not a 2D symbol.");
        }

        int quiet = Math.Max(symbol.QuietZoneModules, 2);
        int cols = symbol.MatrixColumns + (2 * quiet);
        int rows = symbol.MatrixRows + (2 * quiet);
        int width = cols * modulepx;
        int height = rows * modulepx;
        var rgb = NewWhite(width, height);

        for (int r = 0; r < symbol.MatrixRows; r++)
        {
            for (int c = 0; c < symbol.MatrixColumns; c++)
            {
                if (!symbol.Matrix[r, c])
                {
                    continue;
                }

                int px = (c + quiet) * modulepx;
                int py = (r + quiet) * modulepx;
                for (int y = 0; y < modulepx; y++)
                {
                    for (int x = 0; x < modulepx; x++)
                    {
                        SetBlack(rgb, width, px + x, py + y);
                    }
                }
            }
        }

        return (rgb, width, height);
    }

    public static string? DecodeLinear(BarcodeSymbol symbol, BarcodeFormat format)
    {
        (byte[] rgb, int w, int h) = Render1D(symbol);
        var source = new RGBLuminanceSource(rgb, w, h);
        var bitmap = new BinaryBitmap(new HybridBinarizer(source));
        var hints = new Dictionary<DecodeHintType, object>
        {
            [DecodeHintType.TRY_HARDER] = true,
            [DecodeHintType.POSSIBLE_FORMATS] = new List<BarcodeFormat> { format },
        };
        Result? result = new MultiFormatReader().decode(bitmap, hints);
        return result?.Text;
    }

    public static string? DecodeMatrix(BarcodeSymbol symbol, BarcodeFormat format) => DecodeMatrixResult(symbol, format)?.Text;

    /// <summary>The whole decode result, with the symbology identifier (]d2 for GS1 DataMatrix…).</summary>
    public static Result? DecodeMatrixResult(BarcodeSymbol symbol, BarcodeFormat format)
    {
        (byte[] rgb, int w, int h) = Render2D(symbol);
        var source = new RGBLuminanceSource(rgb, w, h);
        var bitmap = new BinaryBitmap(new HybridBinarizer(source));
        var hints = new Dictionary<DecodeHintType, object>
        {
            [DecodeHintType.TRY_HARDER] = true,
            [DecodeHintType.POSSIBLE_FORMATS] = new List<BarcodeFormat> { format },
        };
        return new MultiFormatReader().decode(bitmap, hints);
    }

    private static byte[] NewWhite(int width, int height)
    {
        var rgb = new byte[width * height * 3];
        Array.Fill(rgb, (byte)255);
        return rgb;
    }

    private static void SetBlack(byte[] rgb, int width, int x, int y)
    {
        int i = ((y * width) + x) * 3;
        rgb[i] = rgb[i + 1] = rgb[i + 2] = 0;
    }
}
