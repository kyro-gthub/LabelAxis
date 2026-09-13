using System.Globalization;
using System.Text;
using LabelMaker.Core.Formtec;

namespace LabelMaker.Core.Printing.Thermal;

/// <summary>Turns one rendered label into the bytes a thermal printer understands.</summary>
public interface IThermalEncoder
{
    ThermalLanguage Language { get; }

    /// <summary>File extension for "save to file".</summary>
    string FileExtension { get; }

    /// <param name="image">The label as print-head dots, already the full label (row) size.</param>
    /// <param name="labelHeightMm">Physical label length, for the printer's label-length setting.</param>
    /// <param name="copies">How many of this label to print.</param>
    byte[] EncodeLabel(MonochromeImage image, ThermalPrintSettings settings, RollMedia roll, double labelHeightMm, int copies);
}

public static class ThermalEncoders
{
    public static IThermalEncoder For(ThermalLanguage language) => language switch
    {
        ThermalLanguage.Tspl => new TsplEncoder(),
        _ => new ZplEncoder(),
    };
}

/// <summary>
/// ZPL II: each label is a self-contained ^XA…^XZ format with the picture as a compressed
/// ^GFA graphic field, so a job can be stopped between labels without leaving the printer
/// half-way through a format.
/// </summary>
public sealed class ZplEncoder : IThermalEncoder
{
    public ThermalLanguage Language => ThermalLanguage.Zpl;

    public string FileExtension => ".zpl";

