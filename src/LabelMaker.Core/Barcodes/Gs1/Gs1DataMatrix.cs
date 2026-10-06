using System.Text;
using ZXing.Common;
using ZXing.Datamatrix.Encoder;

namespace LabelMaker.Core.Barcodes.Gs1;

/// <summary>
/// GS1 DataMatrix: an ECC 200 DataMatrix that starts with FNC1 and uses FNC1 as the separator
/// between elements. ZXing.Net's own writer ignores the GS1 setting (and its compact encoder
/// leaves stray characters after the data), so the data codewords are written here in ASCII
/// encodation — digit pairs packed two to a codeword, which suits GS1 data — and ZXing does
/// the error correction and module placement.
/// </summary>
public static class Gs1DataMatrix
{
    private const char Fnc1 = (char)232;
    private const char FirstPad = (char)129;

    /// <summary>The symbol for <paramref name="elementString"/> (AIs and data, group separators between elements).</summary>
    public static BitMatrix Encode(string elementString, bool rectangular = false)
    {
        ArgumentNullException.ThrowIfNull(elementString);
        string codewords = DataCodewords(elementString);
        SymbolInfo info = SymbolInfo.lookup(codewords.Length, rectangular ? SymbolShapeHint.FORCE_RECTANGLE : SymbolShapeHint.FORCE_SQUARE)
                          ?? throw new FormatException("The data is too long for a DataMatrix.");
        string full = ErrorCorrection.encodeECC200(Pad(codewords, Capacity(info)), info);
        var placement = new DefaultPlacement(full, info.getSymbolDataWidth(), info.getSymbolDataHeight());
        placement.place();
        return Draw(placement, info);
    }

    /// <summary>FNC1, then the data in ASCII encodation; a group separator becomes FNC1.</summary>
    internal static string DataCodewords(string data)
    {
        var cw = new StringBuilder(data.Length + 1);
        cw.Append(Fnc1);
        for (int i = 0; i < data.Length; i++)
        {
            char c = data[i];
            if (c == Gs1.GroupSeparator)
            {
                cw.Append(Fnc1);
            }
            else if (char.IsAsciiDigit(c) && i + 1 < data.Length && char.IsAsciiDigit(data[i + 1]))
            {
                cw.Append((char)(130 + ((c - '0') * 10) + (data[i + 1] - '0')));
                i++;
            }
            else if (c < 128)
            {
                cw.Append((char)(c + 1));
            }
            else if (c < 256)
            {
                cw.Append((char)235).Append((char)(c - 127)); // upper shift
            }
            else
            {
                throw new FormatException($"“{c}” cannot be put in a GS1 DataMatrix.");
            }
        }

        return cw.ToString();
    }

    // The data codewords the symbol holds: the sum over its interleaved blocks.
    private static int Capacity(SymbolInfo info)
    {
        int total = 0;
        for (int block = 1; block <= info.getInterleavedBlockCount(); block++)
        {
            total += info.getDataLengthForInterleavedBlock(block);
        }

        return total;
    }

    /// <summary>Fills the rest of the symbol: 129 first, then the 253-state randomised pad (ISO/IEC 16022 5.2.3).</summary>
    private static string Pad(string codewords, int capacity)
    {
        var sb = new StringBuilder(codewords, capacity);
        if (sb.Length < capacity)
        {
            sb.Append(FirstPad);
        }

        while (sb.Length < capacity)
        {
            int position = sb.Length + 1;
            int value = 129 + ((149 * position) % 253) + 1;
            sb.Append((char)(value <= 254 ? value : value - 254));
        }

        return sb.ToString();
    }

    /// <summary>The modules with each data region's finder pattern (solid left and bottom, dotted top and right).</summary>
    private static BitMatrix Draw(DefaultPlacement placement, SymbolInfo info)
    {
        int width = info.getSymbolWidth(), height = info.getSymbolHeight();
        int regionWidth = info.matrixWidth, regionHeight = info.matrixHeight;
        var matrix = new BitMatrix(width, height);
        int my = 0;
        for (int y = 0; y < info.getSymbolDataHeight(); y++)
        {
            int mx;
            if (y % regionHeight == 0)
            {
                for (mx = 0; mx < width; mx++)
                {
                    matrix[mx, my] = mx % 2 == 0;
                }

                my++;
            }

            mx = 0;
            for (int x = 0; x < info.getSymbolDataWidth(); x++)
            {
                if (x % regionWidth == 0)
                {
                    matrix[mx++, my] = true;
                }

                matrix[mx++, my] = placement.getBit(x, y);
                if (x % regionWidth == regionWidth - 1)
                {
                    matrix[mx++, my] = y % 2 == 0;
                }
            }

            my++;
            if (y % regionHeight == regionHeight - 1)
            {
                for (mx = 0; mx < width; mx++)
                {
                    matrix[mx, my] = true;
                }

                my++;
            }
        }

        return matrix;
    }
}
