using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using BarePDF.Pdfium;

namespace BarePDF.Views;

internal sealed class PdfPrintPaginator : DocumentPaginator
{
    private const double PrintDpi = 300.0;

    private readonly PdfDocument _document;
    private Size _pageSize;

    public PdfPrintPaginator(PdfDocument document, Size pageSize)
    {
        _document = document;
        _pageSize = pageSize;
    }

    public override bool IsPageCountValid => true;
    public override int PageCount => _document.PageCount;
    public override IDocumentPaginatorSource? Source => null;

    public override Size PageSize
    {
        get => _pageSize;
        set => _pageSize = value;
    }

    public override DocumentPage GetPage(int pageNumber)
    {
        using var page = _document.GetPage(pageNumber);

        var pageWidth = page.WidthPoints * 96.0 / 72.0;
        var pageHeight = page.HeightPoints * 96.0 / 72.0;
        var scale = Math.Min(_pageSize.Width / pageWidth, _pageSize.Height / pageHeight);
        var renderedWidth = pageWidth * scale;
        var renderedHeight = pageHeight * scale;
        var x = (_pageSize.Width - renderedWidth) / 2.0;
        var y = (_pageSize.Height - renderedHeight) / 2.0;

        var bitmap = page.Render(PrintDpi);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(bitmap, new Rect(x, y, renderedWidth, renderedHeight));
        }

        return new DocumentPage(visual, _pageSize, new Rect(_pageSize), new Rect(_pageSize));
    }
}
