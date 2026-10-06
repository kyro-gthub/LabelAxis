using System.Globalization;
using LabelMaker.Core.Barcodes;
using ZXing;
using ZXing.Common;
using ZXing.OneD;

namespace LabelMaker.Core.Verification;

/// <summary>A picture in shades of grey: 0 is black, 255 is white, one byte per pixel, row by row.</summary>
public sealed class GrayImage
{
    public GrayImage(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            throw new ArgumentException("The picture's size does not match its pixels.", nameof(pixels));
        }

        Pixels = pixels;
        Width = width;
        Height = height;
    }

    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>The reflectance (0 to 1) at a point between pixels, by bilinear interpolation.</summary>
    public double At(double x, double y)
    {
        x = Math.Clamp(x, 0, Width - 1.001);
        y = Math.Clamp(y, 0, Height - 1.001);
        int x0 = (int)x, y0 = (int)y;
        double fx = x - x0, fy = y - y0;
        int i = (y0 * Width) + x0;
        double top = (Pixels[i] * (1 - fx)) + (Pixels[i + 1] * fx);
        double bottom = (Pixels[i + Width] * (1 - fx)) + (Pixels[i + Width + 1] * fx);
        return ((top * (1 - fy)) + (bottom * fy)) / 255.0;
    }

    public bool Contains(double x, double y) => x >= 0 && y >= 0 && x <= Width - 1 && y <= Height - 1;

    /// <summary>A picture from 32-bit BGRA pixels (as WPF and cameras give them).</summary>
    public static GrayImage FromBgra(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        var gray = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = bgra.Slice(y * stride, width * 4);
            for (int x = 0; x < width; x++)
            {
                int b = row[x * 4], g = row[(x * 4) + 1], r = row[(x * 4) + 2];
                gray[(y * width) + x] = (byte)(((r * 77) + (g * 150) + (b * 29)) >> 8);
            }
        }

        return new GrayImage(gray, width, height);
    }
}

/// <summary>A barcode quality grade, A (best) to F (fails), as ISO/IEC 15416 and 15415 grade them.</summary>
public enum QualityGrade
{
    F = 0,
    D = 1,
    C = 2,
    B = 3,
    A = 4,
}

/// <summary>One measured quality: its value as shown, its grade, and what it means.</summary>
public sealed record QualityParameter(string Name, string Value, QualityGrade? Grade, string Meaning);

/// <summary>The result of checking a barcode in a picture.</summary>
public sealed class BarcodeQualityReport
{
    public bool Found { get; init; }

    /// <summary>The barcode type as LabelAxis names it ("Code 128"), when found.</summary>
    public string Symbology { get; init; } = string.Empty;

    public BarcodeSymbology? Kind { get; init; }

    /// <summary>What the barcode says.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>The overall grade, and as a number from 0.0 to 4.0 (the ISO way to write it).</summary>
    public QualityGrade Grade { get; init; }

    public double Score { get; init; }

    public IReadOnlyList<QualityParameter> Parameters { get; init; } = Array.Empty<QualityParameter>();

    /// <summary>What to change to make it better, most important first.</summary>
    public IReadOnlyList<string> Advice { get; init; } = Array.Empty<string>();

    /// <summary>Where the barcode is in the picture (its corners or the scanned line), for drawing on it.</summary>
    public IReadOnlyList<(double X, double Y)> Outline { get; init; } = Array.Empty<(double, double)>();

    /// <summary>The size of one module (the narrowest bar) in the picture, in pixels.</summary>
    public double ModulePixels { get; init; }

    public static BarcodeQualityReport NotFound { get; } = new()
    {
        Found = false,
        Advice = new[]
        {
            "No barcode was found. Hold the label flat and fill most of the picture with the barcode, in even light without glare.",
        },
    };
}

