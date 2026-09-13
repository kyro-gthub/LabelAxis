using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using LabelMaker.App.Rendering;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Printing;

/// <summary>
/// A <see cref="DocumentPaginator"/> that prints the project's standalone pages
/// (spec §5) several-to-a-sheet, each in its own slot — "print multiple pages in
/// one sheet" (spec §15). Same physical-accuracy rules as <see cref="LabelSheetPaginator"/>.
/// </summary>
public sealed class MultiPageSheetPaginator : DocumentPaginator
{
    /// <summary>Sheets, labels and records in this job, for the dialog and the print log.</summary>
    public PrintJobSummary Summary { get; init; } = new();

    private const double K = UnitConverter.DiuPerInch / UnitConverter.MmPerInch;

    private readonly List<PlannedMultiSheet> _sheets;
    private readonly LabelTemplate _template;
    private readonly SheetVisualRenderer _renderer;
    private readonly PrinterCalibration _calibration;
    private readonly double _userScalePercent;
    private readonly SheetMedia _media;
    private readonly LabelRenderOptions _options;

    public MultiPageSheetPaginator(
        IEnumerable<PlannedMultiSheet> sheets,
        LabelTemplate template,
        SheetVisualRenderer renderer,
        SheetMedia media,
        PrinterCalibration calibration,
        double userScalePercent,
        LabelOutlineStyle outline)
    {
        _sheets = sheets.ToList();
        _template = template;
        _renderer = renderer;
        _media = media;
        _calibration = calibration;
        _userScalePercent = userScalePercent;
        _options = renderer.OptionsFor(template, outline);

        PageSize = new Size(_media.PageWidthMm * K, _media.PageHeightMm * K);
    }

    public override bool IsPageCountValid => true;

    public override int PageCount => _sheets.Count;

    public override Size PageSize { get; set; }

    public override IDocumentPaginatorSource? Source => null;

    public override DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= _sheets.Count)
        {
            return DocumentPage.Missing;
        }

        var mapper = new PrintCoordinateMapper(_media, _calibration, _userScalePercent);
        DrawingVisual sheet = _renderer.RenderMultiSheet(_template, _sheets[pageNumber].Documents, _options, mapper);

        var page = new ContainerVisual();
        page.Children.Add(sheet);

        var box = new Size(_media.PageWidthMm * K, _media.PageHeightMm * K);
        return new DocumentPage(page, box, new Rect(box), new Rect(box));
    }
}
