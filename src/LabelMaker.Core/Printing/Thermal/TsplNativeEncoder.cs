using System.Globalization;
using System.Text;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model.Text;

namespace LabelMaker.Core.Printing.Thermal;

/// <summary>
/// TSPL/TSPL2 in printer-native mode. Each design's background is downloaded to the printer's
/// memory once as a 1-bit BMP (DOWNLOAD "LAXBGn.BMP"); every row is then CLS, PUTBMP of the
/// background, the text (BLOCK with the scalable font "0") and barcodes (BARCODE, QRCODE,
/// DMATRIX) with that row's values, and PRINT. The job ends with KILL for each stored file.
/// </summary>
public sealed class TsplNativeEncoder : INativeThermalEncoder
{
    private static readonly Encoding Latin1 = Encoding.Latin1;

    public ThermalLanguage Language => ThermalLanguage.Tspl;

    public bool CanCarry(NativeElement element) => element switch
    {
        // TSPL strings have no plain way to hold a quote or a backslash.
        NativeText text => Plain(text.Text),
        NativeBarcode { Symbology: NativeSymbology.Code128 } barcode => barcode.Code128Codewords is { } codes && Code128Data(codes) is not null,
        // A turned rectangular Data Matrix would need the printer's own turning rules; square ones read the same either way.
        NativeBarcode { Symbology: NativeSymbology.DataMatrix } barcode => Plain(barcode.Data) &&
            (barcode.Rotation is NativeRotation.None or NativeRotation.Half || barcode.Box.Width == barcode.Box.Height),
        NativeBarcode barcode => Plain(barcode.Data),
        _ => true,
    };

    public byte[] BeginJob(IReadOnlyList<MonochromeImage?> backgrounds, ThermalPrintSettings settings, RollMedia roll, int rowWidthDots, double labelHeightMm)
    {
        ArgumentNullException.ThrowIfNull(backgrounds);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(roll);
        MediaSensing sensing = settings.Sensing ?? roll.Sensing;
        double widthMm = rowWidthDots * 25.4 / Math.Max(1, settings.Dpi);
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
        // Text is sent as Latin-1, which code page 1252 matches for every letter the printer font is given.
        head.Append("CODEPAGE 1252\r\n");

        using var output = new MemoryStream();
        Write(output, head.ToString());
        for (int i = 0; i < backgrounds.Count; i++)
        {
            if (backgrounds[i] is not { } image)
            {
                continue;
            }

            byte[] bmp = Bmp(image);
            Write(output, Invariant($"DOWNLOAD \"{NativeThermalEncoders.BackgroundName(i)}.BMP\",{bmp.Length},"));
            output.Write(bmp);
            Write(output, "\r\n");
        }

        return output.ToArray();
    }

    public byte[] EncodeRow(NativeRow row, IReadOnlyList<MonochromeImage?> backgrounds, ThermalPrintSettings settings, RollMedia roll, double labelHeightMm, int copies)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(backgrounds);
        using var output = new MemoryStream();
        Write(output, "CLS\r\n");
        foreach (NativeLabel label in row.Labels)
        {
            if (label.Background >= 0 && label.Background < backgrounds.Count && backgrounds[label.Background] is not null)
            {
                Write(output, Invariant($"PUTBMP {label.OffsetX},{label.OffsetY},\"{NativeThermalEncoders.BackgroundName(label.Background)}.BMP\"\r\n"));
            }

            foreach (NativeElement element in label.Elements)
            {
                WriteElement(output, element, label.OffsetX, label.OffsetY, settings);
            }
        }