    public byte[] EncodeLabel(MonochromeImage image, ThermalPrintSettings settings, RollMedia roll, double labelHeightMm, int copies)
    {
        MediaSensing sensing = settings.Sensing ?? roll.Sensing;
        int offsetX = settings.Dots(settings.OffsetXMm);
        int offsetY = settings.Dots(settings.OffsetYMm);
        var sb = new StringBuilder(image.Bits.Length);

        sb.Append("^XA\n");
        sb.Append("^CI28\n");
        sb.Append(Invariant($"^PW{image.Width}\n"));
        sb.Append(Invariant($"^LL{Math.Max(image.Height, settings.Dots(labelHeightMm))}\n"));
        sb.Append("^LH0,0\n");
        sb.Append(sensing switch
        {
            MediaSensing.BlackMark => "^MNM\n",
            MediaSensing.Continuous => "^MNN\n",
            _ => "^MNY\n",
        });
        sb.Append(settings.AfterPrint switch
        {
            ThermalAfterPrint.Peel => "^MMP\n",
            ThermalAfterPrint.Cut => "^MMC\n",
            _ => "^MMT\n",
        });
        sb.Append(Invariant($"~SD{Math.Clamp(settings.Darkness, 0, 30):00}\n"));
        sb.Append(Invariant($"^PR{Math.Clamp(settings.SpeedIps, 1, 30)}\n"));
        sb.Append(settings.Rotate180 ? "^POI\n" : "^PON\n");

        // Negative offsets: shift the whole label instead (^LS moves left, ^LT moves up).
        if (offsetX < 0)
        {
            sb.Append(Invariant($"^LS{Math.Clamp(-offsetX, 0, 9999)}\n"));
        }

        if (offsetY < 0)
        {
            sb.Append(Invariant($"^LT{Math.Clamp(offsetY, -120, 0)}\n"));
        }

        int total = image.Bits.Length;
        sb.Append(Invariant($"^FO{Math.Max(0, offsetX)},{Math.Max(0, offsetY)}^GFA,{total},{total},{image.BytesPerRow},"));
        AppendCompressed(sb, image);
        sb.Append("^FS\n");
        sb.Append(Invariant($"^PQ{Math.Max(1, copies)},0,1,N\n"));
        sb.Append("^XZ\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>
    /// ZPL's ASCII compression, row by row: hex digits, with runs written as a repeat count
    /// (G–Y = 1–19, g–z = 20–400) before the digit, "," for "zeros to the end of the row",
    /// "!" for "ones to the end", and ":" for "same as the row above".
    /// </summary>
    public static void AppendCompressed(StringBuilder sb, MonochromeImage image)
    {
        const string hex = "0123456789ABCDEF";
        var row = new char[image.BytesPerRow * 2];
        string? previous = null;

        for (int y = 0; y < image.Height; y++)
        {
            ReadOnlySpan<byte> bytes = image.Row(y);
            for (int i = 0; i < bytes.Length; i++)
            {
                row[i * 2] = hex[bytes[i] >> 4];
                row[(i * 2) + 1] = hex[bytes[i] & 0xF];
            }

            string text = new(row);
            if (text == previous)
            {
                sb.Append(':');
                continue;
            }

            previous = text;
            int end = text.Length;
            char tail = '\0';
            if (end > 0 && (text[end - 1] == '0' || text[end - 1] == 'F'))
            {
                char c = text[end - 1];
                int start = end;
                while (start > 0 && text[start - 1] == c)
                {
                    start--;
                }

                // Only worth it when the run is longer than the one-character marker.
                if (end - start > 1)
                {
                    tail = c == '0' ? ',' : '!';
                    end = start;
                }
            }

            int pos = 0;
            while (pos < end)
            {
                char c = text[pos];
                int run = 1;
                while (pos + run < end && text[pos + run] == c)
                {
                    run++;
                }

                AppendRun(sb, c, run);
                pos += run;
            }

            if (tail != '\0')
            {
                sb.Append(tail);
            }
        }
    }

    private static void AppendRun(StringBuilder sb, char c, int count)
    {
        if (count == 1)
        {
            sb.Append(c);
            return;
        }

        while (count > 0)
        {
            // One pair of letters covers up to 419 (z = 400, Y = 19); longer runs repeat.
            int chunk = Math.Min(count, 419);
            int twenties = chunk / 20;
            int units = chunk - (twenties * 20);

            if (twenties > 0)
            {
                sb.Append((char)('g' + twenties - 1));
            }

            if (units > 0)
            {
                sb.Append((char)('G' + units - 1));
            }

            sb.Append(c);
            count -= chunk;
        }
    }

    /// <summary>Expands ZPL ASCII-compressed graphic data back to row bytes (used by tests and the output check).</summary>
    public static byte[] Decompress(string data, int bytesPerRow, int rows)
    {
        var output = new byte[bytesPerRow * rows];
        var current = new StringBuilder(bytesPerRow * 2);
        string previous = new('0', bytesPerRow * 2);
        int row = 0;
        int repeat = 0;

        void EndRow(string text)
        {
            if (row >= rows)
            {
                return;
            }

            for (int i = 0; i < bytesPerRow; i++)
            {
                output[(row * bytesPerRow) + i] = byte.Parse(text.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            previous = text;
            row++;
            current.Clear();
        }

        foreach (char c in data)
        {
            if (c is >= 'G' and <= 'Y')
            {
                repeat += c - 'G' + 1;
            }
            else if (c is >= 'g' and <= 'z')
            {
                repeat += (c - 'g' + 1) * 20;
            }
            else if (c == ',' || c == '!')
            {
                current.Append(c == ',' ? '0' : 'F', (bytesPerRow * 2) - current.Length);
                EndRow(current.ToString());
            }
            else if (c == ':')
            {
                EndRow(previous);
            }
            else if (Uri.IsHexDigit(c))
            {
                current.Append(char.ToUpperInvariant(c), Math.Max(1, repeat));
                repeat = 0;
                if (current.Length >= bytesPerRow * 2)
                {
                    EndRow(current.ToString(0, bytesPerRow * 2));
                }
            }
        }

        return output;
    }

    private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// TSPL/TSPL2: SIZE/GAP set the stock, then CLS, one BITMAP with the whole label and PRINT.
/// In a TSPL bitmap a 0 bit prints, so the dots are inverted on the way out.
/// </summary>
public sealed class TsplEncoder : IThermalEncoder
{
    public ThermalLanguage Language => ThermalLanguage.Tspl;

    public string FileExtension => ".prn";

    public byte[] EncodeLabel(MonochromeImage image, ThermalPrintSettings settings, RollMedia roll, double labelHeightMm, int copies)
    {
        MediaSensing sensing = settings.Sensing ?? roll.Sensing;
        double widthMm = image.Width * 25.4 / Math.Max(1, settings.Dpi);
        var head = new StringBuilder();
        head.Append(Invariant($"SIZE {widthMm:0.##} mm,{labelHeightMm:0.##} mm\r\n"));
        head.Append(sensing switch
        {
            MediaSensing.BlackMark => Invariant($"BLINE {roll.GapMm:0.##} mm,0 mm\r\n"),
            MediaSensing.Continuous => "GAP 0 mm,0 mm\r\n",
            _ => Invariant($"GAP {roll.GapMm:0.##} mm,0 mm\r\n"),
        });
        head.Append(settings.Rotate180 ? "DIRECTION 0,0\r\n" : "DIRECTION 1,0\r\n");
        head.Append(Invariant($"REFERENCE {Math.Max(0, settings.Dots(settings.OffsetXMm))},{Math.Max(0, settings.Dots(settings.OffsetYMm))}\r\n"));
        head.Append(Invariant($"SPEED {Math.Clamp(settings.SpeedIps, 1, 12)}\r\n"));
        head.Append(Invariant($"DENSITY {Math.Clamp((int)Math.Round(settings.Darkness / 2.0), 0, 15)}\r\n"));
        head.Append(settings.AfterPrint == ThermalAfterPrint.Cut ? "SET CUTTER 1\r\n" : "SET CUTTER OFF\r\n");
        head.Append(settings.AfterPrint == ThermalAfterPrint.Peel ? "SET PEEL ON\r\n" : "SET PEEL OFF\r\n");
        head.Append("CLS\r\n");
        head.Append(Invariant($"BITMAP 0,0,{image.BytesPerRow},{image.Height},0,"));

        byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
        byte[] tailBytes = Encoding.ASCII.GetBytes(Invariant($"\r\nPRINT 1,{Math.Max(1, copies)}\r\n"));
        var output = new byte[headBytes.Length + image.Bits.Length + tailBytes.Length];
        headBytes.CopyTo(output, 0);
        for (int i = 0; i < image.Bits.Length; i++)
        {
            output[headBytes.Length + i] = (byte)~image.Bits[i];
        }

        // Padding bits past the right edge must not print either.
        int spare = (image.BytesPerRow * 8) - image.Width;
        if (spare > 0)
        {
            byte keepWhite = (byte)((1 << spare) - 1);
            for (int y = 0; y < image.Height; y++)
            {
                output[headBytes.Length + (y * image.BytesPerRow) + image.BytesPerRow - 1] |= keepWhite;
            }
        }

        tailBytes.CopyTo(output, headBytes.Length + image.Bits.Length);
        return output;
    }

    private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
