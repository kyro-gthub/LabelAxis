using LabelMaker.Core.Units;

namespace LabelMaker.Core.Barcodes;

/// <summary>A fully positioned barcode ready for a drawing surface to fill as rectangles.</summary>
public sealed class PlacedBarcode
{
    public required BarcodeSymbol Symbol { get; init; }

    /// <summary>Dark rectangles in millimetres, relative to the object's top-left (rotation not applied).</summary>
    public required IReadOnlyList<RectMm> DarkRects { get; init; }

    /// <summary>Background rectangle (whole symbol incl. quiet zone) in millimetres.</summary>
    public required RectMm SymbolRect { get; init; }

    /// <summary>Human-readable text box in millimetres, or null when there is none.</summary>
    public RectMm? HriRect { get; init; }

    public string HriText { get; init; } = string.Empty;

    /// <summary>Module size actually used (mm). For 1D this is the narrow-bar width.</summary>
    public double ModuleSizeMm { get; init; }

    /// <summary>Natural (unconstrained) size of the whole object incl. HRI, in millimetres.</summary>
    public required SizeMm NaturalSizeMm { get; init; }
}

/// <summary>
/// Places a <see cref="BarcodeSymbol"/> into physical space. Two modes:
/// <c>Natural</c> keeps the requested module width and reports the resulting size;
/// <c>Fit</c> scales the symbol to a target rectangle (module width becomes a
/// minimum that, if violated, is surfaced by the caller / preflight).
/// </summary>
public static class BarcodeLayout
{
    public static SizeMm NaturalSize(BarcodeSymbol symbol, BarcodeSpec spec)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(spec);
        double hri = HriHeightMm(symbol, spec);

        if (symbol.Is2D)
        {
            int cells = Math.Max(symbol.MatrixColumns, symbol.MatrixRows) + (2 * symbol.QuietZoneModules);
            double side = cells * spec.ModuleWidthMm;
            return new SizeMm(side, side + hri);
        }

        double width = symbol.TotalWidthModules * spec.ModuleWidthMm;
        return new SizeMm(width, spec.BarHeightMm + hri);
    }

    public static PlacedBarcode PlaceNatural(BarcodeSymbol symbol, BarcodeSpec spec)
    {
        SizeMm natural = NaturalSize(symbol, spec);
        return Build(symbol, spec, new RectMm(0, 0, natural.Width, natural.Height), spec.ModuleWidthMm);
    }

    public static PlacedBarcode PlaceFit(BarcodeSymbol symbol, BarcodeSpec spec, SizeMm target)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        double hri = HriHeightMm(symbol, spec);
        double usableHeight = Math.Max(0.1, target.Height - hri);

        double moduleFromWidth, moduleFromHeight;
        if (symbol.Is2D)
        {
            int cols = symbol.MatrixColumns + (2 * symbol.QuietZoneModules);
            int rows = symbol.MatrixRows + (2 * symbol.QuietZoneModules);
            moduleFromWidth = target.Width / cols;
            moduleFromHeight = usableHeight / rows;
        }
        else
        {
            moduleFromWidth = target.Width / Math.Max(1, symbol.TotalWidthModules);
            moduleFromHeight = double.MaxValue; // bar height is independent for 1D
        }

        double module = Math.Min(moduleFromWidth, moduleFromHeight);
        if (module <= 0)
        {
            module = spec.ModuleWidthMm;
        }

        return Build(symbol, spec, new RectMm(0, 0, target.Width, target.Height), module, fitHeight: usableHeight);
    }

    private static PlacedBarcode Build(
        BarcodeSymbol symbol, BarcodeSpec spec, RectMm objectRect, double moduleMm, double? fitHeight = null)
    {
        double hriHeight = HriHeightMm(symbol, spec);
        var darkRects = new List<RectMm>();

        RectMm symbolRect;
        if (symbol.Is2D)
        {
            int q = symbol.QuietZoneModules;
            double side = Math.Min(
                objectRect.Width,
                (fitHeight ?? (objectRect.Height - hriHeight)));
            double cellsAcross = symbol.MatrixColumns + (2 * q);
            double cell = side / cellsAcross;
            double originX = objectRect.X + ((objectRect.Width - (cell * cellsAcross)) / 2.0);
            double originY = spec.HumanReadable == HumanReadablePosition.Above
                ? objectRect.Y + hriHeight
                : objectRect.Y;

            symbolRect = new RectMm(originX, originY, cell * cellsAcross, cell * (symbol.MatrixRows + (2 * q)));

            for (int row = 0; row < symbol.MatrixRows; row++)
            {
                int runStart = -1;
                for (int col = 0; col <= symbol.MatrixColumns; col++)
                {
                    bool dark = col < symbol.MatrixColumns && symbol.Matrix![row, col];
                    if (dark && runStart < 0)
                    {
                        runStart = col;
                    }
                    else if (!dark && runStart >= 0)
                    {
                        double x = originX + ((q + runStart) * cell);
                        double y = originY + ((q + row) * cell);
                        darkRects.Add(new RectMm(x, y, (col - runStart) * cell, cell));
                        runStart = -1;
                    }
                }
            }

            moduleMm = cell;
        }
        else
        {
            double barHeight = fitHeight ?? spec.BarHeightMm;
            double totalWidth = symbol.TotalWidthModules * moduleMm;
            double originX = objectRect.X + ((objectRect.Width - totalWidth) / 2.0);
            double originY = spec.HumanReadable == HumanReadablePosition.Above
                ? objectRect.Y + hriHeight
                : objectRect.Y;

            symbolRect = new RectMm(originX, originY, totalWidth, barHeight);

            foreach (BarModule bar in symbol.Bars)
            {
                if (!bar.IsBar)
                {
                    continue;
                }

                darkRects.Add(new RectMm(
                    originX + (bar.StartModule * moduleMm),
                    originY,
                    bar.WidthModules * moduleMm,
                    barHeight));
            }
        }

        RectMm? hriRect = null;
        if (hriHeight > 0 && !string.IsNullOrEmpty(symbol.HumanReadableText))
        {
            double y = spec.HumanReadable == HumanReadablePosition.Above
                ? objectRect.Y
                : symbolRect.Bottom;
            hriRect = new RectMm(objectRect.X, y, objectRect.Width, hriHeight);
        }

        return new PlacedBarcode
        {
            Symbol = symbol,
            DarkRects = darkRects,
            SymbolRect = symbolRect,
            HriRect = hriRect,
            HriText = hriRect is null ? string.Empty : symbol.HumanReadableText,
            ModuleSizeMm = moduleMm,
            NaturalSizeMm = NaturalSize(symbol, spec),
        };
    }

    private static double HriHeightMm(BarcodeSymbol symbol, BarcodeSpec spec)
    {
        if (spec.HumanReadable == HumanReadablePosition.None ||
            symbol.Is2D ||
            string.IsNullOrEmpty(symbol.HumanReadableText))
        {
            return 0;
        }

        // Point size → mm, plus a small leading.
        return (spec.HumanReadableFontSizePt * 25.4 / 72.0 * 1.35) + 0.4;
    }
}
