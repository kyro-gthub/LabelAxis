using System.Globalization;
using System.Text;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model.Text;

namespace LabelMaker.Core.Printing.Thermal;

/// <summary>
/// ZPL II in printer-native mode. Each design's background goes to the printer's memory once
/// (~DG R:LAXBGn.GRF); every row is then a short format that recalls it (^XG) and adds the text
/// (^A0, the printer's scalable font) and barcodes (^BC, ^BE, ^BQ, …) with that row's values.
/// The job ends by deleting the stored backgrounds (^ID).
/// </summary>
public sealed class ZplNativeEncoder : INativeThermalEncoder
{
    public ThermalLanguage Language => ThermalLanguage.Zpl;

    public bool CanCarry(NativeElement element) => element switch
    {
        // In ^FB a backslash starts a command of its own.
        NativeText text => !text.Text.Contains('\\', StringComparison.Ordinal),
        NativeBarcode { Symbology: NativeSymbology.Code128 } barcode => barcode.Code128Codewords is { } codes && Code128Data(codes) is not null,
        // ~ is the Data Matrix escape character.
        NativeBarcode { Symbology: NativeSymbology.DataMatrix } barcode => !barcode.Data.Contains('~', StringComparison.Ordinal),
        _ => true,
    };

    public byte[] BeginJob(IReadOnlyList<MonochromeImage?> backgrounds, ThermalPrintSettings settings, RollMedia roll, int rowWidthDots, double labelHeightMm)
    {
        ArgumentNullException.ThrowIfNull(backgrounds);
        var sb = new StringBuilder();
        for (int i = 0; i < backgrounds.Count; i++)
        {
            if (backgrounds[i] is not { } image)
            {
                continue;
            }

            int total = image.Bits.Length;
            sb.Append(Invariant($"~DGR:{NativeThermalEncoders.BackgroundName(i)}.GRF,{total},{image.BytesPerRow},"));
            ZplEncoder.AppendCompressed(sb, image);
            sb.Append('\n');
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    public byte[] EncodeRow(NativeRow row, IReadOnlyList<MonochromeImage?> backgrounds, ThermalPrintSettings settings, RollMedia roll, double labelHeightMm, int copies)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(backgrounds);
        ArgumentNullException.ThrowIfNull(settings);
        MediaSensing sensing = settings.Sensing ?? roll.Sensing;
        int offsetX = settings.Dots(settings.OffsetXMm);
        int offsetY = settings.Dots(settings.OffsetYMm);
        var sb = new StringBuilder();

        sb.Append("^XA\n^CI28\n");
        sb.Append(Invariant($"^PW{row.WidthDots}\n"));
        sb.Append(Invariant($"^LL{Math.Max(row.HeightDots, settings.Dots(labelHeightMm))}\n"));
        // Positive offsets move the label home; negative ones shift the whole label as in picture mode.
        sb.Append(Invariant($"^LH{Math.Max(0, offsetX)},{Math.Max(0, offsetY)}\n"));
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
        if (offsetX < 0)
        {
            sb.Append(Invariant($"^LS{Math.Clamp(-offsetX, 0, 9999)}\n"));
        }

        if (offsetY < 0)
        {
            sb.Append(Invariant($"^LT{Math.Clamp(offsetY, -120, 0)}\n"));
        }

        foreach (NativeLabel label in row.Labels)
        {
            if (label.Background >= 0 && label.Background < backgrounds.Count && backgrounds[label.Background] is not null)
            {
                sb.Append(Invariant($"^FO{label.OffsetX},{label.OffsetY}^XGR:{NativeThermalEncoders.BackgroundName(label.Background)}.GRF,1,1^FS\n"));
            }

            foreach (NativeElement element in label.Elements)
            {
                AppendElement(sb, element, label.OffsetX, label.OffsetY);
            }
        }

        sb.Append(Invariant($"^PQ{Math.Max(1, copies)},0,1,N\n"));
        sb.Append("^XZ\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    public byte[] EndJob(IReadOnlyList<MonochromeImage?> backgrounds)
    {
        ArgumentNullException.ThrowIfNull(backgrounds);
        var sb = new StringBuilder();
        for (int i = 0; i < backgrounds.Count; i++)
        {
            if (backgrounds[i] is not null)
            {
                sb.Append(Invariant($"^XA^IDR:{NativeThermalEncoders.BackgroundName(i)}.GRF^FS^XZ\n"));
            }
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static void AppendElement(StringBuilder sb, NativeElement element, int dx, int dy)
    {
        switch (element)
        {
            case NativeText text:
            {
                DotRect box = text.Box.Offset(dx, dy);
                bool across = text.Rotation is NativeRotation.Quarter or NativeRotation.ThreeQuarter;
                char justify = text.Alignment switch
                {
                    HorizontalAlignment.Center => 'C',
                    HorizontalAlignment.Right => 'R',
                    _ => 'L',
                };
                sb.Append(Invariant($"^FO{box.X},{box.Y}^A0{Orientation(text.Rotation)},{text.FontDots}"));
                sb.Append(Invariant($"^FB{(across ? box.Height : box.Width)},1,0,{justify},0^FH_^FD"));
                AppendField(sb, text.Text);
                sb.Append("^FS\n");
                break;
            }

            case NativeBarcode barcode:
                AppendBarcode(sb, barcode, barcode.Box.Offset(dx, dy));
                break;

            case NativePicture picture:
            {
                int total = picture.Image.Bits.Length;
                sb.Append(Invariant($"^FO{picture.X + dx},{picture.Y + dy}^GFA,{total},{total},{picture.Image.BytesPerRow},"));
                ZplEncoder.AppendCompressed(sb, picture.Image);
                sb.Append("^FS\n");
                break;
            }
        }
    }

    private static void AppendBarcode(StringBuilder sb, NativeBarcode b, DotRect box)
    {
        char o = Orientation(b.Rotation);
        string ratio = Math.Clamp(b.WideRatio, 2.0, 3.0).ToString("0.0", CultureInfo.InvariantCulture);
        sb.Append(Invariant($"^BY{b.ModuleDots},{ratio},{b.BarHeightDots}"));
        sb.Append(Invariant($"^FO{box.X},{box.Y}"));
        string data = b.Data;
        switch (b.Symbology)
        {
            case NativeSymbology.Code128:
                sb.Append(Invariant($"^BC{o},{b.BarHeightDots},N,N,N,N"));
                data = Code128Data(b.Code128Codewords ?? Array.Empty<int>()) ?? string.Empty;
                break;
            case NativeSymbology.Ean13:
                sb.Append(Invariant($"^BE{o},{b.BarHeightDots},N,N"));
                break;
            case NativeSymbology.Ean8:
                sb.Append(Invariant($"^B8{o},{b.BarHeightDots},N,N"));
                break;
            case NativeSymbology.UpcA:
                sb.Append(Invariant($"^BU{o},{b.BarHeightDots},N,N,N"));
                break;
            case NativeSymbology.Code39:
                sb.Append(Invariant($"^B3{o},N,{b.BarHeightDots},N,N"));
                break;
            case NativeSymbology.Code93:
                sb.Append(Invariant($"^BA{o},{b.BarHeightDots},N,N,N"));
                break;
            case NativeSymbology.Interleaved2of5:
                sb.Append(Invariant($"^B2{o},{b.BarHeightDots},N,N,N"));
                break;
            case NativeSymbology.Codabar:
                sb.Append(Invariant($"^BK{o},N,{b.BarHeightDots},N,N,{data[0]},{data[^1]}"));
                data = data[1..^1];
                break;
            case NativeSymbology.QrCode:
                // A QR code reads the same at any turn, so it is drawn upright in its square.
                sb.Append(Invariant($"^BQN,2,{Math.Clamp(b.ModuleDots, 1, 10)}"));
                data = QrLevel(b.ErrorCorrection) + "A," + data;
                break;
            case NativeSymbology.DataMatrix:
                sb.Append(Invariant($"^BXN,{b.ModuleDots},200"));
                break;
        }

        sb.Append("^FH_^FD");
        if (b.Symbology == NativeSymbology.Code128)
        {
            sb.Append(data); // already escaped
        }
        else
        {
            AppendField(sb, data);
        }

        sb.Append("^FS\n");
    }

    /// <summary>
    /// Code 128 for ^BC mode N: the start code and every set switch and FNC written as ZPL's
    /// invocation codes (>9 >: >; start A/B/C, >5 >6 >7 code C/B/A, >8 FNC1, >4 shift), so the
    /// printer draws exactly the code values LabelAxis chose. Null for values ZPL cannot be given
    /// this way (a literal ">", control characters, FNC4).
    /// </summary>
    public static string? Code128Data(IReadOnlyList<int> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        if (codes.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        char set;
        switch (codes[0])
        {
            case 103: sb.Append(">9"); set = 'A'; break;
            case 104: sb.Append(">:"); set = 'B'; break;
            case 105: sb.Append(">;"); set = 'C'; break;
            default: return null;
        }

        bool shifted = false;
        for (int i = 1; i < codes.Count; i++)
        {
            int v = codes[i];
            if (set == 'C')
            {
                switch (v)
                {
                    case < 100: sb.Append(v.ToString("00", CultureInfo.InvariantCulture)); break;
                    case 100: sb.Append(">6"); set = 'B'; break;
                    case 101: sb.Append(">7"); set = 'A'; break;
                    case 102: sb.Append(">8"); break;
                    default: return null;
                }

                continue;
            }

            char current = shifted ? (set == 'A' ? 'B' : 'A') : set;
            shifted = false;
            switch (v)
            {
                case 96: sb.Append(">2"); continue; // FNC3
                case 97: sb.Append(">3"); continue; // FNC2
                case 98: sb.Append(">4"); shifted = true; continue;
                case 99: sb.Append(">5"); set = 'C'; continue;
                case 100 when set == 'A': sb.Append(">6"); set = 'B'; continue;
                case 101 when set == 'B': sb.Append(">7"); set = 'A'; continue;
                case 102: sb.Append(">8"); continue;
                case > 95: return null; // FNC4
            }

            char c = current == 'A' && v >= 64 ? (char)(v - 64) : (char)(v + 32);
            if (c < ' ' || c == '\u007F' || c == '>')
            {
                return null;
            }

            AppendField(sb, c.ToString());
        }

        return sb.ToString();
    }

    private static char Orientation(NativeRotation rotation) => rotation switch
    {
        NativeRotation.Quarter => 'R',
        NativeRotation.Half => 'I',
        NativeRotation.ThreeQuarter => 'B',
        _ => 'N',
    };

    private static char QrLevel(QrErrorCorrection level) => level switch
    {
        QrErrorCorrection.Low => 'L',
        QrErrorCorrection.Quartile => 'Q',
        QrErrorCorrection.High => 'H',
        _ => 'M',
    };

    /// <summary>
    /// Field data after ^FH_: the characters ZPL would take as commands (^ ~) and the escape
    /// itself (_) are written as hex, as is everything beyond ASCII (UTF-8, read under ^CI28),
    /// so the job stays plain ASCII.
    /// </summary>
    public static void AppendField(StringBuilder sb, string text)
    {
        Span<byte> bytes = stackalloc byte[4];
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (rune.Value is >= 0x20 and < 0x7F && rune.Value is not ('^' or '~' or '_'))
            {
                sb.Append((char)rune.Value);
                continue;
            }

            int n = rune.EncodeToUtf8(bytes);
            for (int i = 0; i < n; i++)
            {
                sb.Append('_').Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }
        }
    }

    private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
