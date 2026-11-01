using LabelMaker.Core.Formtec;

namespace LabelMaker.Data.Publisher;

/// <summary>
/// Publisher lays labels out one to a page and prints several pages on each sheet ("multiple
/// pages per sheet"). These find the label sheet such a page belongs on, so an imported file
/// prints many labels to a sheet as it did in Publisher, not one label alone on a small page.
/// </summary>
public static class PublisherSheet
{
    /// <summary>How far a sheet's label may differ from the Publisher page, in millimetres.</summary>
    public const double ToleranceMm = 0.6;

    /// <summary>
    /// A page that fits on an A4 sheet at least twice is a label, not a page to print alone.
    /// </summary>
    public static bool IsLabelPage(double widthMm, double heightMm)
    {
        if (widthMm <= 0 || heightMm <= 0)
        {
            return false;
        }

        double upright = Math.Floor(210 / widthMm) * Math.Floor(297 / heightMm);
        double turned = Math.Floor(297 / widthMm) * Math.Floor(210 / heightMm);
        return Math.Max(upright, turned) >= 2;
    }

    /// <summary>Label sheets (not rolls) whose labels are this size, the closest first.</summary>
    public static IReadOnlyList<LabelTemplate> Candidates(TemplateStore store, double widthMm, double heightMm)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.All
            .Where(t => !t.IsRoll && t.Grid.Count >= 2
                        && Math.Abs(t.Grid.LabelWidthMm - widthMm) <= ToleranceMm
                        && Math.Abs(t.Grid.LabelHeightMm - heightMm) <= ToleranceMm)
            .OrderBy(t => Math.Abs(t.Grid.LabelWidthMm - widthMm) + Math.Abs(t.Grid.LabelHeightMm - heightMm))
            .ThenByDescending(t => t.IsUserDefined)
            .ThenBy(t => t.Brand, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The sheet for a Publisher page of this size, or null when the page is not a label or no
    /// sheet has labels this size. The sheet Publisher itself names (its label product, such as
    /// "Avery L7651") wins; then a sheet laid out as Publisher lays out its pages
    /// (<paramref name="layout"/>: the same margins and gaps), the last used first; then the sheet
    /// last used in LabelAxis; then the closest in size.
    /// </summary>
    public static LabelTemplate? Find(TemplateStore store, double widthMm, double heightMm, string? publisherLabel = null, string? lastUsedId = null,
        PubSheetLayout? layout = null)
    {
        if (!IsLabelPage(widthMm, heightMm))
        {
            return null;
        }

        IReadOnlyList<LabelTemplate> sheets = Candidates(store, widthMm, heightMm);
        if (sheets.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(publisherLabel) && sheets.FirstOrDefault(t => Names(publisherLabel, t)) is { } named)
        {
            return named;
        }

        if (layout is not null && sheets.Where(t => ReferenceEquals(FromPublisher(layout, widthMm, heightMm, t), t))
                .OrderByDescending(t => string.Equals(t.Id, lastUsedId, StringComparison.OrdinalIgnoreCase)).FirstOrDefault() is { } laidOut)
        {
            return laidOut;
        }

        return sheets.FirstOrDefault(t => string.Equals(t.Id, lastUsedId, StringComparison.OrdinalIgnoreCase)) ?? sheets[0];
    }

    /// <summary>
    /// The sheet from Publisher's own layout of its pages on the paper (margins and gaps), so the
    /// labels print exactly where Publisher printed them. When <paramref name="match"/>, a built-in
    /// sheet with labels this size, has the same layout (to half a millimetre), it is used as it is;
    /// otherwise a sheet is made from Publisher's numbers, on <paramref name="match"/>'s paper (A4
    /// when there is none; Publisher does not say). Null when the layout gives fewer than two labels.
    /// </summary>
    public static LabelTemplate? FromPublisher(PubSheetLayout layout, double widthMm, double heightMm, LabelTemplate? match)
    {
        ArgumentNullException.ThrowIfNull(layout);
        const double PtToMm = 25.4 / 72.0;
        double left = Math.Round(layout.LeftMarginPt * PtToMm, 2), top = Math.Round(layout.TopMarginPt * PtToMm, 2);
        double across = Math.Round(Math.Max(0, layout.HorizontalGapPt) * PtToMm, 2), down = Math.Round(Math.Max(0, layout.VerticalGapPt) * PtToMm, 2);
        PageSize paper = match?.EffectivePage ?? PageSize.A4;

        int columns = (int)Math.Floor((paper.WidthMm - left + across + 0.05) / (widthMm + across));
        int rows = (int)Math.Floor((paper.HeightMm - top + down + 0.05) / (heightMm + down));
        if (columns < 1 || rows < 1 || columns * rows < 2)
        {
            return null;
        }

        if (match is not null && match.Grid.Columns == columns && match.Grid.Rows == rows
            && Math.Abs(match.Grid.MarginLeftMm - left) <= 0.5 && Math.Abs(match.Grid.MarginTopMm - top) <= 0.5
            && Math.Abs(match.Grid.HGapMm - across) <= 0.5 && Math.Abs(match.Grid.VGapMm - down) <= 0.5)
        {
            return match;
        }

        return new LabelTemplate
        {
            Id = FormattableString.Invariant($"publisher-sheet-{widthMm:0.##}x{heightMm:0.##}-{columns}x{rows}-{left:0.##}-{top:0.##}"),
            Brand = "Publisher",
            ProductCode = $"{columns} × {rows}",
            Name = FormattableString.Invariant($"Publisher label sheet, {columns} × {rows} labels of {widthMm:0.##} × {heightMm:0.##} mm"),
            Categories = new List<string> { "Imported" },
            Page = paper,
            Orientation = PageOrientation.Portrait,
            PrintableMarginMm = match?.PrintableMarginMm ?? new MarginsMm(4.0),
            Grid = new LabelGrid
            {
                Columns = columns,
                Rows = rows,
                LabelWidthMm = widthMm,
                LabelHeightMm = heightMm,
                MarginLeftMm = left,
                MarginTopMm = top,
                HGapMm = across,
                VGapMm = down,
                CornerRadiusMm = match?.Grid.CornerRadiusMm ?? 0,
            },
            Source = "Label layout of an imported Microsoft Publisher document (its margins and gaps)",
        };
    }

    /// <summary>"Avery L7651 Mini Address" names the Avery L7651 sheet: its brand and its code as a whole word.</summary>
    private static bool Names(string label, LabelTemplate sheet)
    {
        if (string.IsNullOrWhiteSpace(sheet.ProductCode) || !label.Contains(sheet.Brand, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return label.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.Equals(sheet.ProductCode, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly char[] WordBreaks = { ' ', '-', '/', '(', ')', ',' };
}
