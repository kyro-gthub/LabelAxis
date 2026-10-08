using System.Globalization;
using LabelMaker.Core.Barcodes;
using ZXing;
using ZXing.Common;
using ZXing.OneD;

namespace LabelMaker.Core.Verification;

/// <summary>Grading square codes (QR Code, Data Matrix and the rest) on the main parameters of ISO/IEC 15415.</summary>
public static partial class BarcodeGrader
{
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
