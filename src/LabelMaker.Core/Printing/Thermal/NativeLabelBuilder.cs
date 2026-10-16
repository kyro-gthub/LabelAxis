using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;

namespace LabelMaker.Core.Printing.Thermal;

/// <summary>
/// Turns one label of a job (a design and its record) into what the printer draws over the
/// stored background. Objects it cannot hand to the printer are listed in
/// <see cref="Result.NeedPictures"/> for the caller to send as pictures of their area.
/// </summary>
public static class NativeLabelBuilder
{
    /// <summary>Where a printer font's capitals sit: its cell top is this many font heights above the baseline.</summary>
    private const double CellAboveBaseline = 0.8;

    public sealed class Result
    {
        public required NativeLabel Label { get; init; }

        /// <summary>Objects to send as a picture on this label, each with the area (in label dots) the picture covers.</summary>
        public List<(LabelObject Object, DotRect Area)> NeedPictures { get; } = new();
    }

    public static Result Build(
        NativeDesign design,
        ResolveContext context,
        ThermalPrintSettings settings,
        ITextMeasurer measurer,
        INativeThermalEncoder encoder,
        int offsetX,
        int offsetY,
        int background)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(encoder);

        var result = new Result { Label = new NativeLabel { OffsetX = offsetX, OffsetY = offsetY, Background = background } };
        var resolver = new VariableResolver(context);

        foreach (LabelObject obj in design.Drawn)
        {
            List<NativeElement>? elements = obj switch
            {
                TextObject text => TextElements(text, resolver, settings, measurer),
                BarcodeObject barcode => BarcodeElements(barcode, resolver, settings, measurer),
                _ => null,
            };

            if (elements is not null && elements.All(encoder.CanCarry))
            {
                result.Label.Elements.AddRange(elements);
            }
            else
            {
                result.NeedPictures.Add((obj, Area(obj, settings)));
            }
        }

        foreach (LabelObject obj in design.Pictures)
        {
            result.NeedPictures.Add((obj, Area(obj, settings)));
        }

