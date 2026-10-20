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
    /// "Avery L7651") wins; then the sheet last used in LabelAxis; then the closest in size.
    /// </summary>
    public static LabelTemplate? Find(TemplateStore store, double widthMm, double heightMm, string? publisherLabel = null, string? lastUsedId = null)
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

        return sheets.FirstOrDefault(t => t.Id == lastUsedId) ?? sheets[0];
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