/// <summary>
/// Checks how well a barcode in a picture will scan, the way a barcode verifier does — ISO/IEC
/// 15416 for barcodes with bars (ten scan lines across the bars; contrast, edge contrast,
/// modulation, defects, decodability, quiet zones) and the main parts of ISO/IEC 15415 for
/// square codes (contrast, modulation, damaged modules, axial non-uniformity). A picture from
/// a webcam or a phone is not a calibrated verifier: the grade is a close guide, not a
/// certificate.
/// </summary>
public static class BarcodeGrader
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

    // ---- barcodes with bars (ISO/IEC 15416) ----------------------------------------------------

    private sealed record Line(
        QualityGrade Grade, double Sc, double Rmin, double Rmax, double EcMin, double Mod, double Defects, bool Decoded,
        double? Decodability, double QuietLeft, double QuietRight, bool QuietCut, double Module, double BarGain);

    private static BarcodeQualityReport Grade1D(GrayImage image, Result result)
    {
        (BarcodeSymbology? kind, string name) = Describe(result);
        ResultPoint[] points = result.ResultPoints ?? Array.Empty<ResultPoint>();
        if (points.Length < 2)
        {
            return new BarcodeQualityReport { Found = true, Symbology = name, Kind = kind, Text = result.Text ?? string.Empty, Grade = QualityGrade.F };
        }

        (double X, double Y) p0 = (points[0].X, points[0].Y), p1 = (points[^1].X, points[^1].Y);
        double length = Math.Sqrt(Math.Pow(p1.X - p0.X, 2) + Math.Pow(p1.Y - p0.Y, 2));
        if (length < 8)
        {
            return new BarcodeQualityReport { Found = true, Symbology = name, Kind = kind, Text = result.Text ?? string.Empty, Grade = QualityGrade.F };
        }

        (double dx, double dy) = ((p1.X - p0.X) / length, (p1.Y - p0.Y) / length);
        (double nx, double ny) = (-dy, dx);
        double reach = (0.3 * length) + 12;

        // How far the bars go across the line, to place ten scans over the middle 80% of their height.
        double[] centre = Profile(image, p0, dx, dy, nx, ny, 0, -reach, length + reach);
        (double lo, double hi) = Extremes(centre);
        double gt = (lo + hi) / 2;
        int inner0 = (int)(reach / Step), inner1 = (int)((reach + length) / Step);
        bool[] reference = Binarise(centre, gt);
        double top = 0, bottom = 0;
        for (double s = 1; s < length * 2; s++)
        {
            if (Agreement(Binarise(Profile(image, p0, dx, dy, nx, ny, -s, -reach, length + reach), gt), reference, inner0, inner1) < 0.88)
            {
                break;
            }

            top = -s;
        }

        for (double s = 1; s < length * 2; s++)
        {
            if (Agreement(Binarise(Profile(image, p0, dx, dy, nx, ny, s, -reach, length + reach), gt), reference, inner0, inner1) < 0.88)
            {
                break;
            }

            bottom = s;
        }

        var reader = new MultiFormatOneDReader(new Dictionary<DecodeHintType, object>
        {
            [DecodeHintType.POSSIBLE_FORMATS] = new List<BarcodeFormat> { result.BarcodeFormat },
        });

        var lines = new List<Line>();
        double height = bottom - top;
        for (int i = 0; i < 10; i++)
        {
            double offset = height < 4 ? 0 : top + (height * (0.1 + (0.8 * i / 9.0)));
            double[] profile = Profile(image, p0, dx, dy, nx, ny, offset, -reach, length + reach);
            if (Scan(profile, inner0, inner1, result, reader) is { } line)
            {
                lines.Add(line);
            }
        }

        if (lines.Count == 0)
        {
            return new BarcodeQualityReport { Found = true, Symbology = name, Kind = kind, Text = result.Text ?? string.Empty, Grade = QualityGrade.F };
        }

        double score = lines.Average(l => (int)l.Grade);
        QualityGrade Avg(Func<Line, QualityGrade> g) => FromScore(lines.Average(l => (int)g(l)));
        double module = lines.Average(l => l.Module) * Step;
        var parameters = new List<QualityParameter>
        {
            new("Decode", $"{lines.Count(l => l.Decoded)} of {lines.Count} scan lines read", lines.All(l => l.Decoded) ? QualityGrade.A : Avg(l => l.Decoded ? QualityGrade.A : QualityGrade.F),
                "Every line across the bars should read the same."),
            new("Symbol contrast", Pct(lines.Average(l => l.Sc)), Avg(l => Band(l.Sc, 0.70, 0.55, 0.40, 0.20)),
                "The difference between the lightest space and the darkest bar."),
            new("Darkest bar", Pct(lines.Average(l => l.Rmin)), Avg(l => l.Rmin <= 0.5 * l.Rmax ? QualityGrade.A : QualityGrade.F),
                "Bars must be at most half as light as the spaces."),
            new("Edge contrast", Pct(lines.Average(l => l.EcMin)), Avg(l => l.EcMin >= 0.15 ? QualityGrade.A : QualityGrade.F),
                "The weakest step between a bar and the space beside it."),
            new("Modulation", Pct(lines.Average(l => l.Mod)), Avg(l => Band(l.Mod, 0.70, 0.60, 0.50, 0.40)),
                "How evenly dark the bars and light the spaces are: blur and narrow spaces lower it."),
            new("Defects", Pct(lines.Average(l => l.Defects)), Avg(l => BandLow(l.Defects, 0.15, 0.20, 0.25, 0.30)),
                "Spots in the spaces and voids in the bars."),
        };

        if (lines.Any(l => l.Decodability is not null))
        {
            parameters.Add(new QualityParameter("Decodability", Pct(lines.Where(l => l.Decodability is not null).Average(l => l.Decodability!.Value)),
                Avg(l => l.Decodability is { } v ? Band(v, 0.62, 0.50, 0.37, 0.25) : QualityGrade.A),
                "How close the bar and space widths are to what they should be."));
        }

        double needed = QuietNeeded(kind);
        double qLeft = lines.Average(l => l.QuietLeft), qRight = lines.Average(l => l.QuietRight);
        bool cut = lines.Any(l => l.QuietCut);
        parameters.Add(new QualityParameter("Quiet zones", cut
                ? $"{qLeft:0} and {qRight:0} modules" + (qLeft < needed || qRight < needed ? " (the picture ends: show more space around the barcode)" : string.Empty)
                : $"{qLeft:0} and {qRight:0} modules, {needed:0} needed",
            Avg(l => l.QuietCut || (l.QuietLeft >= needed && l.QuietRight >= needed) ? QualityGrade.A : QualityGrade.F),
            "The blank space on each side of the bars."));

        double gain = lines.Average(l => l.BarGain);
        parameters.Add(new QualityParameter("Bar width", gain switch
        {
            > 0.005 => $"{Pct(gain)} of a module too wide",
            < -0.005 => $"{Pct(-gain)} of a module too narrow",
            _ => "as it should be",
        }, null, "Ink spread makes bars wider; too little heat or ink makes them narrower."));
        parameters.Add(new QualityParameter("Module size in the picture", $"{module:0.0} pixels", null,
            "At least 3 pixels per narrow bar is needed for a fair check."));

        return new BarcodeQualityReport
        {
            Found = true,
            Symbology = name,
            Kind = kind,
            Text = result.Text ?? string.Empty,
            Score = Math.Round(score, 1),
            Grade = FromScore(score),
            Parameters = parameters,
            Advice = Advice1D(lines, gain, needed, module, cut),
            Outline = new[] { p0, p1 },
            ModulePixels = module,
        };
    }

    private static double QuietNeeded(BarcodeSymbology? kind) => kind switch
    {
        BarcodeSymbology.Ean13 or BarcodeSymbology.UpcA => 9,
        BarcodeSymbology.Ean8 or BarcodeSymbology.UpcE => 7,
        _ => 10,
    };

    /// <summary>Reflectances along a line: from <paramref name="from"/> to <paramref name="to"/> pixels past p0, offset across it.</summary>
    private static double[] Profile(GrayImage image, (double X, double Y) p0, double dx, double dy, double nx, double ny, double offset,
        double from, double to)
    {
        int count = (int)((to - from) / Step);
        var values = new double[count];
        for (int k = 0; k < count; k++)
        {
            double t = from + (k * Step);
            double x = p0.X + (dx * t) + (nx * offset), y = p0.Y + (dy * t) + (ny * offset);
            values[k] = image.Contains(x, y) ? image.At(x, y) : double.NaN;
        }

        return values;
    }

    private static (double Min, double Max) Extremes(double[] values)
    {
        double min = 1, max = 0;
        foreach (double v in values)
        {
            if (!double.IsNaN(v))
            {
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
        }

        return (min, max);
    }

    private static bool[] Binarise(double[] values, double threshold) => values.Select(v => !double.IsNaN(v) && v < threshold).ToArray();

    private static double Agreement(bool[] a, bool[] b, int from, int to)
    {
        int same = 0, total = 0;
        for (int i = Math.Max(0, from); i < Math.Min(Math.Min(a.Length, b.Length), to); i++)
        {
            total++;
            same += a[i] == b[i] ? 1 : 0;
        }

        return total == 0 ? 0 : same / (double)total;
    }

    /// <summary>A run of samples on one side of the threshold.</summary>
    private readonly record struct Run(int Start, int Length, bool Dark);

    private static List<Run> Runs(double[] values, double threshold)
    {
        var runs = new List<Run>();
        int start = 0;
        for (int i = 1; i <= values.Length; i++)
        {
            bool dark = values[start] < threshold;
            if (i == values.Length || (values[i] < threshold) != dark || double.IsNaN(values[i]) != double.IsNaN(values[start]))
            {
                runs.Add(new Run(start, i - start, dark && !double.IsNaN(values[start])));
                start = i;
            }
        }

        return runs;
    }

    private static Line? Scan(double[] profile, int inner0, int inner1, Result result, MultiFormatOneDReader reader)
    {
        // The symbol: from the first bar to the last, found by walking out from the decoded
        // ends until a light stretch far longer than any element (the quiet zone) begins.
        (double lo, double hi) = Extremes(profile.Skip(inner0).Take(Math.Max(1, inner1 - inner0)).ToArray());
        double gt = (lo + hi) / 2;
        List<Run> runs = Runs(profile, gt);
        int first = -1, last = -1;
        for (int pass = 0; pass < 2; pass++)
        {
            List<Run> inner = runs.Where(r => r.Start + r.Length > inner0 && r.Start < inner1).ToList();
            if (inner.Count < 3)
            {
                return null;
            }

            // One module, from the narrowest bar-and-space pair (ink spread does not change a pair); no
            // element is wider than 4 modules, so a light stretch of more than 5.5 is the quiet zone.
            double unit = Enumerable.Range(0, inner.Count - 1).Select(i => (inner[i].Length + inner[i + 1].Length) / 2.0).OrderBy(l => l)
                .ElementAt(Math.Min(inner.Count - 2, (inner.Count - 1) / 10));
            double quiet = Math.Max(unit * 5.5, 6);
            int a = runs.FindIndex(r => r.Start + r.Length > inner0);
            int b = runs.FindLastIndex(r => r.Start < inner1);
            while (a > 0 && !(runs[a - 1] is { Dark: false } q && q.Length > quiet))
            {
                a--;
            }

            while (b < runs.Count - 1 && !(runs[b + 1] is { Dark: false } q && q.Length > quiet))
            {
                b++;
            }

            first = Enumerable.Range(a, b - a + 1).FirstOrDefault(i => runs[i].Dark, -1);
            last = Enumerable.Range(a, b - a + 1).LastOrDefault(i => runs[i].Dark, -1);
            if (first < 0 || last <= first)
            {
                return null;
            }

            // Reflectances over the symbol and its quiet zones set the threshold for the second pass.
            int from = runs[first].Start - (first > 0 ? Math.Min(runs[first - 1].Length, (int)(quiet * 2)) : 0);
            int to = runs[last].Start + runs[last].Length + (last < runs.Count - 1 ? Math.Min(runs[last + 1].Length, (int)(quiet * 2)) : 0);
            (lo, hi) = Extremes(profile[Math.Max(0, from)..Math.Min(profile.Length, to)]);
            gt = (lo + hi) / 2;
            if (pass == 0)
            {
                runs = Runs(profile, gt);
            }
        }

        double rmin = lo, rmax = hi, sc = rmax - rmin;
        if (sc <= 0.01)
        {
            return null;
        }

        // Elements of the symbol, with the quiet zones as its outer spaces.
        Run? leftQuiet = first > 0 && !runs[first - 1].Dark ? runs[first - 1] : null;
        Run? rightQuiet = last < runs.Count - 1 && !runs[last + 1].Dark ? runs[last + 1] : null;
        List<Run> elements = runs.Skip(first).Take(last - first + 1).ToList();
        var withQuiet = new List<Run>();
        if (leftQuiet is { } lq)
        {
            withQuiet.Add(lq);
        }

        withQuiet.AddRange(elements);
        if (rightQuiet is { } rq)
        {
            withQuiet.Add(rq);
        }

        double Extreme(Run r) => r.Dark
            ? profile.Skip(r.Start).Take(r.Length).Where(v => !double.IsNaN(v)).DefaultIfEmpty(rmin).Min()
            : profile.Skip(r.Start).Take(Math.Min(r.Length, 40)).Where(v => !double.IsNaN(v)).DefaultIfEmpty(rmax).Max();

        double ecMin = 1;
        for (int i = 0; i + 1 < withQuiet.Count; i++)
        {
            double space = withQuiet[i].Dark ? Extreme(withQuiet[i + 1]) : Extreme(withQuiet[i]);
            double bar = withQuiet[i].Dark ? Extreme(withQuiet[i]) : Extreme(withQuiet[i + 1]);
            ecMin = Math.Min(ecMin, space - bar);
        }

        // Element reflectance non-uniformity: the highest bump inside a bar (or deepest dip
        // inside a space) that has lower (higher) values on both sides. The slope down from an
        // edge is not a bump, so soft edges do not count as defects.
        double ernMax = 0;
        foreach (Run r in elements)
        {
            if (r.Length < 3)
            {
                continue;
            }

            double[] s = profile.Skip(r.Start).Take(r.Length).Select(v => double.IsNaN(v) ? gt : v).ToArray();
            double[] fromLeft = new double[s.Length], fromRight = new double[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                fromLeft[i] = i == 0 ? s[0] : r.Dark ? Math.Min(fromLeft[i - 1], s[i]) : Math.Max(fromLeft[i - 1], s[i]);
            }

            for (int i = s.Length - 1; i >= 0; i--)
            {
                fromRight[i] = i == s.Length - 1 ? s[i] : r.Dark ? Math.Min(fromRight[i + 1], s[i]) : Math.Max(fromRight[i + 1], s[i]);
            }

            for (int i = 1; i < s.Length - 1; i++)
            {
                double bump = r.Dark
                    ? s[i] - Math.Max(fromLeft[i], fromRight[i])
                    : Math.Min(fromLeft[i], fromRight[i]) - s[i];
                ernMax = Math.Max(ernMax, bump);
            }
        }

        // Edge positions to a fraction of a sample, for the widths.
        double Edge(int at)
        {
            double a = profile[Math.Max(0, at - 1)], b = profile[Math.Min(profile.Length - 1, at)];
            return Math.Abs(b - a) < 1e-9 ? at : at - 1 + ((gt - a) / (b - a));
        }

        var widths = new List<(double Width, bool Dark)>();
        for (int i = 0; i < elements.Count; i++)
        {
            double start = Edge(elements[i].Start), end = Edge(elements[i].Start + elements[i].Length);
            widths.Add((end - start, elements[i].Dark));
        }

        bool wholeModules = result.BarcodeFormat is BarcodeFormat.CODE_128 or BarcodeFormat.CODE_93 or BarcodeFormat.EAN_13 or BarcodeFormat.EAN_8
            or BarcodeFormat.UPC_A or BarcodeFormat.UPC_E;
        (double? decodability, double module, double gain) = Widths(widths, wholeModules);

        double quietLeft = leftQuiet is { } l ? l.Length / module : 0, quietRight = rightQuiet is { } r2 ? r2.Length / module : 0;
        bool cut = (leftQuiet is { } l2 && l2.Start == 0) || (rightQuiet is { } r3 && r3.Start + r3.Length >= profile.Length) ||
                   (leftQuiet is { } l3 && double.IsNaN(profile[Math.Max(0, l3.Start - 1)])) || first == 0 || last == runs.Count - 1;

        // Read the line on its own, at whole pixels.
        int pixels = profile.Length / 2;
        var bits = new BitArray(pixels);
        for (int x = 0; x < pixels; x++)
        {
            double v = (Value(profile, 2 * x, rmax) + Value(profile, (2 * x) + 1, rmax)) / 2;
            if (v < gt)
            {
                bits[x] = true;
            }
        }

        bool decoded;
        try
        {
            decoded = reader.decodeRow(0, bits, null)?.Text == result.Text;
            if (!decoded)
            {
                bits.reverse();
                decoded = reader.decodeRow(0, bits, null)?.Text == result.Text;
            }
        }
        catch (Exception ex) when (ex is ReaderException or ArgumentException or IndexOutOfRangeException)
        {
            decoded = false;
        }

        double mod = ecMin / sc, defects = ernMax / sc;
        QualityGrade grade = new[]
        {
            Band(sc, 0.70, 0.55, 0.40, 0.20),
            rmin <= 0.5 * rmax ? QualityGrade.A : QualityGrade.F,
            ecMin >= 0.15 ? QualityGrade.A : QualityGrade.F,
            Band(mod, 0.70, 0.60, 0.50, 0.40),
            BandLow(defects, 0.15, 0.20, 0.25, 0.30),
            decoded ? QualityGrade.A : QualityGrade.F,
            decodability is { } v2 ? Band(v2, 0.62, 0.50, 0.37, 0.25) : QualityGrade.A,
            cut || (quietLeft >= QuietNeeded(Map(result.BarcodeFormat, false)) && quietRight >= QuietNeeded(Map(result.BarcodeFormat, false)))
                ? QualityGrade.A : QualityGrade.F,
        }.Min();

        return new Line(grade, sc, rmin, rmax, Math.Max(0, ecMin), Math.Max(0, mod), defects, decoded, decodability,
            quietLeft, quietRight, cut, module, gain);
    }

    private static double Value(double[] profile, int i, double fallback) => i < profile.Length && !double.IsNaN(profile[i]) ? profile[i] : fallback;

    /// <summary>The module size, the decodability (for symbologies of whole-module widths) and the bar width gain.</summary>
    private static (double? Decodability, double Module, double BarGain) Widths(List<(double Width, bool Dark)> widths, bool wholeModules)
    {
        // Leading edge to the next leading edge (a bar and its space, or a space and its bar):
        // ink spread widens bars and narrows spaces by the same amount, so these pairs keep
        // their true widths — the way verifiers measure (n, k) symbologies.
        var pairs = new List<double>();
        for (int i = 0; i + 1 < widths.Count; i++)
        {
            pairs.Add(widths[i].Width + widths[i + 1].Width);
        }

        if (pairs.Count == 0)
        {
            return (null, Math.Max(0.5, widths.Sum(w => w.Width)), 0);
        }

        double module = Math.Max(0.5, pairs.Min() / 2);
        for (int i = 0; i < 6; i++)
        {
            double modules = pairs.Sum(p => Math.Max(2, Math.Round(p / module)));
            module = pairs.Sum() / modules;
        }

        // How much wider the bars are than they should be (spaces narrower by the same).
        double gain = 0;
        for (int pass = 0; pass < 3; pass++)
        {
            double barExcess = 0, spaceExcess = 0;
            int bars = 0, spaces = 0;
            foreach ((double width, bool dark) in widths)
            {
                double m = width / module;
                double ideal = Math.Max(1, Math.Round(dark ? m - gain : m + gain));
                if (dark)
                {
                    barExcess += m - ideal;
                    bars++;
                }
                else
                {
                    spaceExcess += m - ideal;
                    spaces++;
                }
            }

            gain = bars > 0 && spaces > 0 ? ((barExcess / bars) - (spaceExcess / spaces)) / 2 : 0;
        }

        // Decodability: the worst pair's distance from a whole number of modules (half a module off misreads).
        double? decodability = null;
        if (wholeModules)
        {
            double worst = pairs.Max(p => Math.Abs((p / module) - Math.Round(p / module)));
            decodability = Math.Clamp(1 - (2 * worst), 0, 1);
        }

        return (decodability, module, gain);
    }

    private static IReadOnlyList<string> Advice1D(List<Line> lines, double gain, double quietNeeded, double module, bool cut)
    {
        var advice = new List<string>();
        if (module < 3)
        {
            advice.Add("The bars are small in the picture: bring the camera closer or scan at a higher resolution, then check again.");
        }

        if (lines.Average(l => l.Sc) < 0.55 || lines.Any(l => l.Rmin > 0.5 * l.Rmax))
        {
            advice.Add("Low contrast: print darker (more heat or ink), use white labels, and check the picture is evenly lit.");
        }

        if (gain > 0.15)
        {
            advice.Add("The bars print too wide (ink spread): lower the printer's darkness or heat, or turn on its bar width reduction.");
        }
        else if (gain < -0.15)
        {
            advice.Add("The bars print too thin: raise the printer's darkness or heat.");
        }

        if (lines.Average(l => l.Mod) < 0.6)
        {
            advice.Add("Bars and spaces are not cleanly separated (blur or uneven print): clean the print head, print slower, or make the barcode bigger.");
        }

        if (lines.Average(l => l.Defects) > 0.15 || lines.Max(l => l.Defects) > 0.20)
        {
            advice.Add("Spots or gaps in the bars: clean the print head and check the label for dust or damage.");
        }

        if (!cut && lines.Any(l => l.QuietLeft < quietNeeded || l.QuietRight < quietNeeded))
        {
            advice.Add($"Not enough blank space beside the bars: keep at least {quietNeeded:0} modules clear on each side.");
        }

        if (lines.Any(l => !l.Decoded))
        {
            advice.Add("Some lines across the bars do not read: part of the barcode may be damaged, smudged or too short.");
        }

        if (advice.Count == 0)
        {
            advice.Add("This barcode prints well.");
        }

        return advice;
    }

    // ---- square codes (ISO/IEC 15415, the main parameters) ----------------------------------------

    private static BarcodeQualityReport Grade2D(GrayImage image, Result result)
    {
        (BarcodeSymbology? kind, string name) = Describe(result);
        var points = (result.ResultPoints ?? Array.Empty<ResultPoint>()).Where(p => p is not null).Select(p => (X: (double)p.X, Y: (double)p.Y)).ToList();
        (List<(double X, double Y, bool Dark)> modules, double pitchX, double pitchY, List<(double X, double Y)> outline) =
            SampleGrid(image, result) ?? (new(), 0, 0, points);

        var parameters = new List<QualityParameter> { new("Decode", "reads", QualityGrade.A, "The code reads.") };
        var advice = new List<string>();
        var grades = new List<QualityGrade> { QualityGrade.A };
        double modulePx = (pitchX + pitchY) / 2;

        if (modules.Count >= 16)
        {
            // Reflectance at each module's centre, over a spot the size of a fifth of a module.
            double radius = Math.Max(0.5, modulePx * 0.2);
            double[] r = modules.Select(m => Spot(image, m.X, m.Y, radius)).ToArray();
            double[] sorted = r.OrderBy(v => v).ToArray();
            double rmin = sorted[(int)(sorted.Length * 0.02)], rmax = sorted[(int)(sorted.Length * 0.98)];
            double sc = rmax - rmin, gt = (rmax + rmin) / 2;

            QualityGrade scGrade = Band(sc, 0.70, 0.55, 0.40, 0.20);
            parameters.Add(new QualityParameter("Symbol contrast", Pct(sc), scGrade, "The difference between light and dark modules."));
            grades.Add(scGrade);

            // Modulation: how far each module is from the threshold, on the side it should be.
            var mods = new List<double>();
            int wrong = 0;
            for (int i = 0; i < r.Length; i++)
            {
                bool looksDark = r[i] < gt;
                if (looksDark != modules[i].Dark)
                {
                    wrong++;
                }

                mods.Add(sc <= 0 ? 0 : 2 * Math.Abs(r[i] - gt) / sc * (looksDark == modules[i].Dark ? 1 : -1));
            }

            double mod5 = mods.OrderBy(v => v).ElementAt((int)(mods.Count * 0.05));
            QualityGrade modGrade = Band(mod5, 0.50, 0.40, 0.30, 0.20);
            parameters.Add(new QualityParameter("Modulation", Pct(Math.Max(0, mod5)), modGrade,
                "How clearly the weakest modules are light or dark: blur and ink spread lower it."));
            grades.Add(modGrade);

            double damaged = wrong / (double)r.Length;
            QualityGrade damageGrade = BandLow(damaged, 0.0, 0.01, 0.03, 0.06);
            parameters.Add(new QualityParameter("Damaged modules", Pct(damaged), damageGrade,
                "Modules that look the wrong colour; error correction repairs a few."));
            grades.Add(damageGrade);

            double an = Math.Abs(pitchX - pitchY) / ((pitchX + pitchY) / 2);
            QualityGrade anGrade = BandLow(an, 0.06, 0.08, 0.10, 0.12);
            parameters.Add(new QualityParameter("Axial non-uniformity", Pct(an), anGrade,
                "Whether modules are as tall as they are wide (a stretched print or a slanted picture raises it)."));
            grades.Add(anGrade);

            if (sc < 0.55)
            {
                advice.Add("Low contrast: print darker, use white labels, and check the picture is evenly lit.");
            }

            if (mod5 < 0.40 || damaged > 0.01)
            {
                advice.Add("Some modules are unclear: print slower or bigger, clean the print head, or lower the darkness if dark modules run together.");
            }

            if (an > 0.08)
            {
                advice.Add("The code looks stretched: hold the camera straight above it; if it still does, check the printer's speed and the label feed.");
            }
        }

        parameters.Add(new QualityParameter("Module size in the picture", $"{modulePx:0.0} pixels", null,
            "At least 4 pixels per module is needed for a fair check."));
        if (modulePx is > 0 and < 4)
        {
            advice.Insert(0, "The code is small in the picture: bring the camera closer or scan at a higher resolution, then check again.");
        }

        if (advice.Count == 0)
        {
            advice.Add("This code prints well.");
        }

        QualityGrade grade = grades.Min();
        return new BarcodeQualityReport
        {
            Found = true,
            Symbology = name,
            Kind = kind,
            Text = (result.Text ?? string.Empty).TrimStart('\u001d'),
            Grade = grade,
            Score = (int)grade,
            Parameters = parameters,
            Advice = advice,
            Outline = outline,
            ModulePixels = modulePx,
        };
    }

    private static double Spot(GrayImage image, double x, double y, double radius)
    {
        double sum = 0;
        int n = 0;
        for (double oy = -radius; oy <= radius + 1e-9; oy += Math.Max(0.5, radius / 2))
        {
            for (double ox = -radius; ox <= radius + 1e-9; ox += Math.Max(0.5, radius / 2))
            {
                if ((ox * ox) + (oy * oy) <= (radius * radius) + 1e-9)
                {
                    sum += image.At(x + ox, y + oy);
                    n++;
                }
            }
        }

        return n == 0 ? image.At(x, y) : sum / n;
    }

    /// <summary>
    /// The module grid of a QR code or DataMatrix: where each module's centre is in the picture
    /// and whether it should be dark, from ZXing's detector; and the module pitch across and down.
    /// </summary>
    private static (List<(double X, double Y, bool Dark)> Modules, double PitchX, double PitchY, List<(double X, double Y)> Outline)? SampleGrid(
        GrayImage image, Result result)
    {
        var source = new PlanarYUVLuminanceSource(image.Pixels, image.Width, image.Height, 0, 0, image.Width, image.Height, false);
        BitMatrix black = new BinaryBitmap(new HybridBinarizer(source)).BlackMatrix;
        try
        {
            if (result.BarcodeFormat == BarcodeFormat.QR_CODE &&
                new ZXing.QrCode.Internal.Detector(black).detect(null) is { } qr && qr.Points.Length >= 3)
            {
                int dim = qr.Bits.Width;
                (double X, double Y) bl = (qr.Points[0].X, qr.Points[0].Y), tl = (qr.Points[1].X, qr.Points[1].Y), tr = (qr.Points[2].X, qr.Points[2].Y);
                double span = dim - 7;
                (double X, double Y) At(double u, double v) =>
                    (tl.X + ((u - 3.5) / span * (tr.X - tl.X)) + ((v - 3.5) / span * (bl.X - tl.X)),
                     tl.Y + ((u - 3.5) / span * (tr.Y - tl.Y)) + ((v - 3.5) / span * (bl.Y - tl.Y)));
                var modules = new List<(double, double, bool)>();
                for (int y = 0; y < dim; y++)
                {
                    for (int x = 0; x < dim; x++)
                    {
                        (double px, double py) = At(x + 0.5, y + 0.5);
                        modules.Add((px, py, qr.Bits[x, y]));
                    }
                }

                double pitchX = Distance(tl, tr) / span, pitchY = Distance(tl, bl) / span;
                return (modules, pitchX, pitchY, new List<(double, double)> { At(0, 0), At(dim, 0), At(dim, dim), At(0, dim) });
            }

            if (result.BarcodeFormat == BarcodeFormat.DATA_MATRIX &&
                new ZXing.Datamatrix.Internal.Detector(black).detect() is { } dm && dm.Points.Length >= 4)
            {
                int w = dm.Bits.Width, h = dm.Bits.Height;
                // Corners as the detector gives them: top-left, bottom-left, bottom-right, top-right.
                (double X, double Y) tl = (dm.Points[0].X, dm.Points[0].Y), bl = (dm.Points[1].X, dm.Points[1].Y);
                (double X, double Y) br = (dm.Points[2].X, dm.Points[2].Y), tr = (dm.Points[3].X, dm.Points[3].Y);
                (double X, double Y) At(double u, double v)
                {
                    double fu = u / w, fv = v / h;
                    double topX = tl.X + ((tr.X - tl.X) * fu), topY = tl.Y + ((tr.Y - tl.Y) * fu);
                    double botX = bl.X + ((br.X - bl.X) * fu), botY = bl.Y + ((br.Y - bl.Y) * fu);
                    return (topX + ((botX - topX) * fv), topY + ((botY - topY) * fv));
                }

                var modules = new List<(double, double, bool)>();
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        (double px, double py) = At(x + 0.5, y + 0.5);
                        modules.Add((px, py, dm.Bits[x, y]));
                    }
                }

                return (modules, Distance(tl, tr) / w, Distance(tl, bl) / h, new List<(double, double)> { tl, tr, br, bl });
            }
        }
        catch (Exception ex) when (ex is ReaderException or ArgumentException or IndexOutOfRangeException)
        {
            // The detector did not find the grid: only the reading is graded.
        }

        return null;
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
}