        return result;
    }

    /// <summary>The dots an object can cover, rounded outwards by a dot so its edges are never cut.</summary>
    public static DotRect Area(LabelObject obj, ThermalPrintSettings settings)
    {
        RectMm r = obj.RotatedBounds;
        int x = settings.Dots(r.Left) - 1, y = settings.Dots(r.Top) - 1;
        return new DotRect(x, y, settings.Dots(r.Right) + 1 - x, settings.Dots(r.Bottom) + 1 - y);
    }

    // ---- text ---------------------------------------------------------------------------

    private static List<NativeElement>? TextElements(TextObject text, VariableResolver resolver, ThermalPrintSettings settings, ITextMeasurer measurer)
    {
        RichTextContent resolved = text.Content.Clone();
        foreach (TextRun run in resolved.Paragraphs.SelectMany(p => p.Runs))
        {
            run.Text = resolver.Resolve(run.Text);
            if (!run.Text.All(IsPrinterFontChar))
            {
                return null;
            }
        }

        RectMm b = text.Bounds;
        var content = new RectMm(
            text.PaddingMm.Left,
            text.PaddingMm.Top,
            Math.Max(0.5, b.Width - text.PaddingMm.Horizontal),
            Math.Max(0.5, b.Height - text.PaddingMm.Vertical));
        TextBlock block = TextLayouter.Layout(resolved, text, content, measurer);
        NativeRotation rotation = NativeDesign.QuarterTurn(text.RotationDegrees) ?? NativeRotation.None;

        var elements = new List<NativeElement>();
        double y = content.Y + block.TopOffsetMm(text.VerticalAlignment, content.Height);
        foreach (TextLine line in block.Lines)
        {
            string words = string.Concat(line.Spans.Select(s => s.Text));
            if (line.RightToLeft)
            {
                return null;
            }

            if (words.Trim().Length > 0)
            {
                double sizePt = line.Spans.Max(s => s.FontSizePt);
                elements.Add(Line(text, words, sizePt, content.X, content.Width, y + line.BaselineOffsetMm, line.Alignment, rotation, settings));
            }

            y += line.HeightMm;
        }

        return elements;
    }

    /// <summary>One line of printer text whose baseline is at <paramref name="baselineMm"/> in the object's own (unturned) frame.</summary>
    private static NativeText Line(LabelObject obj, string words, double sizePt, double leftMm, double widthMm, double baselineMm,
        HorizontalAlignment alignment, NativeRotation rotation, ThermalPrintSettings settings)
    {
        double fontMm = sizePt * 25.4 / 72.0;
        var local = new RectMm(leftMm, baselineMm - (CellAboveBaseline * fontMm), widthMm, fontMm);
        return new NativeText(
            ToLabelDots(obj, local, settings),
            Math.Max(10, settings.Dots(fontMm)),
            alignment == HorizontalAlignment.Justify ? HorizontalAlignment.Left : alignment,
            rotation,
            words.Trim());
    }

    /// <summary>Letters a printer's built-in font has: printable ASCII and Western European (Latin-1).</summary>
    public static bool IsPrinterFontChar(char c) => c is (>= ' ' and <= '~') or (>= ' ' and <= 'ÿ');

    // ---- barcodes -----------------------------------------------------------------------

    private static List<NativeElement>? BarcodeElements(BarcodeObject barcode, VariableResolver resolver, ThermalPrintSettings settings, ITextMeasurer measurer)
    {
        BarcodeSpec spec = barcode.Spec;
        string value = resolver.Resolve(spec.Data);
        if (NativeDesign.SymbologyOf(spec) is not { } symbology ||
            !BarcodeEngine.TryEncode(spec, value, out BarcodeSymbol? symbol, out _) || symbol is null)
        {
            return null; // the picture shows what the screen shows for a value that does not encode
        }

        string? data = PrinterData(symbology, spec, value.Trim());
        if (data is null)
        {
            return null;
        }

        RectMm b = barcode.Bounds;
        PlacedBarcode placed = barcode.AutoSize
            ? BarcodeLayout.PlaceNatural(symbol, spec)
            : BarcodeLayout.PlaceFit(symbol, spec, b.Size);
        double dx = barcode.AutoSize ? (b.Width - placed.NaturalSizeMm.Width) / 2.0 : 0;
        double dy = barcode.AutoSize ? (b.Height - placed.NaturalSizeMm.Height) / 2.0 : 0;
        double module = placed.ModuleSizeMm;
        RectMm bars;
        int moduleDots;
        double ratio = 1;
        if (symbol.Is2D)
        {
            int q = symbol.QuietZoneModules;
            bars = new RectMm(dx + placed.SymbolRect.X + (q * module), dy + placed.SymbolRect.Y + (q * module),
                symbol.MatrixColumns * module, symbol.MatrixRows * module);
            moduleDots = Math.Max(1, settings.Dots(module));
        }
        else
        {
            BarModule[] runs = symbol.Bars.SkipWhile(r => !r.IsBar).Reverse().SkipWhile(r => !r.IsBar).Reverse().ToArray();
            if (runs.Length == 0)
            {
                return null;
            }

            int narrow = runs.Min(r => r.WidthModules), wide = runs.Max(r => r.WidthModules);
            ratio = (double)wide / narrow;
            int first = runs[0].StartModule, end = runs[^1].StartModule + runs[^1].WidthModules;
            bars = new RectMm(dx + placed.SymbolRect.X + (first * module), dy + placed.SymbolRect.Y, (end - first) * module, placed.SymbolRect.Height);
            moduleDots = Math.Max(1, settings.Dots(narrow * module));
        }

        NativeRotation rotation = NativeDesign.QuarterTurn(barcode.RotationDegrees) ?? NativeRotation.None;
        DotRect box = ToLabelDots(barcode, bars, settings);
        var elements = new List<NativeElement>
        {
            new NativeBarcode(
                box,
                rotation,
                symbology,
                data,
                moduleDots,
                Math.Max(1, settings.Dots(bars.Height)),
                ratio,
                spec.QrEcLevel,
                symbology == NativeSymbology.Code128 ? BarcodeEngine.Code128Codewords(spec, value) : null),
        };

        if (placed.HriRect is { } hri && placed.HriText.Length > 0)
        {
            if (!placed.HriText.All(IsPrinterFontChar))
            {
                return null;
            }

            // As on screen: long text under the bars is made smaller until it fits their width.
            double sizePt = spec.HumanReadableFontSizePt;
            double width = measurer.MeasureWidthMm(placed.HriText, spec.HumanReadableFontFamily, sizePt, false, false);
            if (width > hri.Width && width > 0)
            {
                sizePt = Math.Max(4, sizePt * hri.Width / width);
            }

            double fontMm = sizePt * 25.4 / 72.0;
            double baseline = dy + hri.Y + ((hri.Height + fontMm) / 2.0) - (0.2 * fontMm);
            elements.Add(Line(barcode, placed.HriText, sizePt, dx + hri.X, hri.Width, baseline, HorizontalAlignment.Center, rotation, settings));
        }

        return elements;
    }

    /// <summary>The value as the printer's barcode command takes it, or null when it has no way to take it.</summary>
    private static string? PrinterData(NativeSymbology symbology, BarcodeSpec spec, string value)
    {
        static bool Digits(string s) => s.Length > 0 && s.All(char.IsAsciiDigit);
        const string code39 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

        switch (symbology)
        {
            case NativeSymbology.Code128:
                return value;
            case NativeSymbology.Ean13:
                // The printer adds the check digit itself.
                return Digits(value) && value.Length is 12 or 13 ? value[..12] : null;
            case NativeSymbology.Ean8:
                return Digits(value) && value.Length is 7 or 8 ? value[..7] : null;
            case NativeSymbology.UpcA:
                return Digits(value) && value.Length is 11 or 12 ? value[..11] : null;
            case NativeSymbology.Code39:
            {
                string v = value.Trim('*');
                if (v.Length == 0 || !v.All(c => code39.Contains(c, StringComparison.Ordinal)))
                {
                    return null;
                }

                // A check character is worked out here, so both printer languages print the same thing.
                return spec.AddCheckDigit ? v + code39[v.Sum(c => code39.IndexOf(c, StringComparison.Ordinal)) % 43] : v;
            }

            case NativeSymbology.Code93:
                return value.Length > 0 && value.All(c => code39.Contains(c, StringComparison.Ordinal)) ? value : null;
            case NativeSymbology.Interleaved2of5:
                return Digits(value) && value.Length % 2 == 0 ? value : null;
            case NativeSymbology.Codabar:
            {
                string v = value.ToUpperInvariant();
                bool ends = v.Length >= 2 && "ABCD".Contains(v[0], StringComparison.Ordinal) && "ABCD".Contains(v[^1], StringComparison.Ordinal);
                string body = ends ? v[1..^1] : v;
                return body.Length > 0 && body.All(c => "0123456789-$:/.+".Contains(c, StringComparison.Ordinal))
                    ? (ends ? v : "A" + v + "A")
                    : null;
            }

            case NativeSymbology.QrCode:
            case NativeSymbology.DataMatrix:
                // Plain ASCII only: other characters are coded differently from printer to printer.
                return value.Length > 0 && value.All(c => c is >= ' ' and <= '~') ? value : null;
            default:
                return null;
        }
    }

    // ---- geometry -----------------------------------------------------------------------

    /// <summary>
    /// A rectangle in the object's own frame (millimetres from its unturned top-left) as dots on
    /// the label, after the object's quarter turn about its centre.
    /// </summary>
    internal static DotRect ToLabelDots(LabelObject obj, RectMm local, ThermalPrintSettings settings)
    {
        RectMm b = obj.Bounds;
        double cx = b.Width / 2.0, cy = b.Height / 2.0;
        NativeRotation rotation = NativeDesign.QuarterTurn(obj.RotationDegrees) ?? NativeRotation.None;

        (double X, double Y) Turn(double x, double y)
        {
            double rx = x - cx, ry = y - cy;
            (rx, ry) = rotation switch
            {
                NativeRotation.Quarter => (-ry, rx),
                NativeRotation.Half => (-rx, -ry),
                NativeRotation.ThreeQuarter => (ry, -rx),
                _ => (rx, ry),
            };
            return (b.X + cx + rx, b.Y + cy + ry);
        }

        (double X, double Y) a = Turn(local.Left, local.Top), c = Turn(local.Right, local.Bottom);
        double left = Math.Min(a.X, c.X), top = Math.Min(a.Y, c.Y), right = Math.Max(a.X, c.X), bottom = Math.Max(a.Y, c.Y);
        int x = settings.Dots(left), y = settings.Dots(top);
        return new DotRect(x, y, Math.Max(1, settings.Dots(right) - x), Math.Max(1, settings.Dots(bottom) - y));
    }
}
