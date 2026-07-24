using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PublisherRip.App.Models;

namespace PublisherRip.App.Services;

internal sealed class PrintPagePaginator : DocumentPaginator
{
    private readonly IReadOnlyList<PrintPageItem> _pages;
    private readonly Rect _contentRect;
    private Size _pageSize;

    public PrintPagePaginator(IReadOnlyList<PrintPageItem> pages, Size pageSize, Rect contentRect)
    {
        _pages = pages;
        _pageSize = pageSize;
        _contentRect = contentRect;
    }

    public override bool IsPageCountValid => true;

    public override int PageCount => _pages.Count;

    public override Size PageSize
    {
        get => _pageSize;
        set => _pageSize = value;
    }

    public override IDocumentPaginatorSource? Source => null;

    public override DocumentPage GetPage(int pageNumber)
    {
        var pageItem = _pages[pageNumber];
        var visual = new DrawingVisual();

        using var drawingContext = visual.RenderOpen();
        drawingContext.DrawRectangle(Brushes.White, null, new Rect(new Point(0, 0), _pageSize));

        var bitmap = PageBitmapFactory.CreatePrintableBitmap(pageItem.Source, pageItem.PageIndex, pageItem.IsPdf, _contentRect.Size);
        var drawRect = CalculateFitRect(bitmap, _contentRect);
        drawingContext.DrawImage(bitmap, drawRect);

        return new DocumentPage(visual, _pageSize, new Rect(new Point(0, 0), _pageSize), _contentRect);
    }

    private static Rect CalculateFitRect(BitmapSource bitmap, Rect bounds)
    {
        var dpiX = bitmap.DpiX <= 0 ? 96 : bitmap.DpiX;
        var dpiY = bitmap.DpiY <= 0 ? 96 : bitmap.DpiY;
        var width = bitmap.PixelWidth * 96d / dpiX;
        var height = bitmap.PixelHeight * 96d / dpiY;
        var scale = Math.Min(bounds.Width / width, bounds.Height / height);

        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            scale = 1;
        }

        var scaledWidth = width * scale;
        var scaledHeight = height * scale;
        var x = bounds.X + ((bounds.Width - scaledWidth) / 2d);
        var y = bounds.Y + ((bounds.Height - scaledHeight) / 2d);
        return new Rect(x, y, scaledWidth, scaledHeight);
    }
}
