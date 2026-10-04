using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace LabelMaker.App.Printing;

/// <summary>One drawn page as a document, for sending a single sheet (a calibration target) through the normal print paths.</summary>
public sealed class SinglePagePaginator : DocumentPaginator
{
    private readonly Visual _visual;

    public SinglePagePaginator(Visual visual, Size pageSize)
    {
        _visual = visual;
        PageSize = pageSize;
    }

    public override bool IsPageCountValid => true;

    public override int PageCount => 1;

    public override Size PageSize { get; set; }

    public override IDocumentPaginatorSource? Source => null;

    public override DocumentPage GetPage(int pageNumber) =>
        pageNumber == 0 ? new DocumentPage(_visual, PageSize, new Rect(PageSize), new Rect(PageSize)) : DocumentPage.Missing;
}
