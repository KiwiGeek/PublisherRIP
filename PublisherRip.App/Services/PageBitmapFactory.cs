using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using PublisherRip.App.Models;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace PublisherRip.App.Services;

internal static class PageBitmapFactory
{
    public static bool IsPdf(DocumentSource source)
    {
        if (string.Equals(source.Extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return source.Content.Length >= 4
            && source.Content[0] == '%'
            && source.Content[1] == 'P'
            && source.Content[2] == 'D'
            && source.Content[3] == 'F';
    }

    public static int GetPdfPageCount(DocumentSource source)
    {
        using var stream = CreateRandomAccessStream(source.Content);
        var document = LoadPdfDocument(stream, source.FileName);
        return checked((int)document.PageCount);
    }

    public static BitmapSource CreatePreview(DocumentSource source, int pageIndex, bool isPdf)
    {
        return isPdf
            ? RenderPdfPage(source, pageIndex, new Size(220, 280), 144)
            : LoadImageBitmap(source.Content, 220, 280);
    }

    public static BitmapSource CreatePrintableBitmap(DocumentSource source, int pageIndex, bool isPdf, Size targetSize)
    {
        return isPdf
            ? RenderPdfPage(source, pageIndex, targetSize, 300)
            : LoadImageBitmap(source.Content, null, null);
    }

    private static BitmapSource RenderPdfPage(DocumentSource source, int pageIndex, Size targetSize, double dpi)
    {
        using var sourceStream = CreateRandomAccessStream(source.Content);
        var document = LoadPdfDocument(sourceStream, source.FileName);
        using var page = document.GetPage((uint)pageIndex);
        var renderStream = new InMemoryRandomAccessStream();

        var pixelBounds = CalculateFittedPixelBounds(page.Size.Width, page.Size.Height, targetSize, dpi);
        var renderOptions = new PdfPageRenderOptions
        {
            DestinationWidth = pixelBounds.Width,
            DestinationHeight = pixelBounds.Height
        };

        page.RenderToStreamAsync(renderStream, renderOptions).AsTask().GetAwaiter().GetResult();
        renderStream.Seek(0);
        return CreateBitmapSource(renderStream.AsStreamForRead());
    }

    private static BitmapSource LoadImageBitmap(byte[] content, int? decodePixelWidth, int? decodePixelHeight)
    {
        using var stream = new MemoryStream(content, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;

        if (decodePixelWidth.HasValue)
        {
            bitmap.DecodePixelWidth = decodePixelWidth.Value;
        }

        if (decodePixelHeight.HasValue)
        {
            bitmap.DecodePixelHeight = decodePixelHeight.Value;
        }

        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static InMemoryRandomAccessStream CreateRandomAccessStream(byte[] content)
    {
        var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream);
        writer.WriteBytes(content);
        writer.StoreAsync().AsTask().GetAwaiter().GetResult();
        writer.FlushAsync().AsTask().GetAwaiter().GetResult();
        writer.DetachStream();
        stream.Seek(0);
        return stream;
    }

    private static PdfDocument LoadPdfDocument(IRandomAccessStream stream, string fileName)
    {
        try
        {
            return PdfDocument.LoadFromStreamAsync(stream).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            var details = string.IsNullOrWhiteSpace(ex.Message)
                ? $"{ex.GetType().Name} (0x{ex.HResult:X8})"
                : $"{ex.Message} (0x{ex.HResult:X8})";
            throw new InvalidOperationException($"Unable to open PDF '{fileName}': {details}", ex);
        }
    }

    private static BitmapSource CreateBitmapSource(Stream stream)
    {
        using (stream)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }

    private static (uint Width, uint Height) CalculateFittedPixelBounds(double sourceWidth, double sourceHeight, Size targetSize, double dpi)
    {
        var maxWidth = Math.Max(1, targetSize.Width * dpi / 96d);
        var maxHeight = Math.Max(1, targetSize.Height * dpi / 96d);
        var scale = Math.Min(maxWidth / sourceWidth, maxHeight / sourceHeight);

        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            scale = 1;
        }

        return (
            Math.Max(1u, (uint)Math.Round(sourceWidth * scale)),
            Math.Max(1u, (uint)Math.Round(sourceHeight * scale)));
    }
}