        Write(output, Invariant($"PRINT 1,{Math.Max(1, copies)}\r\n"));
        return output.ToArray();
    }

    public byte[] EndJob(IReadOnlyList<MonochromeImage?> backgrounds)
    {
        ArgumentNullException.ThrowIfNull(backgrounds);
        var sb = new StringBuilder();
        for (int i = 0; i < backgrounds.Count; i++)
        {
            if (backgrounds[i] is not null)
            {
                sb.Append(Invariant($"KILL \"{NativeThermalEncoders.BackgroundName(i)}.BMP\"\r\n"));
            }
        }

        return Latin1.GetBytes(sb.ToString());
    }

    private static void WriteElement(MemoryStream output, NativeElement element, int dx, int dy, ThermalPrintSettings settings)
    {
        switch (element)
        {
            case NativeText text:
            {
                DotRect box = text.Box.Offset(dx, dy);
                (int x, int y) = Reference(box, text.Rotation);
                bool across = text.Rotation is NativeRotation.Quarter or NativeRotation.ThreeQuarter;
                int width = across ? box.Height : box.Width, height = across ? box.Width : box.Height;
                // Font "0" is sized in points.
                int points = Math.Max(1, (int)Math.Round(text.FontDots * 72.0 / Math.Max(1, settings.Dpi)));
                int align = text.Alignment switch
                {
                    HorizontalAlignment.Center => 2,
                    HorizontalAlignment.Right => 3,
                    _ => 1,
                };
                Write(output, Invariant($"BLOCK {x},{y},{width},{height},\"0\",{(int)text.Rotation},{points},{points},0,{align},\"{text.Text}\"\r\n"));
                break;
            }

            case NativeBarcode barcode:
                Write(output, BarcodeCommand(barcode, barcode.Box.Offset(dx, dy)));
                break;

            case NativePicture picture:
            {
                MonochromeImage image = picture.Image;
                // Overwrite: the picture already holds whatever of the background lies under it.
                Write(output, Invariant($"BITMAP {picture.X + dx},{picture.Y + dy},{image.BytesPerRow},{image.Height},0,"));
                output.Write(TsplEncoder.Inverted(image));
                Write(output, "\r\n");
                break;
            }
        }
    }

    private static string BarcodeCommand(NativeBarcode b, DotRect box)
    {
        switch (b.Symbology)
        {
            case NativeSymbology.QrCode:
            {
                // A QR code reads the same at any turn, so it is drawn upright in its square.
                char level = b.ErrorCorrection switch
                {
                    QrErrorCorrection.Low => 'L',
                    QrErrorCorrection.Quartile => 'Q',
                    QrErrorCorrection.High => 'H',
                    _ => 'M',
                };
                return Invariant($"QRCODE {box.X},{box.Y},{level},{Math.Clamp(b.ModuleDots, 1, 10)},A,0,\"{b.Data}\"\r\n");
            }

            case NativeSymbology.DataMatrix:
                return Invariant($"DMATRIX {box.X},{box.Y},{box.Width},{box.Height},x{b.ModuleDots},\"{b.Data}\"\r\n");
        }

        (string type, string data) = b.Symbology switch
        {
            NativeSymbology.Code128 => ("128M", Code128Data(b.Code128Codewords ?? Array.Empty<int>()) ?? string.Empty),
            NativeSymbology.Ean13 => ("EAN13", b.Data),
            NativeSymbology.Ean8 => ("EAN8", b.Data),
            NativeSymbology.UpcA => ("UPCA", b.Data),
            NativeSymbology.Code39 => ("39S", b.Data),
            NativeSymbology.Code93 => ("93", b.Data),
            NativeSymbology.Interleaved2of5 => ("25", b.Data),
            _ => ("CODA", b.Data),
        };

        (int x, int y) = Reference(box, b.Rotation);
        int wide = b.Symbology is NativeSymbology.Code39 or NativeSymbology.Interleaved2of5 or NativeSymbology.Codabar
            ? (int)Math.Round(b.ModuleDots * Math.Clamp(b.WideRatio, 2.0, 3.0))
            : b.ModuleDots;
        return Invariant($"BARCODE {x},{y},\"{type}\",{b.BarHeightDots},0,{(int)b.Rotation},{b.ModuleDots},{wide},\"{data}\"\r\n");
    }

    /// <summary>
    /// TSPL turns text and barcodes about their reference point (the unturned top-left), so the
    /// point that puts the turned field into <paramref name="box"/> is the box corner the
    /// field's top-left ends up at.
    /// </summary>
    internal static (int X, int Y) Reference(DotRect box, NativeRotation rotation) => rotation switch
    {
        NativeRotation.Quarter => (box.Right, box.Y),
        NativeRotation.Half => (box.Right, box.Bottom),
        NativeRotation.ThreeQuarter => (box.X, box.Bottom),
        _ => (box.X, box.Y),
    };

    /// <summary>
    /// Code 128 for the "128M" type: start code, set switches and FNC codes as TSPL's !nnn
    /// control codes, so the printer draws exactly the code values LabelAxis chose. Null when a
    /// character cannot be written this way (! " \ or a control character).
    /// </summary>
    public static string? Code128Data(IReadOnlyList<int> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        if (codes.Count == 0 || codes[0] is < 103 or > 105)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.Append('!').Append(codes[0].ToString("000", CultureInfo.InvariantCulture));
        char set = codes[0] switch { 103 => 'A', 104 => 'B', _ => 'C' };
        bool shifted = false;
        for (int i = 1; i < codes.Count; i++)
        {
            int v = codes[i];
            if (set == 'C')
            {
                if (v < 100)
                {
                    sb.Append(v.ToString("00", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append('!').Append(v.ToString("000", CultureInfo.InvariantCulture));
                    set = v switch { 100 => 'B', 101 => 'A', _ => 'C' };
                }

                continue;
            }

            char current = shifted ? (set == 'A' ? 'B' : 'A') : set;
            shifted = false;
            if (v >= 96)
            {
                sb.Append('!').Append(v.ToString("000", CultureInfo.InvariantCulture));
                switch (v)
                {
                    case 98: shifted = true; break;
                    case 99: set = 'C'; break;
                    case 100 when set == 'A': set = 'B'; break;
                    case 101 when set == 'B': set = 'A'; break;
                }

                continue;
            }

            char c = current == 'A' && v >= 64 ? (char)(v - 64) : (char)(v + 32);
            if (c < ' ' || c == '\u007F' || c is '!' or '"' or '\\')
            {
                return null;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// A 1-bit Windows BMP of the picture (black = printed), the format PUTBMP reads: rows from
    /// the bottom up, each padded to four bytes, colour 0 black and colour 1 white.
    /// </summary>
    public static byte[] Bmp(MonochromeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int stride = ((image.Width + 31) / 32) * 4;
        const int headerSize = 14 + 40 + 8;
        int size = headerSize + (stride * image.Height);
        var bmp = new byte[size];

        void Int32At(int at, int value) => BitConverter.TryWriteBytes(bmp.AsSpan(at, 4), value);
        void Int16At(int at, short value) => BitConverter.TryWriteBytes(bmp.AsSpan(at, 2), value);

        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        Int32At(2, size);
        Int32At(10, headerSize);
        Int32At(14, 40);
        Int32At(18, image.Width);
        Int32At(22, image.Height);
        Int16At(26, 1);
        Int16At(28, 1);
        Int32At(34, stride * image.Height);
        Int32At(38, 2835); // 72 dpi; printers ignore it
        Int32At(42, 2835);
        Int32At(46, 2);
        // Palette: 0 = black, 1 = white.
        bmp[58] = bmp[59] = bmp[60] = 0xFF;

        for (int y = 0; y < image.Height; y++)
        {
            int row = headerSize + ((image.Height - 1 - y) * stride);
            bmp.AsSpan(row, stride).Fill(0xFF);
            ReadOnlySpan<byte> bits = image.Row(y);
            for (int i = 0; i < bits.Length; i++)
            {
                bmp[row + i] = (byte)~bits[i];
            }

            // Bits past the right edge stay white.
            int spare = (image.BytesPerRow * 8) - image.Width;
            if (spare > 0)
            {
                bmp[row + image.BytesPerRow - 1] |= (byte)((1 << spare) - 1);
            }
        }

        return bmp;
    }

    private static bool Plain(string s) => !s.Contains('"', StringComparison.Ordinal) && !s.Contains('\\', StringComparison.Ordinal);

    private static void Write(MemoryStream output, string text) => output.Write(Latin1.GetBytes(text));

    private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
